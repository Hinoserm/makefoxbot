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
            ERROR
        }
        public enum QueueType
        {
            UNKNOWN,
            IMG2IMG,
            TXT2IMG
        }
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);

        public QueueStatus status = QueueStatus.PENDING;

        public FoxUserSettings? Settings = null;
        public FoxUser? User = null;

        public ulong ID { get; private set; }
        public int? ReplyMessageID { get; private set; }

        public QueueType Type { get; private set; }
        public int MessageID { get; private set; }
        public DateTime DateCreated { get; private set; }
        public DateTime? DateStarted { get; private set; } = null;
        public DateTime? DateSent { get; private set; } = null;
        public DateTime? DateFinished { get; private set; } = null;
        public string? LinkToken { get; private set; } = null;

        public FoxTelegram? Telegram { get; private set; }

        public ulong? OutputImageID { get; set; }

        private FoxImage? _outputImage;
        private FoxImage? _inputImage;

        public CancellationTokenSource stopToken { get; private set; } = new();

        private Stopwatch UserNotifyTimer = new Stopwatch();
        public FoxWorker? Worker { get; private set; } = null;
        public int? WorkerID { get; private set; } = null;

        private static readonly object lockObj = new object();
        private static PriorityQueue<FoxQueue, (int Priority, ulong InverseId)> priorityQueue = new PriorityQueue<FoxQueue, (int, ulong)>();
        private static Dictionary<AccessLevel, int> priorityMap = new Dictionary<AccessLevel, int>
            {
                { AccessLevel.ADMIN, 4 },  // Highest priority
                { AccessLevel.PREMIUM, 3 },
                { AccessLevel.BASIC, 2 },
                { AccessLevel.BANNED, 1 }   // Lowest priority
            };
        private static SemaphoreSlim queueSemaphore = new SemaphoreSlim(0, int.MaxValue);

        static FoxQueue()
        {
            FoxWorker.OnTaskCompleted += (sender, args) => _ = queueSemaphore.Release();
            FoxWorker.OnWorkerStart += (sender, args) => _ = queueSemaphore.Release();
            FoxWorker.OnWorkerOnline += (sender, args) => _ = queueSemaphore.Release();
        }

        public static async Task Enqueue(FoxQueue item)
        {
            FoxLog.WriteLine($"Enqueueing task {item.ID}.", LogLevel.LOG_DEBUG);
            lock (lockObj)
            {
                // Determine the priority based on the user's access level
                int priorityLevel = priorityMap[item.User.GetAccessLevel()];
                priorityQueue.Enqueue(item, (priorityLevel, item.ID));
            }
            queueSemaphore.Release();
        }

        public static void StartTaskLoop()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    FoxQueue? itemToAssign = null;
                    FoxWorker? suitableWorker = null;
                    
                    await queueSemaphore.WaitAsync(2000);


                    lock (lockObj)
                    {
                        if (priorityQueue.TryPeek(out FoxQueue? item, out var itemPriority))
                        {
                            suitableWorker = FindSuitableWorkerForTask(item);
                            if (suitableWorker != null)
                            {
                                priorityQueue.TryDequeue(out itemToAssign, out itemPriority);
                            }
                            //else
                            //    semaphore.Release();
                        }
                    }

                    if (itemToAssign is not null)
                    {
                        FoxLog.WriteLine($"Task popped from queue: {itemToAssign.ID}", LogLevel.LOG_DEBUG);

                        if (suitableWorker is not null)
                        {
                            FoxLog.WriteLine($"Assigned task to {suitableWorker?.name ?? "unknown worker"}: {itemToAssign.ID}", LogLevel.LOG_DEBUG);

                            suitableWorker.AssignTask(itemToAssign);
                        }
                    }
                }
            });
        }

        private static FoxWorker? FindSuitableWorkerForTask(FoxQueue item)
        {
            // First, filter out workers based on their online status, image size and steps capacity,
            // and further check if they are not busy (qItem is null) and either have the model loaded or it's available to them.
            var suitableWorkers = FoxWorker.GetWorkers().Values
                .Where(worker => worker.Online
                                 && (worker.qItem == null)  // Worker is not currently busy
                                 && (!worker.MaxImageSize.HasValue || (item.Settings.width * item.Settings.height) <= worker.MaxImageSize.Value)
                                 && (!worker.MaxImageSteps.HasValue || item.Settings.steps <= worker.MaxImageSteps.Value))
                .ToList();

            // Prioritize workers who already have the model loaded.
            var preferredWorkers = suitableWorkers
                .Where(worker => worker.GetRecentModels().Contains(item.Settings.model))
                .OrderByDescending(worker => priorityMap[item.User.GetAccessLevel()])
                .ToList();

            if (preferredWorkers.Any())
            {
                var w = preferredWorkers.First();
                //FoxLog.WriteLine($"Preferred worker found: {w.name}");
                return w;
            }

            // If no preferred worker is available, fall back to any suitable worker,
            // still respecting the user's access level priority.
            var suitableWorker = suitableWorkers
                .Where(worker => worker.availableModels.ContainsKey(item.Settings.model)) // Ensure model is available
                .OrderByDescending(worker => priorityMap[item.User.GetAccessLevel()])
                .FirstOrDefault();
            //FoxLog.WriteLine($"Suitable worker found: {suitableWorker?.name ?? "none"}");
            return suitableWorker;
        }

        private static bool IsWorkerSuitableForTask(FoxWorker worker, FoxQueue item)
        {
            // Check for worker's suitability based on item requirements
            // Placeholder for actual implementation
            return true;
        }

        public static async Task EnqueueOldItems()
        {
            int count = 0;

            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

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
                            q.status IN ('PENDING', 'ERROR')
                        ORDER BY 
                            q.date_added ASC
                        LIMIT 1
                        FOR UPDATE;
                        ";

                await using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    var q = new FoxQueue();
                    var settings = new FoxUserSettings();

                    q.ID = Convert.ToUInt64(r["id"]);

                    q.User = await FoxUser.GetByUID(Convert.ToInt64(r["uid"]));

                    long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];
                    long? teleChatHash = r["chat_access_hash"] is DBNull ? null : (long)r["chat_access_hash"];

                    q.Telegram = new FoxTelegram((long)r["tele_id"], (long)r["user_access_hash"], teleChatId, teleChatHash);

                    q.Type = Enum.Parse<FoxQueue.QueueType>(Convert.ToString(r["type"]) ?? "UNKNOWN");

                    if (!(r["steps"] is DBNull))
                        settings.steps = Convert.ToInt16(r["steps"]);
                    if (!(r["cfgscale"] is DBNull))
                        settings.cfgscale = Convert.ToDecimal(r["cfgscale"]);
                    if (!(r["prompt"] is DBNull))
                        settings.prompt = Convert.ToString(r["prompt"]);
                    if (!(r["negative_prompt"] is DBNull))
                        settings.negative_prompt = Convert.ToString(r["negative_prompt"]);
                    if (!(r["selected_image"] is DBNull))
                        settings.selected_image = Convert.ToUInt64(r["selected_image"]);
                    if (!(r["width"] is DBNull))
                        settings.width = Convert.ToUInt32(r["width"]);
                    if (!(r["height"] is DBNull))
                        settings.height = Convert.ToUInt32(r["height"]);
                    if (!(r["denoising_strength"] is DBNull))
                        settings.denoising_strength = Convert.ToDecimal(r["denoising_strength"]);
                    if (!(r["seed"] is DBNull))
                        settings.seed = Convert.ToInt32(r["seed"]);
                    if (!(r["model"] is DBNull))
                        settings.model = Convert.ToString(r["model"]);
                    if (!(r["reply_msg"] is DBNull))
                        q.ReplyMessageID = Convert.ToInt32(r["reply_msg"]);
                    if (!(r["msg_id"] is DBNull))
                        q.MessageID = Convert.ToInt32(r["msg_id"]);
                    if (!(r["date_added"] is DBNull))
                        q.DateCreated = Convert.ToDateTime(r["date_added"]);
                    if (!(r["link_token"] is DBNull))
                        q.LinkToken = Convert.ToString(r["link_token"]);

                    q.Settings = settings;
                    count++;
                    await Enqueue(q);
                }
            }

            FoxLog.WriteLine($"Added {count} saved tasks to queue.");
        }

        public async Task SetStatus(QueueStatus status, int? messageID = null)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

            using var cmd = new MySqlCommand();

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
                    cmd.Parameters.AddWithValue("msg_id", messageID);
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
        }

        public static async Task<FoxQueue?> Add(FoxTelegram telegram, FoxUser user, FoxUserSettings settings, QueueType type, int messageID, int? replyMessageID = null)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SQL.OpenAsync();

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
                ReplyMessageID = replyMessageID
            };

            if (type == QueueType.IMG2IMG && !(await FoxImage.IsImageValid(settings.selected_image)))
                throw new Exception("Invalid input image");

            using var cmd = new MySqlCommand();

            cmd.Connection = SQL;
            cmd.CommandText = @"
                INSERT INTO queue (status, type, uid, tele_id, tele_chatid, steps, cfgscale, prompt, negative_prompt, selected_image, width, height, denoising_strength, seed, reply_msg, msg_id, date_added, model)
                VALUES ('PENDING', @type, @uid, @tele_id, @tele_chatid, @steps, @cfgscale, @prompt, @negative_prompt, @selected_image, @width, @height, @denoising_strength, @seed, @reply_msg, @msg_id, @now, @model)
            ";

            cmd.Parameters.AddWithValue("uid", user.UID);
            cmd.Parameters.AddWithValue("type", q.Type.ToString());
            cmd.Parameters.AddWithValue("tele_id", telegram.User.ID);
            cmd.Parameters.AddWithValue("tele_chatid", telegram.Chat?.ID);
            cmd.Parameters.AddWithValue("steps", settings.steps);
            cmd.Parameters.AddWithValue("cfgscale", settings.cfgscale);
            cmd.Parameters.AddWithValue("prompt", settings.prompt);
            cmd.Parameters.AddWithValue("negative_prompt", settings.negative_prompt);
            cmd.Parameters.AddWithValue("selected_image", type == QueueType.IMG2IMG ? settings.selected_image : null);
            cmd.Parameters.AddWithValue("width", settings.width);
            cmd.Parameters.AddWithValue("height", settings.height);
            cmd.Parameters.AddWithValue("denoising_strength", type == QueueType.IMG2IMG ? settings.denoising_strength : null);
            cmd.Parameters.AddWithValue("seed", settings.seed);
            cmd.Parameters.AddWithValue("reply_msg", replyMessageID);
            cmd.Parameters.AddWithValue("msg_id", messageID);
            cmd.Parameters.AddWithValue("now", q.DateCreated);
            cmd.Parameters.AddWithValue("model", settings.model);

            await cmd.ExecuteNonQueryAsync();

            q.ID = (ulong)cmd.LastInsertedId;

            _= FoxQueue.Enqueue(q);

            return q;
        }

        public async Task Start(FoxWorker worker) {
            UserNotifyTimer.Start();
            this.Worker = worker;

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
                if (this.UserNotifyTimer.ElapsedMilliseconds >= 2000 && progressPercent > 10.0 && this.Telegram is not null)
                {
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

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
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
        public static async Task<FoxQueue?> Get(long id)
        {
            var settings = new FoxUserSettings();
            var q = new FoxQueue();

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                            SELECT queue.*, telegram_users.access_hash as user_access_hash, telegram_chats.access_hash as chat_access_hash
                            FROM queue
                            JOIN telegram_users ON telegram_users.id = queue.tele_id
                            LEFT JOIN telegram_chats ON telegram_chats.id = queue.tele_chatid
                            WHERE queue.id = @id;
                        ";
                    cmd.Parameters.AddWithValue("id", id);

                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                    {
                        q.ID = Convert.ToUInt64(r["id"]);

                        q.User = await FoxUser.GetByUID(Convert.ToInt64(r["uid"]));

                        long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];
                        long? teleChatHash = r["chat_access_hash"] is DBNull ? null : (long)r["chat_access_hash"];

                        q.Telegram = new FoxTelegram((long)r["tele_id"], (long)r["user_access_hash"], teleChatId, teleChatHash);

                        q.Type = Enum.Parse<FoxQueue.QueueType>(Convert.ToString(r["type"]) ?? "UNKNOWN");

                        if (!(r["steps"] is DBNull))
                            settings.steps = Convert.ToInt16(r["steps"]);
                        if (!(r["cfgscale"] is DBNull))
                            settings.cfgscale = Convert.ToDecimal(r["cfgscale"]);
                        if (!(r["prompt"] is DBNull))
                            settings.prompt = Convert.ToString(r["prompt"]);
                        if (!(r["negative_prompt"] is DBNull))
                            settings.negative_prompt = Convert.ToString(r["negative_prompt"]);
                        if (!(r["width"] is DBNull))
                            settings.width = Convert.ToUInt32(r["width"]);
                        if (!(r["height"] is DBNull))
                            settings.height = Convert.ToUInt32(r["height"]);
                        if (!(r["denoising_strength"] is DBNull))
                            settings.denoising_strength = Convert.ToDecimal(r["denoising_strength"]);
                        if (!(r["seed"] is DBNull))
                            settings.seed = Convert.ToInt32(r["seed"]);
                        if (!(r["model"] is DBNull))
                            settings.model = Convert.ToString(r["model"]);
                        if (!(r["reply_msg"] is DBNull))
                            q.ReplyMessageID = Convert.ToInt32(r["reply_msg"]);
                        if (!(r["msg_id"] is DBNull))
                            q.MessageID = Convert.ToInt32(r["msg_id"]);
                        if (!(r["date_added"] is DBNull))
                            q.DateCreated = Convert.ToDateTime(r["date_added"]);
                        if (!(r["link_token"] is DBNull))
                            q.LinkToken = Convert.ToString(r["link_token"]);
                        if (!(r["selected_image"] is DBNull))
                            settings.selected_image = Convert.ToUInt64(r["selected_image"]);
                        if (!(r["image_id"] is DBNull))
                            q.OutputImageID = Convert.ToUInt64(r["image_id"]);
                        if (!(r["worker"] is DBNull))
                            q.WorkerID = Convert.ToInt32(r["worker"]);

                        q.Settings = settings;

                        return q;
                    }                       
                }
            }

            return null;
        }

        public static async Task<(FoxQueue?, int)> Pop(int worker_id)
        {
            var settings = new FoxUserSettings();
            FoxQueue? q = null;

            var processingCount = 0;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var transaction = await SQL.BeginTransactionAsync())
                {
                    try
                    {
                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
SELECT q.*, 
       (SELECT COUNT(*) FROM queue WHERE status IN ('PENDING', 'ERROR')) AS processing_count,
       tu.access_hash AS user_access_hash, 
       tc.access_hash AS chat_access_hash
FROM queue q
INNER JOIN users u ON q.uid = u.id
INNER JOIN worker_models wm ON q.model = wm.model_name AND wm.worker_id = @worker_id
INNER JOIN workers w ON w.id = @worker_id
LEFT JOIN telegram_users tu ON tu.id = q.tele_id
LEFT JOIN telegram_chats tc ON tc.id = q.tele_chatid
WHERE 
    q.status IN ('PENDING', 'ERROR')
    AND (q.retry_date IS NULL OR q.retry_date <= @now)
    AND (w.max_img_size IS NULL OR q.width * q.height <= w.max_img_size)
    AND (w.max_img_steps IS NULL OR q.steps <= w.max_img_steps)
ORDER BY 
    CASE 
        WHEN q.status = 'PENDING' THEN 1
        WHEN q.status = 'ERROR' AND (q.date_failed IS NULL OR q.date_failed < @now - INTERVAL 20 SECOND) THEN 0
        WHEN q.status = 'ERROR' AND (SELECT COUNT(*) FROM queue WHERE status = 'PENDING') = 0 THEN 2
        ELSE 3
    END,
    CASE 
        WHEN u.access_level = 'ADMIN' THEN 0
        WHEN u.access_level = 'PREMIUM' THEN 1
        ELSE 2
    END,
    q.date_added ASC
LIMIT 1
FOR UPDATE;
";
                            cmd.Parameters.AddWithValue("now", DateTime.Now);
                            cmd.Parameters.AddWithValue("worker_id", worker_id);

                            await using var r = await cmd.ExecuteReaderAsync();
                            if (r.HasRows && await r.ReadAsync())
                            {
                                processingCount = Convert.ToInt32(r["processing_count"]);

                                q = new FoxQueue();

                                q.ID = Convert.ToUInt64(r["id"]);

                                q.User = await FoxUser.GetByUID(Convert.ToInt64(r["uid"]));

                                long? teleChatId = r["tele_chatid"] is DBNull ? null : (long)r["tele_chatid"];
                                long? teleChatHash = r["chat_access_hash"] is DBNull ? null : (long)r["chat_access_hash"];

                                q.Telegram = new FoxTelegram((long)r["tele_id"], (long)r["user_access_hash"], teleChatId, teleChatHash);

                                q.Type = Enum.Parse<FoxQueue.QueueType>(Convert.ToString(r["type"]) ?? "UNKNOWN");

                                if (!(r["steps"] is DBNull))
                                    settings.steps = Convert.ToInt16(r["steps"]);
                                if (!(r["cfgscale"] is DBNull))
                                    settings.cfgscale = Convert.ToDecimal(r["cfgscale"]);
                                if (!(r["prompt"] is DBNull))
                                    settings.prompt = Convert.ToString(r["prompt"]);
                                if (!(r["negative_prompt"] is DBNull))
                                    settings.negative_prompt = Convert.ToString(r["negative_prompt"]);
                                if (!(r["selected_image"] is DBNull))
                                    settings.selected_image = Convert.ToUInt64(r["selected_image"]);
                                if (!(r["width"] is DBNull))
                                    settings.width = Convert.ToUInt32(r["width"]);
                                if (!(r["height"] is DBNull))
                                    settings.height = Convert.ToUInt32(r["height"]);
                                if (!(r["denoising_strength"] is DBNull))
                                    settings.denoising_strength = Convert.ToDecimal(r["denoising_strength"]);
                                if (!(r["seed"] is DBNull))
                                    settings.seed = Convert.ToInt32(r["seed"]);
                                if (!(r["model"] is DBNull))
                                    settings.model = Convert.ToString(r["model"]);
                                if (!(r["reply_msg"] is DBNull))
                                    q.ReplyMessageID = Convert.ToInt32(r["reply_msg"]);
                                if (!(r["msg_id"] is DBNull))
                                    q.MessageID = Convert.ToInt32(r["msg_id"]);
                                if (!(r["date_added"] is DBNull))
                                    q.DateCreated = Convert.ToDateTime(r["date_added"]);
                                if (!(r["link_token"] is DBNull))
                                    q.LinkToken = Convert.ToString(r["link_token"]);

                                q.Settings = settings;
                            }
                                
                        }

                        if (q is null)
                        {
                            using var countCmd = new MySqlCommand("SELECT COUNT(*) as p FROM queue WHERE status IN ('PENDING', 'ERROR')", SQL, transaction);

                            processingCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                            if (processingCount < 1)
                                return (null, 0); //This is just here to give me a place to set a breakpoint.

                            return (null, processingCount);
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = $"UPDATE queue SET status = 'PROCESSING' WHERE id = @id";
                            cmd.Parameters.AddWithValue("id", q.ID);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                    } catch  {
                        try {
                            await transaction.RollbackAsync();
                        } catch { } //Something is obviously very broken, not much we can do about it now.

                        throw;
                    }
                }

                q.Settings = settings;
            }

            semaphore.Release();

            return (q, processingCount);
        }

        public static async Task<int> GetCount(FoxUser? user = null)
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
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
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
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
            await this.SaveOutputImage(image);
            await this.SetStatus(QueueStatus.SENDING);

            this.Worker = null;

            _= FoxSendQueue.Send(this);
        }

        public async Task SetWorker(FoxWorker worker)
        {
            this.Worker = worker;
            this.WorkerID = worker.ID;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
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

        public async Task SetError(Exception ex, DateTime? RetryWhen = null)
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = 'ERROR', error_str = @error, retry_date = @retry_date, date_failed = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.ID);
                    cmd.Parameters.AddWithValue("retry_date", RetryWhen);
                    cmd.Parameters.AddWithValue("error", ex.Message);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            try
            {
                if (Telegram is not null)
                {
                    _= Telegram.EditMessageAsync(
                        id: MessageID,
                        text: $"⏳ Error (will re-attempt soon)"
                    );
                }
            }
            catch { }
        }

        public async Task SetCancelled(string reason = "Cancelled by user request")
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = 'CANCELLED', error_str = @reason, date_failed = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.ID);
                    cmd.Parameters.AddWithValue("reason", reason);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private static int GetRandomInt32()
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
        public async Task Cancel()
        {
            this.stopToken.Cancel();
            await this.SetCancelled();
            if (Telegram is not null)
            {
                try
                {
                    await this.Telegram.EditMessageAsync(
                        id: this.MessageID,
                        text: "❌ Cancelled."
                    );
                } catch { }
            }
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

            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

            await SetOutputImage(outputImage);

            return;
        }

        public async Task SetOutputImage(FoxImage value)
        {
            using var SQL = new MySqlConnection(FoxMain.MySqlConnectionString);

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
