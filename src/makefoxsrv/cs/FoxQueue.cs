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
using PayPalCheckoutSdk.Orders;
using System.Reflection.Metadata.Ecma335;
using System.Numerics;

namespace makefoxsrv
{
    internal partial class FoxQueue
    {
        public enum QueueStatus //'PAUSED','CANCELLED','PENDING','PROCESSING','PROCESSED','SENDING','FINISHED','ERROR'
        {
            PAUSED,
            CANCELLED,
            PENDING,
            PROCESSING,
            PROCESSED,
            SENDING,
            FINISHED,
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

        [DbColumn("date_cancelled")]
        public DateTime? DateCancelled { get; private set; } = null;

        [DbColumn("retry_count")]
        public int RetryCount { get; private set; } = 0;

        [DbColumnMapping("LastException.Message", "error_str", true)]
        public Exception? LastException { get; private set; } = null;

        [DbInclude()]
        public FoxUserSettings Settings = new();

        [DbColumnMapping("User.UID", "uid")]
        private FoxUser? _user = null;

        public FoxUser User
        {
            get => _user ?? throw new InvalidOperationException("User is null");
            set => _user = value;
        }

        [DbColumn("id")]
        public ulong ID { get; private set; }
        [DbColumn("reply_msg")]
        public int? ReplyMessageID { get; private set; }

        [DbColumn("reply_topic_id")]
        public int? ReplyTopicID { get; private set; }

        [DbColumn("type")]
        public QueueType Type { get; private set; }

        [DbColumn("msg_id")]
        public int MessageID { get; private set; }

        [DbColumn("date_added")]
        public DateTime DateCreated { get; private set; }

        [DbColumn("date_queued")]
        public DateTime? DateQueued { get; private set; } = null;

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

        [DbColumn("complexity")]
        public float? Complexity { get; private set; } = null;

        private FoxImage? _outputImage;
        private FoxImage? _inputImage;

        public CancellationTokenSource stopToken { get; private set; } = new();

        private Stopwatch UserNotifyTimer = new Stopwatch();
        public FoxWorker? Worker { get; private set; } = null;

        [DbColumn("worker")]
        public int? WorkerID { get; private set; } = null;

        private static readonly object lockObj = new object();
        private static List<(FoxQueue task, int priority, DateTime dateStarted)> taskList = new List<(FoxQueue task, int priority, DateTime dateStarted)>();

        public static BoundedDictionary<ulong, FoxQueue> fullQueue { get; private set; } = new(1000); //Store the last 1000 queue items.

        private static Dictionary<AccessLevel, int> priorityMap = new Dictionary<AccessLevel, int>
            {
                { AccessLevel.ADMIN,   4 },  // Highest priority
                { AccessLevel.PREMIUM, 3 },
                { AccessLevel.BASIC,   2 },
                { AccessLevel.BANNED,  1 }   // Lowest priority
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

            // Custom dequeue strategy for queue cache
            fullQueue.RemovalStrategy = items =>
            {
                // Sort items by removal priority:
                // 1. Use DateCompleted if available (oldest first)
                // 2. Fallback to DateAdded if DateCompleted is null
                var sortedItems = items.OrderBy(kvp =>
                    kvp.Value.DateFinished ?? kvp.Value.DateCreated);

                // Build a complete list of removable items based on type priority
                var removableItems = sortedItems
                    .Where(kvp => kvp.Value.status == QueueStatus.CANCELLED) // First priority: CANCELLED
                    .Concat(sortedItems.Where(kvp => kvp.Value.status == QueueStatus.PAUSED)) // Second priority: PAUSED
                    .Concat(sortedItems.Where(kvp => kvp.Value.status == QueueStatus.FINISHED)) // Third priority: FINISHED
                    .Select(kvp => kvp.Key) // Select only the keys
                    .ToList();

                return removableItems;
            };
        }

        public static long ClearCache()
        {
            return 0; //Not yet implemented.
        }

        public static void Enqueue(FoxQueue item, bool addToFront = false)
        {
            item.DateQueued = DateTime.Now;

            if (addToFront)
            {
                lock (lockObj)
                {
                    // Insert the item at the front of the taskList, ignoring priority
                    taskList.Insert(0, (item, int.MaxValue, item.DateCreated)); // Use int.MaxValue to ensure it's treated as highest priority

                    // No need to sort as this item should remain at the front
                }

                queueSemaphore.Release();
                FoxLog.WriteLine($"Enqueueing task {item.ID} to the front of the queue.", LogLevel.DEBUG);
                return;
            }

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
                    if (DateTime.Now >= task.RetryDate.Value)
                    {
                        // Task's retry time has passed; re-enqueue it for processing
                        FoxLog.WriteLine($"Enqueueing delayed task {task.ID}.", LogLevel.DEBUG);
                        Enqueue(task);
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
        public static FoxWorker? CheckWorkerAvailability(FoxUserSettings settings)
        {
            var workers = FoxWorker.GetWorkers().Values;

            // Fetch the model by name using the new FoxModel method
            var model = FoxModel.GetModelByName(settings.Model);

            uint width = Math.Max(settings.Width, settings.hires_width);
            uint height = Math.Max(settings.Height, settings.hires_height);

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

        public static void StartTaskLoop(CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    FoxContextManager.Current = new FoxContext();

                    try
                    {
                        //FoxLog.WriteLine("Waiting for task...", LogLevel.DEBUG);
                        await queueSemaphore.WaitAsync(500, cancellationToken);

                        //FoxLog.WriteLine("Locking...", LogLevel.DEBUG);
                        lock (lockObj)
                        {
                            DateTime now = DateTime.Now;

                            for (int i = 0; i < taskList.Count; i++)
                            {
                                var itemToAssign = taskList[i].task;

                                if (itemToAssign is null)
                                    continue; // Shouldn't happen, but just in case.

                                if (!FoxTelegram.IsConnected)
                                    continue; // Skip if Telegram is not connected

                                FoxContextManager.Current.Queue = itemToAssign;
                                FoxContextManager.Current.User = itemToAssign.User;
                                FoxContextManager.Current.Telegram = itemToAssign.Telegram;
                                FoxContextManager.Current.Message = new Message { id = itemToAssign.MessageID };
                                FoxContextManager.Current.Worker = null;

                                if (itemToAssign.status == QueueStatus.PAUSED)
                                    continue;

                                if (itemToAssign.status == QueueStatus.CANCELLED)
                                {
                                    // Remove the task from the queue if it was cancelled
                                    FoxLog.WriteLine($"Task {itemToAssign.ID} was cancelled, skipping.", LogLevel.DEBUG);
                                    taskList = taskList.Where(t => t.task?.ID != itemToAssign.ID).ToList();
                                    break;
                                }

                                if (itemToAssign.status == QueueStatus.FINISHED)
                                {
                                    // Remove the task from the queue if it was already marked as finished
                                    FoxLog.WriteLine($"Task {itemToAssign.ID} was already marked finished.", LogLevel.ERROR);
                                    taskList = taskList.Where(t => t.task?.ID != itemToAssign.ID).ToList();
                                    break;
                                }

                                if (itemToAssign.OutputImageID is not null)
                                {
                                    if (itemToAssign.RetryDate is not null && itemToAssign.RetryDate.Value >= DateTime.Now)
                                        continue;

                                    // Item was previously generated, but failed during sending.  Resend.
                                    FoxLog.WriteLine($"Task {itemToAssign.ID} was previously generated but not sent.  Resending.", LogLevel.DEBUG);

                                    // Remove the task from the queue as it no longer needs to be processed
                                    taskList = taskList.Where(t => t.task?.ID != itemToAssign.ID).ToList();
                                    _ = FoxSendQueue.Send(itemToAssign);

                                    break;
                                }

                                var suitableWorker = FindSuitableWorkerForTask(itemToAssign);
                                if (suitableWorker != null)
                                {
                                    // Found a suitable worker for the task
                                    FoxContextManager.Current.Worker = suitableWorker;


                                    if (suitableWorker.AssignTask(itemToAssign))
                                    {
                                        // Remove the task from the queue if it was successfully assigned
                                        FoxLog.WriteLine($"Assigned task {itemToAssign.ID}  to {suitableWorker?.name}", LogLevel.DEBUG);
                                        taskList = taskList.Where(t => t.task?.ID != itemToAssign.ID).ToList();
                                        break;
                                    }
                                }
                            }
                        }
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
                    finally
                    {
                        FoxContextManager.Clear();
                    }
                }
            });
        }

        public static FoxWorker? FindSuitableWorkerForTask(FoxQueue item)
        {
            // 1. Fetch the requested model
            var model = FoxModel.GetModelByName(item.Settings.Model);

            uint width = Math.Max(item.Settings.Width, item.Settings.hires_width);
            uint height = Math.Max(item.Settings.Height, item.Settings.hires_height);

            // If no such model or no workers running it, return
            if (model == null || model.GetWorkersRunningModel().Count < 1)
                return null;

            if (item.User is null)
            {
                FoxLog.WriteLine($"Task {item.ID} - Skipping because user is null.", LogLevel.DEBUG);
                return null;
            }

            // 2. Filter out unsuitable workers first
            var workers = FoxWorker.GetWorkers().Values;
            var suitableWorkers = workers
                .Where(worker => worker.Online
                                 && (worker.qItem is null)
                                 && (!worker.MaxImageSize.HasValue || (width * height) <= worker.MaxImageSize.Value)
                                 && (!worker.MaxImageSteps.HasValue || item.Settings.steps <= worker.MaxImageSteps.Value)
                                 && (!item.RegionalPrompting || worker.SupportsRegionalPrompter)
                                 && model.GetWorkersRunningModel().Contains(worker.ID))  // Ensure the worker has the model loaded
                .ToList();

            if (item.Telegram?.Chat is not null)
            {
                // Enforce strict flood-limiting rules: one task per chat, max 10 messages per minute.
                var now = DateTime.Now;
                var oneMinuteAgo = now.AddMinutes(-1);

                // Look in the full queue for all tasks relevant to the same chat
                var chatTasks = fullQueue.Values
                    .Where(queueItem => queueItem != null
                            && queueItem.Telegram?.Chat?.ID == item.Telegram.Chat.ID
                            )
                    .OrderBy(queueItem => queueItem.DateSent)
                    .ToList();

                // Count messages sent within the last minute
                int messagesLastMinute = chatTasks.Count(t => t.DateSent >= oneMinuteAgo);

                // Check if any task in the same chat is currently being processed
                bool hasActiveProcessingTask = chatTasks.Any(t => t.status != FoxQueue.QueueStatus.PAUSED 
                                                               && t.status != FoxQueue.QueueStatus.PENDING
                                                               && t.status != FoxQueue.QueueStatus.CANCELLED
                                                               && t.status != FoxQueue.QueueStatus.FINISHED);

                // Skip the task if flood-limiting rules are violated
                if (messagesLastMinute >= 10 || hasActiveProcessingTask)
                {
                    return null; // Signal to skip this task
                }
            }


            if (!item.User.CheckAccessLevel(AccessLevel.ADMIN))
            {
                // New code to check the total complexity of the user's queue
                var userQueueComplexity = taskList
                    .Select(t => t.task)
                    .Where(queueItem => queueItem != null
                                        && queueItem.User?.UID == item.User?.UID
                                        && queueItem.RetryDate <= DateTime.Now
                                        && (
                                            queueItem.status == FoxQueue.QueueStatus.PENDING ||
                                            queueItem.status == FoxQueue.QueueStatus.PROCESSING ||
                                            queueItem.status == FoxQueue.QueueStatus.ERROR
                                        ))
                    .Sum(queueItem => queueItem!.Complexity ?? 0);

                // 3. Block if the same user is already being processed, or if complexity is too high
                if (userQueueComplexity >= 1.0 || !item.User.CheckAccessLevel(AccessLevel.PREMIUM))
                {
                    var userWorkers = suitableWorkers
                        .Where(worker => worker.qItem != null && worker.qItem.User?.UID == item.User?.UID)
                        .ToList();

                    if (userWorkers.Any())
                    {
                        //FoxLog.WriteLine($"Task {item.ID} - Skipping because user {item.User?.UID} is already being processed by another worker.", LogLevel.DEBUG);
                        return null;
                    }
                }
            }

            // 4. Handle Enhanced/variation tasks that need the same worker, if possible
            if (item.Enhanced || item.Settings.variation_seed != null)
            {
                var timeInQueue = DateTime.Now - item.DateCreated;

                // Wait up to 10 minutes to reuse a compatible worker.
                // After that, take whatever we can get.

                if (timeInQueue.TotalMinutes < 10)
                {
                    var gpuType = item.Worker?.GPUType;

                    if (gpuType is null)
                    {
                        return suitableWorkers.FirstOrDefault(worker => worker.ID == item.WorkerID);
                    }
                    else
                    {
                        return suitableWorkers.FirstOrDefault(worker => worker.GPUType == gpuType);
                    }
                }
            }

            // 5. Gather pending tasks to look ahead for future model usage
            var futureTasks = taskList
                .Select(t => t.task)
                .Where(t => t != null
                            && t!.status == FoxQueue.QueueStatus.PENDING
                            && t.ID != item.ID) // exclude current task
                .ToList();

            // 6. Identify workers that already have the requested model loaded
            var workersRunningThisModel = model.GetWorkersRunningModel();
            var hasModelLoaded = suitableWorkers
                .Where(w => workersRunningThisModel.Contains(w.ID))
                .ToList();

            // 7. Idle workers already loaded with this model
            var idleWorkersWithModel = hasModelLoaded
                .Where(w => w.LoadedModels.Contains(item.Settings.Model) && w.qItem == null)
                .ToList();

            if (idleWorkersWithModel.Any())
                return idleWorkersWithModel.First();

            // 8. Idle workers with no model loaded
            var idleWorkersWithNoModel = hasModelLoaded
                .Where(w => w.LoadedModels.Count() == 0 && w.qItem == null)
                .ToList();

            if (idleWorkersWithNoModel.Any())
                return idleWorkersWithNoModel.First();

            // 9. Check if all suitable workers are idle
            var allIdle = suitableWorkers.All(w => w.qItem == null);
            if (allIdle)
                return suitableWorkers.FirstOrDefault();

            // 10. If user isn't premium and hasn't been waiting long, keep them waiting
            var modelWaitingTime = DateTime.Now - item.DateCreated;
            var waitTime = item.User.CheckAccessLevel(AccessLevel.PREMIUM) ? 4 : 12;

            if (modelWaitingTime.TotalSeconds < waitTime)
            {
                //FoxLog.WriteLine($"Task {item.ID} - Delaying to wait for available model {model.Name}. ({modelWaitingTime.TotalSeconds}s)", LogLevel.DEBUG);
                return null;
            }

            // 13. Fallback: any other idle worker
            var availableSuitableWorkers = suitableWorkers
                .Where(worker => worker.qItem == null)
                .OrderBy(w => w.LastActivity)
                .ToList();

            if (availableSuitableWorkers.Any())
                return availableSuitableWorkers.First();

            // 14. Last resort: no idle workers
            return null;
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
                            q.status IN ('PENDING', 'ERROR', 'PROCESSING', 'PROCESSED', 'SENDING')
                        ORDER BY 
                            q.date_added ASC
                        ";

                using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    var q = FoxDB.LoadObject<FoxQueue>(r);

                    long? uid = r["uid"] is DBNull ? null : Convert.ToInt64(r["uid"]);

                    if (uid is null)
                        continue; //Something went wrong, skip this one.

                    var user = await FoxUser.GetByUID(uid.Value);

                    if (user is null)
                        continue; //Something went wrong, skip this one.

                    q.User = user;

                    long? teleChatId = r["tele_chatid"] is DBNull ? null : Convert.ToInt64(r["tele_chatid"]);

                    var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                    var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                    if (teleUser is null)
                        continue; //Something went wrong, skip this one.

                    q.Telegram = new FoxTelegram(teleUser, teleChat);

                    fullQueue.Add(q.ID, q); // Add to queue cache

                    count++;
                    
                    Enqueue(q);
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
            cmd.Connection = SQL;

            if (newMessageID is not null)
                this.MessageID = newMessageID.Value;

            DateTime now = DateTime.Now;
            string? additionalSetClause = null;

            switch (status)
            {
                case QueueStatus.PENDING:
                    DateQueued = now;
                    additionalSetClause = "date_queued = @now";
                    break;

                case QueueStatus.FINISHED:
                    DateFinished = now;
                    additionalSetClause = "date_finished = @now";
                    break;

                case QueueStatus.PROCESSING:
                    DateStarted = now;
                    additionalSetClause = "date_worker_start = @now";
                    break;

                case QueueStatus.SENDING:
                    DateSent = now;
                    additionalSetClause = "date_sent = @now";
                    break;

                case QueueStatus.CANCELLED:
                    DateCancelled = now;
                    additionalSetClause = "date_cancelled = @now";
                    break;
                case QueueStatus.PAUSED:
                    //Nothing special required here.
                    break;

                case QueueStatus.ERROR:
                    throw new Exception("Use SetError() to set error status");

                default:
                    FoxLog.WriteLine($"Unexpected status '{status}' encountered in SetStatus", LogLevel.WARNING);
                    break;
            }

            cmd.CommandText = $@"
                UPDATE queue 
                SET status = @status 
                    {(!string.IsNullOrEmpty(additionalSetClause) ? $", {additionalSetClause}" : "")}
                    {(newMessageID.HasValue ? ", msg_id = @msg_id" : "")}
                WHERE id = @id";

            cmd.Parameters.AddWithValue("status", status.ToString());
            cmd.Parameters.AddWithValue("id", this.ID);

            if (additionalSetClause is not null)
                cmd.Parameters.AddWithValue("now", now);

            if (newMessageID.HasValue)
                cmd.Parameters.AddWithValue("msg_id", newMessageID.Value);

            await cmd.ExecuteNonQueryAsync();

            FoxLog.WriteLine($"Task {this.ID} status set to {status}", LogLevel.DEBUG);
        }

        public static (int hypotheticalPosition, int totalItems) GetNextPosition(FoxUser user, bool Enhanced = false)
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
                        return (position, totalItems); // Return the position and total items
                    }
                    position++;
                }

                // If the task is not found, return -1
                return (-1, totalItems);
            }
        }

        public static async Task<FoxQueue> Add(FoxTelegram telegram, FoxUser user, FoxUserSettings taskSettings,
                                                QueueType type, int messageID, Message? replyToMessage = null, bool enhanced = false,
                                                FoxQueue? originalTask = null, TimeSpan? delay = null, QueueStatus status = QueueStatus.PENDING)
        {
            if (FoxContextManager.Current.Queue is null)
                FoxContextManager.Current.Queue = originalTask;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            FoxUserSettings settings = taskSettings.Copy();

            if (type == QueueType.TXT2IMG && (settings.Width > 1088 || settings.Height > 1088))
            {
                settings.hires_denoising_strength = 0.5M;
                settings.hires_steps = 15;
                settings.hires_width = settings.Width;
                settings.hires_height = settings.Height;
                settings.hires_enabled = true;

                (settings.Width, settings.Height) = FoxImage.CalculateLimitedDimensions(settings.Width, settings.Height, 1024);
            }

            if (settings.Seed == -1)
                settings.Seed = GetRandomInt32();

            long imageSize = Math.Max(settings.Width, settings.hires_width) * Math.Max(settings.Height, settings.hires_height);
            long imageComplexity = imageSize * (settings.steps + settings.hires_steps);

            // Default and maximum complexity
            long defaultComplexity = 640 * 768 * 20;
            long maxComplexity = 1280 * 1536 * 20;

            double normalizedComplexity = (double)(imageComplexity - defaultComplexity) / (maxComplexity - defaultComplexity);

            int replyTopicID = replyToMessage?.ReplyHeader?.TopicID ?? 0;

            var q = new FoxQueue
            {
                status = status,
                Telegram = telegram,
                User = user,
                DateCreated = DateTime.Now,
                Type = type,
                Settings = settings,
                MessageID = messageID,
                ReplyMessageID = replyToMessage?.id,
                ReplyTopicID = replyTopicID == 0 ? null : replyTopicID,
                Enhanced = enhanced,
                OriginalID = originalTask?.ID,
                WorkerID = originalTask?.WorkerID,
                RetryDate = delay.HasValue ? DateTime.Now.Add(delay.Value) : null,
                RegionalPrompting = settings.regionalPrompting,
                Complexity = (float)normalizedComplexity
            };

            FoxContextManager.Current.Queue = q;

            if (type == QueueType.IMG2IMG && !(await FoxImage.IsImageValid(settings.SelectedImage)))
                throw new Exception("Invalid input image");

            await FoxDB.SaveObjectAsync(q, "queue");

            fullQueue.Add(q.ID, q); // Add to queue cache

            // Log this after saving the object so we have it's ID.

            FoxLog.WriteLine($"Complexity: {normalizedComplexity:F3}", LogLevel.INFO);

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
                    var inlineKeyboardButtons = new ReplyInlineMarkup()
                    {
                        rows = new TL.KeyboardButtonRow[] {
                                new TL.KeyboardButtonRow {
                                    buttons = new TL.KeyboardButtonCallback[]
                                    {
                                        new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes("/cancel " + this.ID) },
                                    }
                                }
                            }
                    };

                    await this.Telegram.EditMessageAsync(
                        id: this.MessageID,
                        text: $"⏳ Generating now on {worker.name}...",
                        replyInlineMarkup: inlineKeyboardButtons
                    );
                }
            }
            catch (WTelegram.WTException ex) when (ex is RpcException rex && rex.Code == 400 && (rex.Message == "MESSAGE_NOT_MODIFIED" || rex.Message == "MESSAGE_ID_INVALID"))
            {
                // Ignore these telegram errors, but log them.
                FoxLog.LogException(ex);
            }
        }

        public async Task Progress(FoxWorker worker, double progressPercent)
        {
            try
            {
                if (this.UserNotifyTimer.ElapsedMilliseconds >= 8000 && this.Telegram is not null && this.Telegram.Chat is null)
                {
                    //Due to the stricter rate limits on editing messages in groups, we only notify the user in private chats.

                    var inlineKeyboardButtons = new ReplyInlineMarkup()
                    {
                        rows = new TL.KeyboardButtonRow[] {
                                new TL.KeyboardButtonRow {
                                    buttons = new TL.KeyboardButtonCallback[]
                                    {
                                        new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes("/cancel " + this.ID) },
                                    }
                                }
                            }
                    };

                    await this.Telegram.EditMessageAsync(
                        id: this.MessageID,
                        text: $"⏳ Generating now on {worker.name} ({(int)progressPercent}%)...",
                        replyInlineMarkup: inlineKeyboardButtons
                    );

                    this.UserNotifyTimer.Restart();
                }
            }
            catch (WTelegram.WTException ex) when (ex is RpcException rex && rex.Code == 400 && (rex.Message == "MESSAGE_NOT_MODIFIED" || rex.Message == "MESSAGE_ID_INVALID"))
            {
                //Ignore these errors.
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

        public static async Task<FoxQueue?> GetNewestFromUser(FoxUser user, long? tgChatId = null)
        {
            // Attempt to find the FoxQueue item in the fullQueue cache

            // Attempt to find the FoxQueue item in the fullQueue cache
            var cachedItem = fullQueue.FirstOrDefault(fq => fq is not null && fq.User.UID == user.UID && (tgChatId == null || fq.Telegram?.Peer?.ID == tgChatId));

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
                var user = await FoxUser.GetByUID(uid);
                    
                if (user is null)
                    throw new Exception("User not found");

                o.User = user;

                long ? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];

                var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                if (teleUser is not null)
                    o.Telegram = new FoxTelegram(teleUser, teleChat);
            });

            // After loading, add the object to the fullQueue cache if it's not null
            if (q is not null)
                fullQueue.Add(q.ID, q); // Add to queue cache

            return q;
        }

        [Cron(seconds: 10)]
        private async Task CronUpdateQueueMessages(CancellationToken cancellationToken)
        {
            List<(FoxQueue item, int priority, DateTime dateStarted)> tasks;

            lock (taskList)
            {
                if (taskList.Count < 1)
                    return;

                // Use ToList() to create a copy if taskList implements IEnumerable
                tasks = taskList.ToList();
            }

            foreach ((var item, _, DateTime dateStarted) in tasks)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                if (item.Telegram is null)
                    continue;

                if (item.Telegram.Chat is not null)
                    continue; // Skip updating in groups.

                if (DateTime.Now - dateStarted < TimeSpan.FromSeconds(10))
                    continue; // Skip updating if the task was started less than 5 seconds ago.

                if (item.User is null)
                    continue;

                if (item.status == QueueStatus.PENDING)
                {
                    if (await item.User.GetFloodWait() > DateTime.Now)
                        continue; // User is rate limited.

                    try
                    {
                        var inlineKeyboardButtons = new ReplyInlineMarkup()
                        {
                            rows = new TL.KeyboardButtonRow[] {
                                new TL.KeyboardButtonRow {
                                    buttons = new TL.KeyboardButtonCallback[]
                                    {
                                        new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes($"/cancel {item.ID}") },
                                    }
                                }
                            }
                        };

                        (int position, int totalItems) = item.GetPosition();

                        await item.Telegram.EditMessageAsync(
                            id: item.MessageID,
                            text: $"⏳ In queue ({position} of {totalItems})...",
                            replyInlineMarkup: inlineKeyboardButtons
                        );

                        await Task.Delay(500);
                    }
                    catch (WTException ex) when (ex.Message == "MESSAGE_NOT_MODIFIED")
                    {
                        // Do nothing; ignore this error
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex);
                    }
                }
            }
        }

        public static async Task<FoxQueue?> GetByMessage(FoxTelegram telegram, long msg_id)
        {
            long user_id = telegram.User.ID;
            long? chat_id = telegram.Chat?.ID;

            // Attempt to find the FoxQueue item in the fullQueue cache
            var cachedItem = fullQueue.FindAll(fq =>
                fq.ReplyMessageID == msg_id &&
                fq.Telegram?.User.ID == user_id &&
                fq.Telegram?.Chat?.ID == chat_id).FirstOrDefault();

            if (cachedItem is not null)
            {
                // If found in cache, return the cached item
                return cachedItem;
            }

            var parameters = new Dictionary<string, object?>
            {
                { "@msg_id", msg_id },
                { "@tele_id", user_id },
                { "@tele_chatid", chat_id }
            };

            string chatCondition = chat_id is null ? "tele_chatid IS NULL" : "tele_chatid = @tele_chatid";

            string query = $"tele_id = @tele_id AND msg_id = @msg_id AND {chatCondition}";

            var q = await FoxDB.LoadObjectAsync<FoxQueue>("queue", query, parameters, async (o, r) =>
            {
                long uid = Convert.ToInt64(r["uid"]);

                var user = await FoxUser.GetByUID(uid);

                if (user is null)
                    throw new Exception("User not found");

                o.User = user;

                long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];

                var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                if (teleUser is not null)
                    o.Telegram = new FoxTelegram(teleUser, teleChat);
            });

            // After loading, add the object to the fullQueue cache if it's not null
            if (q is not null)
                fullQueue.Add(q.ID, q); // Add to queue cache

            return q;
        }

        public static async Task<FoxQueue?> Get(ulong id)
        {
            // Attempt to find the FoxQueue item in the fullQueue cache
            var cachedItem = fullQueue[id];

            if (cachedItem is not null)
            {
                // If found in cache, return the cached item
                return cachedItem;
            }

            var parameters = new Dictionary<string, object?>
            {
                { "@id", id }
            };

            var q = await FoxDB.LoadObjectAsync<FoxQueue>("queue", "id = @id", parameters, async (o, r) =>
            {
                long uid = Convert.ToInt64(r["uid"]);

                var user = await FoxUser.GetByUID(uid);

                if (user is null)
                    throw new Exception("User not found");

                o.User = user;

                long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];

                var teleChat = teleChatId is not null ? await FoxTelegram.GetChatFromID(teleChatId.Value) : null;

                var teleUser = await FoxTelegram.GetUserFromID(Convert.ToInt64(r["tele_id"]));

                if (teleUser is not null)
                    o.Telegram = new FoxTelegram(teleUser, teleChat);
            });

            // After loading, add the object to the fullQueue cache if it's not null
            if (q is not null)
                fullQueue.Add(q.ID, q); // Add to queue cache

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
                    {
                        var dateSent = r["date_sent"];
                        var dateWorkerStart = r["date_worker_start"];

                        if (dateSent is null || dateWorkerStart is null)
                        {
                            FoxLog.WriteLine($"Task {this.ID} - dateSent or dateWorkerStart is null", LogLevel.WARNING);

                            return TimeSpan.Zero;
                        }

                        return Convert.ToDateTime(dateSent).Subtract(Convert.ToDateTime(dateWorkerStart));
                    }
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

        private enum errorType
        {
            SILENT,
            FATAL,
            FLOOD_WAIT,
            SHUTDOWN,
            OTHER
        }

        public async Task SetError(Exception ex, DateTime? retryWhen = null, [CallerMemberName] string callerName = "",
                                   [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            if (this.status == QueueStatus.CANCELLED || this.stopToken.IsCancellationRequested)
            {
                FoxLog.LogException(ex, "Error occured after request was cancelled.", callerName, callerFilePath, lineNumber);

                return;
            }

            //if (this.User is not null)
            //    this.User.RecordError(ex);

            FoxLog.LogException(ex, null, callerName, callerFilePath, lineNumber);

            this.status = QueueStatus.ERROR;
            this.DateLastFailed = DateTime.Now;
            this.LastException = ex;

            int maxRetries = FoxSettings.Get<int?>("MaxRetries") ?? 4;
            int retryDelay = FoxSettings.Get<int?>("RetryDelaySec") ?? 5;

            // List of silent error strings. If any of these are encountered, the task will be retried immediately.

            var silentErrors = new List<string> {
                "llocation on device",
                "out of memory",
                "xpected all tensors to be on",
                "onnection refused",
                "ould not connect to server",
                "rror occurred while sending",
                "rror while copying content to a stream",
            };

            // List of fatal errors. If any of these are encountered, the task will not be retried.
            var fatalErrors = new List<string> {
                "ould not convert string to float",
                "CHANNEL_PRIVATE",
                "CHAT_SEND_PHOTOS_FORBIDDEN"
            };

            var messageBuilder = new StringBuilder();
            errorType errorType = errorType.OTHER;

            // Check if the error message contains any silent error strings

            if (ex is OperationCanceledException)
            {
                errorType = errorType.SHUTDOWN;
                messageBuilder.Append($"⚠️ The server is restarting for maintenance.  This request will resume shortly.");
            }
            else if (ex is RpcException rex && rex.Code == 420)
            {
                var waitTime = TimeSpan.FromSeconds(rex.X);

                this.RetryDate = DateTime.Now + waitTime;
                errorType = errorType.FLOOD_WAIT;

                messageBuilder.AppendLine($"❌ Telegram is currently reporting that you've exceeded the rate limit.  This rate limit is outside of our control.");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine($"⏳ This request will resume in {waitTime.ToPrettyFormat()}.");

                this.RetryCount++;
            }
            if (silentErrors.Any(silentError => ex.Message.Contains(silentError)))
            {
                errorType = errorType.SILENT;
                messageBuilder.AppendLine("❌ This worker has experienced an error.");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("⏳ Your task is being reassigned and will resume soon.");
            }
            else if (fatalErrors.Any(fatalErrStr => ex.Message.Contains(fatalErrStr)))
            {
                errorType = errorType.FATAL;
                this.status = QueueStatus.CANCELLED;

                messageBuilder.AppendLine($"❌ Encountered a fatal error.  Task Cancelled.");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine($"❌ Error: {ex.Message}");
            }
            else
            {
                if (retryWhen is not null)
                    this.RetryDate = retryWhen;
                else
                    this.RetryDate = DateTime.Now.AddSeconds(retryDelay);

                this.RetryCount++;
            }

            if (this.RetryCount > maxRetries && errorType != errorType.SILENT) 
            {
                errorType = errorType.FATAL;
                this.status = QueueStatus.CANCELLED;

                messageBuilder.AppendLine($"❌ Maximum retries attempted.  Task Cancelled.");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Error: {ex.Message}");
            }

            if (errorType == errorType.OTHER)
            {
                messageBuilder.AppendLine($"⏳ Encountered an error.  This request will retry shortly.");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Error: {ex.Message}");
            }

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = @status, error_str = @error, retry_date = @retry_date, retry_count = @retry_count, date_failed = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.ID);
                    cmd.Parameters.AddWithValue("retry_date", RetryDate);
                    cmd.Parameters.AddWithValue("retry_count", RetryCount);
                    cmd.Parameters.AddWithValue("error", ex.Message);
                    cmd.Parameters.AddWithValue("now", this.DateLastFailed);
                    cmd.Parameters.AddWithValue("status", this.status.ToString());

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            switch (errorType)
            {
                case errorType.SILENT:                
                case errorType.FLOOD_WAIT:
                case errorType.OTHER:
                    try
                    {
                        var inlineKeyboardButtons = new ReplyInlineMarkup()
                        {
                            rows = new TL.KeyboardButtonRow[] {
                                    new TL.KeyboardButtonRow {
                                        buttons = new TL.KeyboardButtonCallback[]
                                        {
                                            new TL.KeyboardButtonCallback { text = "Cancel", data = System.Text.Encoding.ASCII.GetBytes("/cancel " + this.ID) },
                                        }
                                    }
                                }
                        };

                        if (Telegram is not null)
                        {
                            await Telegram.EditMessageAsync(
                                id: MessageID,
                                text: messageBuilder.ToString(),
                                replyInlineMarkup: inlineKeyboardButtons
                            );
                        }
                    }
                    catch when (ex.Message != "MESSAGE_NOT_MODIFIED")
                    {
                        FoxLog.LogException(ex);
                    }
                    
                    Enqueue(this, (errorType == errorType.SILENT));

                    break;
                case errorType.SHUTDOWN:
                case errorType.FATAL:
                    try
                    {
                        if (Telegram is not null)
                        {
                            await Telegram.EditMessageAsync(
                                id: MessageID,
                                text: messageBuilder.ToString()
                            );
                        }
                    }
                    catch when (ex.Message != "MESSAGE_NOT_MODIFIED")
                    {
                        FoxLog.LogException(ex);
                    }

                    break;
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
            await this.SetCancelled();
            this.stopToken.Cancel();

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
        }

        public async Task<FoxImage> GetInputImage()
        {
            if (Settings is null)
                throw new Exception("Settings not loaded");

            if (_inputImage == null)
            {
                _inputImage = await FoxImage.Load(Settings.SelectedImage);

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
            OutputImageID = value.ID;
        }
    }
}
