using MySqlConnector;
using Newtonsoft.Json.Linq;
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
using Telegram.Bot;
using Telegram.Bot.Types;

namespace makefoxbot
{
    internal class FoxQueue
    {
        public enum QueueStatus
        {
            PENDING,
            PROCESSING,
            SENDING,
            FINISHED,
            ERROR
        }

        public QueueStatus status = QueueStatus.PROCESSING;

        public FoxUserSettings? settings = null;
        public FoxUser? user = null;
        public ulong id;
        public ulong UID;
        public long TelegramUserID;
        public long TelegramChatID;
        public long reply_msg;
        public string type;
        public int msg_id;
        public DateTime creation_time;
        public DateTime? date_sent = null;
        public string? link_token = null;

        public long position = 0;
        public long total = 0;

        public ulong? image_id = null;

        public FoxImage? output_image = null;
        public FoxImage? input_image = null;

        public static string GenerateShortId(int length = 8)
        {
            const string AlphanumericCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

            if (length <= 0 || length > 22) // Length limited due to the size of the MD5 hash
                throw new ArgumentOutOfRangeException("length", "Length must be between 1 and 22.");

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
                return sb.ToString().Substring(0, length);
            }
        }

        public static async Task<string> CreateLinkToken()
        {

            string token;

            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
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

            return token;
        }
        public async Task<long> CheckPosition()
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = cmd.CommandText = @"
                        SELECT
                            q1.position_in_queue,
                            q1.total_pending
                        FROM
                            (SELECT
                                    id,
                                    ROW_NUMBER() OVER(ORDER BY date_added ASC) AS position_in_queue,
                                    COUNT(*) OVER() AS total_pending
                                FROM
                                    queue
                                WHERE
                                    status = 'PENDING') AS q1
                        WHERE
                            q1.id = @id;
                        ";
                    cmd.Parameters.AddWithValue("id", this.id);

                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                    {
                        this.position = Convert.ToInt64(r["position_in_queue"]);
                        this.total    = Convert.ToInt64(r["total_pending"]);

                        return this.position;
                    }
                }
            }

            return (long)this.id;
        }

        private static CancellationTokenSource notify_cts = new CancellationTokenSource();
        public static async Task NotifyUserPositions(ITelegramBotClient botClient)
        {

            return;

            try
            {
                notify_cts.Cancel();
                notify_cts = new CancellationTokenSource();

                using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
                {
                    await SQL.OpenAsync();

                    long totalPending = 0;

                    using (var totalCmd = new MySqlCommand())
                    {
                        totalCmd.Connection = SQL;
                        totalCmd.CommandText = "SELECT COUNT(*) FROM queue WHERE status = 'PENDING'";
                        totalPending = Convert.ToInt64(await totalCmd.ExecuteScalarAsync());
                    }

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = cmd.CommandText = @"
                            SELECT
                                id,
                                msg_id,
                                tele_chatid,
                                ROW_NUMBER() OVER (ORDER BY date_added ASC) AS position_in_queue
                            FROM
                                queue
                            WHERE
                                status = 'PENDING';
                            ";

                        await using var r = await cmd.ExecuteReaderAsync();
                        while (await r.ReadAsync() && !notify_cts.IsCancellationRequested)
                        {
                            var id = Convert.ToInt64(r["id"]);
                            var position = Convert.ToInt64(r["position_in_queue"]);
                            var msg_id = Convert.ToInt32(r["msg_id"]);
                            var chatid = Convert.ToInt64(r["tele_chatid"]);

                            try
                            {
                                await botClient.EditMessageTextAsync(
                                    chatId: chatid,
                                    messageId: msg_id,
                                    text: $"⏳ In queue ({position} of {totalPending})..."
                                );
                            }
                            catch { } //We don't care if editing fails.
                        }
                    }
                }
            } catch (Exception ex)
            {
                Console.WriteLine("Error in NotifyUserPositions(): " + ex.Message);
            }
        }

        public static async Task<FoxQueue?> Get(long id)
        {
            var settings = new FoxUserSettings();
            var q = new FoxQueue();

            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = cmd.CommandText = "SELECT * FROM queue WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", id);

                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                    {
                        q.id = Convert.ToUInt64(r["id"]);
                        q.UID = Convert.ToUInt64(r["uid"]);
                        q.TelegramUserID = Convert.ToInt64(r["tele_id"]);
                        q.TelegramChatID = Convert.ToInt64(r["tele_chatid"]);
                        q.type = Convert.ToString(r["type"]);

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
                        if (!(r["reply_msg"] is DBNull))
                            q.reply_msg = Convert.ToInt32(r["reply_msg"]);
                        if (!(r["msg_id"] is DBNull))
                            q.msg_id = Convert.ToInt32(r["msg_id"]);
                        if (!(r["date_added"] is DBNull))
                            q.creation_time = Convert.ToDateTime(r["date_added"]);
                        if (!(r["link_token"] is DBNull))
                            q.link_token = Convert.ToString(r["link_token"]);
                        if (!(r["image_id"] is DBNull))
                            q.image_id = Convert.ToUInt64(r["image_id"]);

                        q.settings = settings;

                        return q;
                    }                       
                }
            }

            return null;
        }

        public static async Task<FoxQueue?> Pop()
        {
            var settings = new FoxUserSettings();
            var q = new FoxQueue();

            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
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
    SELECT q.*
    FROM queue q
    INNER JOIN users u ON q.uid = u.id
    WHERE 
        q.status IN ('PENDING', 'ERROR')
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
    FOR UPDATE;";

                            cmd.Parameters.AddWithValue("now", DateTime.Now);

                            await using var r = await cmd.ExecuteReaderAsync();
                            if (r.HasRows && await r.ReadAsync())
                            {
                                q.id = Convert.ToUInt64(r["id"]);
                                q.UID = Convert.ToUInt64(r["uid"]);
                                q.TelegramUserID = Convert.ToInt64(r["tele_id"]);
                                q.TelegramChatID = Convert.ToInt64(r["tele_chatid"]);
                                q.type = Convert.ToString(r["type"]);

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
                                if (!(r["reply_msg"] is DBNull))
                                    q.reply_msg = Convert.ToInt32(r["reply_msg"]);
                                if (!(r["msg_id"] is DBNull))
                                    q.msg_id = Convert.ToInt32(r["msg_id"]);
                                if (!(r["date_added"] is DBNull))
                                    q.creation_time = Convert.ToDateTime(r["date_added"]);
                                if (!(r["link_token"] is DBNull))
                                    q.link_token = Convert.ToString(r["link_token"]);

                                q.settings = settings;
                            }
                            else
                                return null;
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = $"UPDATE queue SET status = 'PROCESSING' WHERE id = @id";
                            cmd.Parameters.AddWithValue("id", q.id);
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

                q.settings = settings;
            }

            return q;
        }

        public static async Task<int> GetCountByUser(FoxUser user)
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"SELECT COUNT(id) FROM queue WHERE (status = 'PENDING' OR status = 'ERROR' OR status = 'PROCESSING') AND uid = " + user.UID;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.HasRows && await reader.ReadAsync())
                        return reader.GetInt32(0);
                }
            }

            return 0;
        }


        public async Task SaveOutputImage(byte[] img, string? filename = null)
        {
            var fname = filename;
            if (fname is null && this.link_token is not null)
                fname = this.link_token + ".png";
                
            this.output_image = await FoxImage.Create(this.UID, img, FoxImage.ImageType.OUTPUT, fname);

            using var SQL = new MySqlConnection(Program.MySqlConnectionString);
            
            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = $"UPDATE queue SET image_id = @image_id WHERE id = @id";
                cmd.Parameters.AddWithValue("id", this.id);
                cmd.Parameters.AddWithValue("image_id", this.output_image.ID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<FoxImage?> LoadOutputImage()
        {

            using var SQL = new MySqlConnection(Program.MySqlConnectionString);
            await SQL.OpenAsync();


            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = "SELECT image_id FROM queue WHERE id = @id";
                cmd.Parameters.AddWithValue("id", this.id);
                var result = await cmd.ExecuteScalarAsync();

                if (result is not null && result is not DBNull)
                    return await FoxImage.Load(Convert.ToUInt64(result));
            }

            return null;
        }

        public async Task Finish()
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = 'FINISHED', date_finished = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.id);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task SetSending()
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET date_sent = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.id);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task SetWorker(string worker)
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET worker = @worker, date_worker_start = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.id);
                    cmd.Parameters.AddWithValue("worker", worker);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task Finish(Exception ex)
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = $"UPDATE queue SET status = 'ERROR', error_str = @error, date_failed = @now WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", this.id);
                    cmd.Parameters.AddWithValue("error", ex.Message);
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

        public static async Task<FoxQueue?> Add(FoxUser user, FoxUserSettings settings, string type, int msg_id, long? reply_msg = null)
        {
            using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                var q = new FoxQueue();

                q.TelegramUserID = settings.TelegramUserID;
                q.TelegramChatID = settings.TelegramChatID;
                q.UID = user.UID;
                q.user = user;
                q.creation_time = DateTime.Now;
                q.link_token = await CreateLinkToken();
                q.type = type;

                if (settings.seed == -1)
                    settings.seed = GetRandomInt32();

                q.settings = settings;

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO queue (status, type, uid, tele_id, tele_chatid, steps, cfgscale, prompt, negative_prompt, selected_image, width, height, denoising_strength, seed, reply_msg, msg_id, date_added, link_token) VALUES ('PENDING', @type, " + user.UID + ", @tele_id, @tele_chatid, @steps, @cfgscale, @prompt, @negative_prompt, @selected_image, @width, @height, @denoising_strength, @seed, @reply_msg, @msg_id, @now, @token)";
                    cmd.Parameters.AddWithValue("type", q.type);
                    cmd.Parameters.AddWithValue("tele_id", q.TelegramUserID);
                    cmd.Parameters.AddWithValue("tele_chatid", q.TelegramChatID);
                    cmd.Parameters.AddWithValue("steps", settings.steps);
                    cmd.Parameters.AddWithValue("cfgscale", settings.cfgscale);
                    cmd.Parameters.AddWithValue("prompt", settings.prompt);
                    cmd.Parameters.AddWithValue("negative_prompt", settings.negative_prompt);
                    cmd.Parameters.AddWithValue("selected_image", settings.selected_image);
                    cmd.Parameters.AddWithValue("width", settings.width);
                    cmd.Parameters.AddWithValue("height", settings.height);
                    cmd.Parameters.AddWithValue("denoising_strength", settings.denoising_strength);
                    cmd.Parameters.AddWithValue("seed", settings.seed);
                    cmd.Parameters.AddWithValue("reply_msg", reply_msg);
                    cmd.Parameters.AddWithValue("msg_id", msg_id);
                    cmd.Parameters.AddWithValue("now", q.creation_time);
                    cmd.Parameters.AddWithValue("token", q.link_token);

                    await cmd.ExecuteNonQueryAsync();

                    q.id = (ulong)cmd.LastInsertedId;

                    return q;
                }

            }

            return null;
        }
    }
}
