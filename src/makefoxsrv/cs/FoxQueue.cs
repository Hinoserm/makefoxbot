using Autofocus;
using Autofocus.Models;
using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WTelegram;
using makefoxsrv;
using TL;
using System.Security.Policy;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;
using System.Runtime.CompilerServices;

namespace makefoxsrv
{
    internal partial class FoxQueue
    {
        public enum QueueStatus
        {
            PENDING,
            PROCESSING,
            SENDING,
            FINISHED,
            CANCELLED,
            ERROR            
        }
        public enum QueueType
        {
            UNKNOWN,
            IMG2IMG,
            TXT2IMG
        }
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);

        [DbColumn("status")]
        public QueueStatus status = QueueStatus.PENDING;

        [DbColumn("retry_date")]
        public DateTime? RetryDate { get; private set; } = null;

        [DbColumn("date_failed")]
        public DateTime? DateLastFailed { get; private set; } = null;

        [DbColumn("retry_count")]
        public int RetryCount { get; private set; } = 0;

        [DbColumnMapping("LastException.Message", "error_str", true)]
        public Exception? LastException { get; private set; } = null;

        [DbInclude()]
        public FoxUserSettings Settings = new();

        [DbColumnMapping("User.UID", "uid")]
        public FoxUser? User = null;

        [DbColumn("id")]
        public ulong ID { get; private set; }
        [DbColumn("reply_msg")]
        public int? ReplyMessageID { get; private set; }

        [DbColumn("type")]
        public QueueType Type { get; private set; }

        [DbColumn("msg_id")]
        public int MessageID { get; private set; }

        [DbColumn("date_added")]
        public DateTime DateCreated { get; private set; }

        [DbColumn("date_worker_start")]
        public DateTime? DateStarted { get; private set; } = null;

        [DbColumn("date_sent")]
        public DateTime? DateSent { get; private set; } = null;

        [DbColumn("date_finished")]
        public DateTime? DateFinished { get; private set; } = null;

        [DbColumn("link_token")]
        public string? LinkToken { get; private set; } = null;

        [DbColumn("enhanced")]
        public bool Enhanced = false;

        [DbColumn("regional_prompting")]
        public bool RegionalPrompting = false;

        [DbColumn("original_id")]
        public ulong? OriginalID { get; set; } // Used for tracking the original ID for Enhanced tasks

        [DbColumnMapping("Telegram.User.ID", "tele_id", true)]
        [DbColumnMapping("Telegram.Chat.ID", "tele_chatid", true)]
        public FoxTelegram? Telegram { get; private set; }

        [DbColumn("image_id")]
        public ulong? OutputImageID { get; set; }

        private FoxImage? _outputImage;
        private FoxImage? _inputImage;

        public CancellationTokenSource stopToken { get; private set; } = new();

        private Stopwatch UserNotifyTimer = new Stopwatch();
        public FoxWorker? Worker { get; private set; } = null;

        [DbColumn("worker")]
        public int? WorkerID { get; private set; } = null;

        private static readonly object lockObj = new object();
        private static List<(FoxQueue task, int priority, DateTime dateStarted)> taskList = new List<(FoxQueue task, int priority, DateTime dateStarted)>();

        public static LimitedMemoryQueue<FoxQueue> fullQueue { get; private set; } = new(400); //Store the last 400 queue items.

        private static Dictionary<AccessLevel, int> priorityMap = new Dictionary<AccessLevel, int>
            {
                { AccessLevel.ADMIN, 4 },  // Highest priority
                { AccessLevel.PREMIUM, 3 },
                { AccessLevel.BASIC, 2 },
                { AccessLevel.BANNED, 1 }   // Lowest priority
            };
        private static SemaphoreSlim queueSemaphore = new SemaphoreSlim(0, int.MaxValue);

        private static ConcurrentQueue<FoxQueue> delayedTasks = new ConcurrentQueue<FoxQueue>();
        private static Timer? delayedTaskTimer = null;

        static FoxQueue()
        {
            FoxWorker.OnTaskCompleted += (sender, args) => _ = queueSemaphore.Release();
            FoxWorker.OnTaskCompleted += (sender, args) => _ = queueSemaphore.Release();
            FoxWorker.OnWorkerStart += (sender, args) => _ = queueSemaphore.Release();
            FoxWorker.OnWorkerOnline += (sender, args) => _ = queueSemaphore.Release();
        }

        public static long ClearCache()
        {
            return 0; //Not yet implemented.
        }

        public static async Task Enqueue(FoxQueue item)
        {
            fullQueue.Enqueue(item);

            if (item.RetryDate.HasValue && item.RetryDate.Value > DateTime.Now)
            {
                // If the item has a RetryDate in the future, add it to the delayed tasks instead of the main queue
                delayedTasks.Enqueue(item);

                // Initialize or reset a timer to check delayed tasks periodically
                if (delayedTaskTimer == null)
                {
                    var delay = item.RetryDate.Value - DateTime.Now;

                    delayedTaskTimer = new Timer(ProcessDelayedTasks, null, delay, TimeSpan.FromSeconds(10));
                }

                FoxLog.WriteLine($"Delaying task {item.ID} until {item.RetryDate}.", LogLevel.DEBUG);
            }
            else
            {
                lock (lockObj)
                {
                    //int priorityLevel = item.Enhanced ? 1 : priorityMap[item.User.GetAccessLevel()];
                    int priorityLevel = priorityMap[item.User.GetAccessLevel()];
                    taskList.Add((item, priorityLevel, item.DateCreated));

                    // Sort by priority, then by DateStarted as a secondary criteria
                    taskList.Sort((x, y) => x.priority != y.priority ? y.priority.CompareTo(x.priority) : x.dateStarted.CompareTo(y.dateStarted));
                }
                queueSemaphore.Release();

                FoxLog.WriteLine($"Enqueueing task {item.ID}.", LogLevel.DEBUG);
            }
        }

        private static void ProcessDelayedTasks(object? state)
        {
            int count = delayedTasks.Count;
            for (int i = 0; i < count; i++)
            {
                if (delayedTasks.TryDequeue(out FoxQueue? task) && task.RetryDate.HasValue)
                {
                    if (DateTime.UtcNow >= task.RetryDate.Value)
                    {
                        // Task's retry time has passed; re-enqueue it for processing
                        FoxLog.WriteLine($"Enqueueing delayed task {task.ID}.", LogLevel.DEBUG);
                        _ = Enqueue(task);
                    }
                    else
                    {
                        // Still waiting for retry time, put it back in the queue
                        delayedTasks.Enqueue(task);
                    }
                }
            }

            // Check if there are no more delayed tasks and stop the timer
            if (delayedTasks.IsEmpty)
            {
                delayedTaskTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                delayedTaskTimer?.Dispose();
                delayedTaskTimer = null;
            }
        }

        public static void StartTaskLoop(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        FoxQueue? itemToAssign = null;
                        FoxWorker? suitableWorker = null;

                        //FoxLog.WriteLine("Waiting for task...", LogLevel.DEBUG);
                        await queueSemaphore.WaitAsync(5000, cancellationToken);

                        //FoxLog.WriteLine("Locking...", LogLevel.DEBUG);
                        lock (lockObj)
                        {
                            for (int i = 0; i < taskList.Count; i++)
                            {
                                var potentialItem = taskList[i].task;
                                suitableWorker = FindSuitableWorkerForTask(potentialItem);
                                if (suitableWorker != null)
                                {
                                    // Found a suitable worker for the task
                                    itemToAssign = potentialItem;
                                    taskList.RemoveAt(i); // Remove the task from the list since it's being assigned
                                    break; // Exit the loop as we've found a task to assign
                                }
                            }
                        }

                        if (itemToAssign is not null)
                        {
                            if (itemToAssign.status == QueueStatus.CANCELLED)
                            {
                                FoxLog.WriteLine($"Task {itemToAssign.ID} was cancelled, skipping.", LogLevel.DEBUG);
                                continue;
                            }
                            FoxLog.WriteLine($"Found {itemToAssign.ID} for worker {suitableWorker?.name ?? "None"}", LogLevel.DEBUG);
                        }

                        if (itemToAssign is not null && suitableWorker is not null)
                        {
                            if (!suitableWorker.AssignTask(itemToAssign))
                            {
                                FoxLog.WriteLine($"Failed to assign task {itemToAssign.ID} to {suitableWorker.name}", LogLevel.DEBUG);
                                _ = Enqueue(itemToAssign); // Re-enqueue the task
                            }
                            else
                            {
                                FoxLog.WriteLine($"Assigned task {itemToAssign.ID}  to {suitableWorker?.name}", LogLevel.DEBUG);
                            }
                        }
                        else if (itemToAssign is not null)
                            _ = Enqueue(itemToAssign); // No suitable worker found, re-enqueue the task

                    }
                    catch (OperationCanceledException)
                    {
                        FoxLog.WriteLine("Task loop cancelled.", LogLevel.DEBUG);
                        break;
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex);
                    }
                }
            });
        }

        public static FoxWorker? CheckWorkerAvailability(FoxUserSettings settings)
        {
            var workers = FoxWorker.GetWorkers().Values;

            // Fetch the model by name using the new FoxModel method
            var model = FoxModel.GetModelByName(settings.model);

            uint width = Math.Max(settings.width, settings.hires_width);
            uint height = Math.Max(settings.height, settings.hires_height);

            // If the model does not exist or has no workers, return null
            if (model is null || model.GetWorkersRunningModel().Count < 1)
            {
                return null; // No workers available for the specified model
            }

            // Filter out workers based on their enabled status, max image size, steps capacity,
            // the availability of the model, and regional prompting support if required.
            var capableWorkers = workers
                .Where(worker => (!worker.MaxImageSize.HasValue || (width * height) <= worker.MaxImageSize.Value)
                                 && (!worker.MaxImageSteps.HasValue || settings.steps <= worker.MaxImageSteps.Value)
                                 && model.GetWorkersRunningModel().Contains(worker.ID) // Check if the worker has the model loaded
                                 && (!settings.regionalPrompting || worker.SupportsRegionalPrompter)) // Check regional prompting condition
                .FirstOrDefault(); // Immediately return the first capable worker found

            return capableWorkers; // Could be null if no capable workers are found
        }

        public static FoxWorker? FindSuitableWorkerForTask(FoxQueue item)
        {
            // Get the model from the global FoxModel system
            var model = FoxModel.GetModelByName(item.Settings.model);

            uint width = Math.Max(item.Settings.width, item.Settings.hires_width);
            uint height = Math.Max(item.Settings.height, item.Settings.hires_height);

            // If the model does not exist or has no workers running it, return null (no suitable worker found)
            if (model == null || model.GetWorkersRunningModel().Count < 1)
            {
                return null;
            }

            // Get all workers
            var workers = FoxWorker.GetWorkers().Values;

            // Filter out workers based on their online status, image size, steps capacity,
            // and whether they have the model loaded (this avoids redundant checks later).
            var suitableWorkers = workers
                .Where(worker => worker.Online
                                 && worker.qItem == null  // Worker is not currently busy
                                 && (!worker.MaxImageSize.HasValue || (width * height) <= worker.MaxImageSize.Value)
                                 && (!worker.MaxImageSteps.HasValue || item.Settings.steps <= worker.MaxImageSteps.Value)
                                 && (!item.RegionalPrompting || worker.SupportsRegionalPrompter)
                                 && model.GetWorkersRunningModel().Contains(worker.ID))  // Ensure the worker has the model loaded
                .ToList();

            // Prioritize workers who already have the model as their last used model (and still have it loaded)
            var preferredWorkers = suitableWorkers
                .Where(worker => worker.LastUsedModel == item.Settings.model)  // Worker last used this model
                .OrderByDescending(worker => priorityMap[item.User.GetAccessLevel()]) // Sort by user access level priority
                .ToList();

            // If there are any preferred workers, return the first one
            if (preferredWorkers.Any())
            {
                var w = preferredWorkers.First();
                return w;
            }

            // If no preferred workers are available, fall back to any suitable worker
            var suitableWorker = suitableWorkers
                .OrderByDescending(worker => priorityMap[item.User.GetAccessLevel()]) // Sort by user access level priority
                .FirstOrDefault();

            return suitableWorker;
        }



        public static async Task EnqueueOldItems()
        {
            int count = 0;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = @"
                        SELECT q.*,
                                tu.access_hash AS user_access_hash, 
                                tc.access_hash AS chat_access_hash
                        FROM queue q
                        LEFT JOIN telegram_users tu ON tu.id = q.tele_id
                        LEFT JOIN telegram_chats tc ON tc.id = q.tele_chatid
                        WHERE 
                            q.status IN ('PENDING', 'ERROR', 'PROCESSING', 'SENDING')
                        ORDER BY 
                            q.date_added ASC
                        ";

                using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    var q = await FoxDB.LoadObjectAsync<FoxQueue>(r);

                    q.User = await FoxUser.GetByUID(Convert.ToInt64(r["uid"]));

                    long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];

                    var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                    var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                    if (teleUser is null)
                        continue; //Something went wrong, skip this one.

                    q.Telegram = new FoxTelegram(teleUser, teleChat);

                    count++;
                    await Enqueue(q);
                }
            }

            FoxLog.WriteLine($"Added {count} saved tasks to queue.");
        }

        public static async Task<int> GetTotalCount(FoxUser user)
        {
            int totalCount = 0;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = @"
                    SELECT COUNT(*) AS total_count
                    FROM queue 
                    WHERE uid = @userID AND status = 'FINISHED'";

                cmd.Parameters.AddWithValue("@userID", user.UID);

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    totalCount = Convert.ToInt32(r["total_count"]);
                }
            }

            return totalCount;
        }

        public static async Task<int> GetRecentCount(FoxUser user, TimeSpan recentWindow)
        {
            int recentCount = 0;
            DateTime recentThreshold = DateTime.Now.Subtract(recentWindow); // Using local time as per your requirements

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = @"
                    SELECT COUNT(*) AS recent_count
                    FROM queue 
                    WHERE uid = @userID AND date_added >= @recentThreshold AND status != 'CANCELLED'";

                cmd.Parameters.AddWithValue("@userID", user.UID);
                cmd.Parameters.AddWithValue("@recentThreshold", recentThreshold);

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    recentCount = Convert.ToInt32(r["recent_count"]);
                }
            }

            return recentCount;
        }

        public async Task SetStatus(QueueStatus status, int? newMessageID = null)
        {
            this.status = status;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using var cmd = new MySqlCommand();

            if (newMessageID is not null)
                MessageID = newMessageID.Value;

            cmd.Connection = SQL;

            switch (status)
            {
                case QueueStatus.PENDING:
                    cmd.CommandText = "UPDATE queue SET status = 'PENDING' WHERE id = @id";
                    break;
                case QueueStatus.FINISHED:
                    DateFinished = DateTime.Now;
                    cmd.CommandText = "UPDATE queue SET status = 'FINISHED', date_finished = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("now", DateFinished);
                    break;
                case QueueStatus.PROCESSING:
                    DateStarted = DateTime.Now;
                    cmd.CommandText = "UPDATE queue SET status = 'PROCESSING', date_worker_start = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("now", DateStarted);
                    break;
                case QueueStatus.SENDING:
                    DateSent = DateTime.Now;
                    cmd.CommandText = "UPDATE queue SET status = 'SENDING', date_sent = @now, msg_id = @msg_id WHERE id = @id";
                    cmd.Parameters.AddWithValue("now", DateSent);
                    cmd.Parameters.AddWithValue("msg_id", newMessageID);
                    break;
                case QueueStatus.CANCELLED:
                    DateLastFailed = DateTime.Now;
                    cmd.CommandText = "UPDATE queue SET status = 'CANCELLED', date_failed = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("now", DateLastFailed);
                    break;
                case QueueStatus.ERROR:
                    throw new Exception("Use SetError() to set error status");
                default:
                    cmd.CommandText = "UPDATE queue SET status = @status WHERE id = @id";
                    cmd.Parameters.AddWithValue("status", status.ToString());
                    break;
            }

            cmd.Parameters.AddWithValue("id", this.ID);
            
            await cmd.ExecuteNonQueryAsync();

            FoxLog.WriteLine($"Task {this.ID} status set to {status}", LogLevel.DEBUG);
        }

        public static (int hypotheticalPosition, int totalItems) GetNextPosition(FoxUser user, bool Enhanced)
        {
            int priorityLevel = priorityMap[user.GetAccessLevel()];
            DateTime dateStarted = DateTime.Now;
            int hypotheticalPosition = 1; // Start from 1 to match your existing indexing logic

            lock (lockObj)
            {
                int totalItems = taskList.Count;

                foreach (var (task, priority, started) in taskList)
                {
                    // If the hypothetical task would come after the current task in the list
                    if (priority < priorityLevel || (priority == priorityLevel && started > dateStarted))
                    {
                        break; // Found where the hypothetical task would fit
                    }
                    hypotheticalPosition++;
                }

                return (hypotheticalPosition, totalItems + 1); // +1 because we're considering the task as if it were added
            }
        }

        public (int position, int totalItems) GetPosition()
        {
            int position = 1; // Start counting positions from 1
            int totalItems = 0; // Initialize total items count

            lock (lockObj)
            {
                totalItems = taskList.Count; // Get the total number of items in the list

                foreach (var (task, _, _) in taskList)
                {
                    if (task.ID == this.ID)
                    {
                        break; // Found the item, break the loop
                    }
                    position++;
                }

                // If the task is not found, reset position to indicate it's not in the list
                if (position > totalItems) position = -1;
            }

            return (position, totalItems);
        }

        public static async Task<FoxQueue?> Add(FoxTelegram telegram, FoxUser user, FoxUserSettings taskSettings, QueueType type, int messageID, int? replyMessageID = null, bool enhanced = false, FoxQueue? originalTask = null, TimeSpan? delay = null)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            FoxUserSettings settings = taskSettings.Copy();

            if (type == QueueType.TXT2IMG && (settings.width > 1088 || settings.height > 1088))
            {
                settings.hires_denoising_strength = 0.5M;
                settings.hires_steps = 15;
                settings.hires_width = settings.width;
                settings.hires_height = settings.height;
                settings.hires_enabled = true;

                (settings.width, settings.height) = FoxImage.CalculateLimitedDimensions(settings.width, settings.height, 1024);
            }

            if (settings.seed == -1)
                settings.seed = GetRandomInt32();

            var q = new FoxQueue
            {
                Telegram = telegram,
                User = user,
                DateCreated = DateTime.Now,
                Type = type,
                Settings = settings,
                MessageID = messageID,
                ReplyMessageID = replyMessageID,
                Enhanced = enhanced,
                OriginalID = originalTask?.ID,
                RetryDate = delay.HasValue ? DateTime.Now.Add(delay.Value) : null,
                RegionalPrompting = settings.regionalPrompting
            };

            if (type == QueueType.IMG2IMG && !(await FoxImage.IsImageValid(settings.selected_image)))
                throw new Exception("Invalid input image");

            await FoxDB.SaveObjectAsync(q, "queue");

            //using var cmd = new MySqlCommand();

            //cmd.Connection = SQL;
            //cmd.CommandText = @"
            //    INSERT INTO queue (status, type, uid, tele_id, tele_chatid, steps, cfgscale, prompt, negative_prompt, selected_image, width, height, denoising_strength, seed, enhanced, reply_msg, msg_id, date_added, model, upscaler_name, upscaler_denoise, upscaler_width, upscaler_height, upscaler_steps)
            //    VALUES ('PENDING', @type, @uid, @tele_id, @tele_chatid, @steps, @cfgscale, @prompt, @negative_prompt, @selected_image, @width, @height, @denoising_strength, @seed, @enhanced, @reply_msg, @msg_id, @now, @model, @upscaler_name, @upscaler_denoise, @upscaler_width, @upscaler_height, @upscaler_steps)
            //";

            //cmd.Parameters.AddWithValue("uid", user.UID);
            //cmd.Parameters.AddWithValue("type", q.Type.ToString());
            //cmd.Parameters.AddWithValue("tele_id", telegram.User.ID);
            //cmd.Parameters.AddWithValue("tele_chatid", telegram.Chat?.ID);
            //cmd.Parameters.AddWithValue("steps", settings.steps);
            //cmd.Parameters.AddWithValue("cfgscale", settings.cfgscale);
            //cmd.Parameters.AddWithValue("prompt", settings.prompt);
            //cmd.Parameters.AddWithValue("negative_prompt", settings.negative_prompt);
            //cmd.Parameters.AddWithValue("selected_image", type == QueueType.IMG2IMG ? settings.selected_image : null);
            //cmd.Parameters.AddWithValue("width", settings.width);
            //cmd.Parameters.AddWithValue("height", settings.height);
            //cmd.Parameters.AddWithValue("denoising_strength", type == QueueType.IMG2IMG ? settings.denoising_strength : null);
            //cmd.Parameters.AddWithValue("seed", settings.seed);
            //cmd.Parameters.AddWithValue("reply_msg", replyMessageID);
            //cmd.Parameters.AddWithValue("msg_id", messageID);
            //cmd.Parameters.AddWithValue("now", q.DateCreated);
            //cmd.Parameters.AddWithValue("model", settings.model);
            //cmd.Parameters.AddWithValue("enhanced", settings.Enhance);
            //cmd.Parameters.AddWithValue("upscaler_name", settings.UpscalerName);
            //cmd.Parameters.AddWithValue("upscaler_denoise", settings.UpscalerDenoiseStrength);
            //cmd.Parameters.AddWithValue("upscaler_width", settings.UpscalerWidth);
            //cmd.Parameters.AddWithValue("upscaler_height", settings.UpscalerHeight);
            //cmd.Parameters.AddWithValue("upscaler_steps", settings.UpscalerSteps);

            //await cmd.ExecuteNonQueryAsync();

            

            //q.ID = (ulong)cmd.LastInsertedId;

            return q;
        }

        public async Task Start(FoxWorker worker) {
            UserNotifyTimer.Start();
            this.Worker = worker;

            FoxLog.WriteLine($"Task {this.ID} started on worker {worker.name}", LogLevel.DEBUG);

            await this.SetStatus(QueueStatus.PROCESSING);

            try
            {
                if (this.Telegram is not null)
                {
                    await this.Telegram.EditMessageAsync(
                        id: this.MessageID,
                        text: $"⏳ Generating now on {worker.name}..."
                    );
                }
            }
            catch (WTelegram.WTException ex) when (ex is RpcException rex && rex.Code == 400 && (rex.Message == "MESSAGE_NOT_MODIFIED" || rex.Message == "MESSAGE_ID_INVALID"))
            {
                //Ignore these telegram errors.
            }
        }

        public async Task Progress(FoxWorker worker, IProgress p, double progressPercent)
        {
            try
            {
                if (this.UserNotifyTimer.ElapsedMilliseconds >= 2000 && this.Telegram is not null && this.Telegram.Chat is null)
                {
                    //Due to the stricter rate limits on editing messages in groups, we only notify the user in private chats.
                    await this.Telegram.EditMessageAsync(
                        id: this.MessageID,
                        text: $"⏳ Generating now on {worker.name} ({(int)progressPercent}%)..."
                    );

                    this.UserNotifyTimer.Restart();
                }
            }
            catch (WTelegram.WTException ex) when (ex is RpcException rex && rex.Code == 400 && (rex.Message == "MESSAGE_NOT_MODIFIED" || rex.Message == "MESSAGE_ID_INVALID"))
            {
                //Ignore these telegram errors.
            }
        }

        public static string GenerateShortId(int length = 8)
        {
            const string AlphanumericCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

            if (length <= 0 || length > 22) // Length limited due to the size of the MD5 hash
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be between 1 and 22.");

            // Create a new GUID and convert it to a byte array
            byte[] guidBytes = Guid.NewGuid().ToByteArray();

            // Compute the MD5 hash of the GUID
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(guidBytes);

                // Convert the hash to a base-62 string
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(AlphanumericCharacters[b % AlphanumericCharacters.Length]);
                }

                // Truncate or pad the string to the specified length
                return sb.ToString()[..length];
            }
        }

        public async Task GenerateLinkToken()
        {
            string token;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                while (true)
                {
                    token = GenerateShortId(12);

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "SELECT COUNT(id) FROM queue WHERE link_token = @token";
                        cmd.Parameters.AddWithValue("token", token);
                        await using var reader = await cmd.ExecuteReaderAsync();
                        reader.Read();
                        if (reader.GetInt32(0) <= 0)
                            break;
                    }
                }
            }

            this.LinkToken = token;

            return;
        }
        //public async Task<long> CheckPosition()
        //{
        //    using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
        //    {
        //        await SQL.OpenAsync();

        //        using (var cmd = new MySqlCommand())
        //        {
        //            cmd.Connection = SQL;
        //            cmd.CommandText = cmd.CommandText = @"
        //                SELECT
        //                    q1.position_in_queue,
        //                    q1.total_pending
        //                FROM
        //                    (SELECT
        //                            id,
        //                            ROW_NUMBER() OVER(ORDER BY date_added ASC) AS position_in_queue,
        //                            COUNT(*) OVER() AS total_pending
        //                        FROM
        //                            queue
        //                        WHERE
        //                            status = 'PENDING') AS q1
        //                WHERE
        //                    q1.id = @id;
        //                ";
        //            cmd.Parameters.AddWithValue("id", this.ID);

        //            await using var r = await cmd.ExecuteReaderAsync();
        //            if (r.HasRows && await r.ReadAsync())
        //            {
        //                this.position = Convert.ToInt64(r["position_in_queue"]);
        //                this.total    = Convert.ToInt64(r["total_pending"]);

        //                return this.position;
        //            }
        //        }
        //    }

        //    return (long)this.ID;
        //}

        public static async Task<FoxQueue?> GetNewestFromUser(FoxUser user, long? tgChatId = null)
        {
            // Attempt to find the FoxQueue item in the fullQueue cache

            // Attempt to find the FoxQueue item in the fullQueue cache
            var cachedItem = fullQueue.FirstOrDefault(fq => fq.User.UID == user.UID && (tgChatId == null || fq.Telegram?.Peer?.ID == tgChatId));

            if (cachedItem is not null)
            {
                // If found in cache, return the cached item
                return cachedItem;
            }

            var parameters = new Dictionary<string, object?>
            {
                { "@uid", user.UID },
                { "@peerId", tgChatId }
            };

            var q = await FoxDB.LoadObjectAsync<FoxQueue>("queue", "uid = @uid AND (@peerId IS NULL OR tele_chatid = @peerId) ORDER BY id DESC LIMIT 1", parameters, async (o, r) =>
            {
                long uid = Convert.ToInt64(r["uid"]);
                o.User = await FoxUser.GetByUID(uid);

                long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];

                var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                if (teleUser is not null)
                    o.Telegram = new FoxTelegram(teleUser, teleChat);
            });

            // After loading, add the object to the fullQueue cache if it's not null
            if (q is not null)
                fullQueue.Enqueue(q);

            return q;
        }

        public static async Task<FoxQueue?> Get(ulong id)
        {
            // Attempt to find the FoxQueue item in the fullQueue cache
            var cachedItem = fullQueue.FindAll(fq => fq.ID == id).FirstOrDefault();

            if (cachedItem is not null)
            {
                // If found in cache, return the cached item
                return cachedItem;
            }

            var parameters = new Dictionary<string, object>
            {
                { "@id", id }
            };

            var q = await FoxDB.LoadObjectAsync<FoxQueue>("queue", "id = @id", parameters, async (o, r) =>
            {
                long uid = Convert.ToInt64(r["uid"]);
                o.User = await FoxUser.GetByUID(uid);

                long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];

                var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                if (teleUser is not null)
                    o.Telegram = new FoxTelegram(teleUser, teleChat);
            });

            // After loading, add the object to the fullQueue cache if it's not null
            if (q is not null)
                fullQueue.Enqueue(q);

            return q;
        }

        public static async Task<int> GetCount(FoxUser? user = null)
        {
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT COUNT(id) FROM queue WHERE (status = 'PENDING' OR status = 'ERROR' OR status = 'PROCESSING')";
                    if (user is not null)
                        cmd.CommandText += " AND uid = " + user.UID;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows && await reader.ReadAsync())
                        return reader.GetInt32(0);
                }
            }

            return 0;
        }

        public async Task<TimeSpan> GetGPUTime()
        {
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT date_sent, date_worker_start FROM queue WHERE (id = {ID})";

                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                        return Convert.ToDateTime(r["date_sent"]).Subtract(Convert.ToDateTime(r["date_worker_start"]));
                }
            }

            return TimeSpan.Zero;
        }

        public async Task Send(byte[] image)
        {
            try
            {
                FoxLog.WriteLine($"Sending task {this.ID} to sendqueue...", LogLevel.DEBUG);
                await this.SaveOutputImage(image);
                await this.SetStatus(QueueStatus.SENDING);

                this.Worker = null;

                _ = FoxSendQueue.Send(this);
            }
            catch (Exception ex)
            {
                await this.SetError(ex);
            }
        }

        public async Task SetWorker(FoxWorker worker)
        {
            this.Worker = worker;
            this.WorkerID = worker.ID;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET worker = @worker, date_worker_start = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.ID);
                    cmd.Parameters.AddWithValue("worker", worker.ID);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        

        public async Task SetError(Exception ex, DateTime? RetryWhen = null, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            this.status = QueueStatus.ERROR;
            this.DateLastFailed = DateTime.Now;
            this.LastException = ex;

            const int maxRetries = 4; // Set the max number of retries here

            bool shouldRetry = true;

            // List of silent error strings. If any of these are encountered, the task will be retried immediately.
            var silentErrors = new List<string> {
                "out of memory",
                "xpected all tensors to be on",
                "onnection refused",
                "ould not connect to server",
                "rror occurred while sending",
                "rror while copying content to a stream"
            };

            // List of fatal errors. If any of these are encountered, the task will not be retried.
            var fatalErrors = new List<string> {
                "ould not convert string to float",
                "CHANNEL_PRIVATE",
                "CHAT_SEND_PHOTOS_FORBIDDEN"
            };

            // Check if the error message contains any silent error strings
            bool isSilentError = silentErrors.Any(silentError => ex.Message.Contains(silentError));

            // Check if the error message contains any silent error strings
            bool isFatalRrror = fatalErrors.Any(fatalErrStr => ex.Message.Contains(fatalErrStr));

            // Set retry date
            if (isSilentError)
            {
                this.RetryDate = DateTime.Now.AddSeconds(3); // Retry in 3 seconds for silent errors
            }
            else
            {
                this.RetryDate = RetryWhen ?? DateTime.Now.AddSeconds(5); // Use provided retry time for non-silent errors
                this.RetryCount++;
            }

            if (this.RetryCount >= maxRetries) 
            {
                shouldRetry = false;
            }

            if (isFatalRrror)
                shouldRetry = false;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = 'ERROR', error_str = @error, retry_date = @retry_date, retry_count = @retry_count, date_failed = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.ID);
                    cmd.Parameters.AddWithValue("retry_date", RetryWhen);
                    cmd.Parameters.AddWithValue("retry_count", RetryCount);
                    cmd.Parameters.AddWithValue("error", ex.Message);
                    cmd.Parameters.AddWithValue("now", this.DateLastFailed);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            try
            {
                if (Telegram is not null)
                {
                    var messageBuilder = new StringBuilder();

                    if (isFatalRrror)
                    {
                        messageBuilder.Append($"❌ Encountered a fatal error.");
                    }
                    else if (!shouldRetry)
                    {
                        messageBuilder.Append($"❌ Encountered an error.  Giving up after {this.RetryCount} attempts.");
                    } else {
                        messageBuilder.Append($"⏳ Encountered an error. ");
                    }

                    if (shouldRetry) {
                        if (this.RetryDate is not null)
                        {
                            TimeSpan delay = this.RetryDate.Value - DateTime.Now;

                            messageBuilder.Append($"Retrying in {delay.TotalSeconds:F0} seconds. ({this.RetryCount}/{maxRetries})");
                        }
                        else
                        {
                            messageBuilder.Append($"Retrying... ({this.RetryCount}/{maxRetries})");
                        }
                    }

                    if (!isSilentError)
                    {
                        messageBuilder.Append($"\n\n{ex.Message}");
                    }

                    _ = Telegram.EditMessageAsync(
                        id: MessageID,
                        text: messageBuilder.ToString()
                    );
                }
            }
            catch { }

            FoxLog.LogException(ex, null, callerName, callerFilePath, lineNumber);

            if (shouldRetry)
            {
                await Enqueue(this);
            } else {
                await this.SetCancelled(true);
            }
        }

        public static int GetRandomInt32()
        {
            Random random = new Random();
            int number;
            do
            {
                number = random.Next(int.MinValue, int.MaxValue);
            }
            while (number == -1 || number == 0);

            return number;
        }

        public async Task SetCancelled(bool silent = false)
        {
            await this.SetStatus(QueueStatus.CANCELLED);

            if (Telegram is not null && !silent)
            {
                try
                {
                    await this.Telegram.EditMessageAsync(
                        id: this.MessageID,
                        text: "❌ Cancelled."
                    );
                }
                catch { }
            }

            FoxLog.WriteLine($"Task {this.ID} cancelled.", LogLevel.DEBUG);
        }

        public bool IsFinished()
        {
            return this.status == QueueStatus.FINISHED || this.status == QueueStatus.CANCELLED;
        }

        public async Task Cancel()
        {
            lock (lockObj)
            {
                // Find the task index using its ID. Assuming 'task.ID' can uniquely identify each task.
                var taskIndex = taskList.FindIndex(t => t.task.ID == this.ID);

                if (taskIndex != -1) // Check if the task was found
                {
                    taskList.RemoveAt(taskIndex); // Remove the found task from the list
                    FoxLog.WriteLine($"Cancelled and removed task {this.ID}.", LogLevel.DEBUG);
                }
                else
                {
                    FoxLog.WriteLine($"Task {this.ID} not found for cancellation.", LogLevel.DEBUG);
                }
            }

            await this.SetCancelled();
            this.stopToken.Cancel();
        }

        public async Task<FoxImage> GetInputImage()
        {
            if (Settings is null)
                throw new Exception("Settings not loaded");

            if (_inputImage == null)
            {
                _inputImage = await FoxImage.Load(Settings.selected_image);

                if (_inputImage is null)
                    throw new Exception("Input image could not be loaded");
            }
            return _inputImage;
        }

        public async Task<FoxImage> GetOutputImage()
        {
            if (OutputImageID is null)
                throw new Exception("Output image ID not set");

            if (_outputImage == null)
            {
                _outputImage = await FoxImage.Load(OutputImageID.Value);

                if (_outputImage is null)
                    throw new Exception("Output image could not be loaded");
            }

            return _outputImage;
        }
        public async Task SaveOutputImage(byte[] img, string? fileName = null)
        {
            var finalFileName = fileName;

            if (finalFileName is null)
                finalFileName = $"{this.ID}.png";

            if (this.User is null)
                throw new Exception("User not loaded");

            var outputImage = await FoxImage.Create(this.User.UID, img, FoxImage.ImageType.OUTPUT, finalFileName);

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SetOutputImage(outputImage);

            return;
        }

        public async Task SetOutputImage(FoxImage value)
        {
            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = $"UPDATE queue SET image_id = @image_id WHERE id = @id";
                cmd.Parameters.AddWithValue("id", this.ID);
                cmd.Parameters.AddWithValue("image_id", value.ID);
                await cmd.ExecuteNonQueryAsync();
            }

            _outputImage = value;
            OutputImageID = value.ID; // Adjust as needed.
        }
    }
}
