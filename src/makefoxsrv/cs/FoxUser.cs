using AsyncKeyedLock;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTelegram;
using makefoxsrv;
using TL;
using System.Globalization;
using System.Linq.Expressions;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Data.SqlTypes;

public enum AccessLevel
{
    BANNED,
    BASIC,
    PREMIUM,
    ADMIN
}

namespace makefoxsrv
{
    public class FoxUser
    {
        private static readonly AsyncKeyedLocker<ulong> _cacheLocks = new();
        private static FoxCache<FoxUser> _userCacheByUID = new(TimeSpan.FromHours(24));

        public ulong UID;
        public string? Username;
        public long? TelegramID;

        public FoxTelegram Telegram
        {
            get
            {
                if (TelegramID is null)
                    throw new InvalidOperationException("TelegramID is null");

                return new FoxTelegram(new TL.User { id = TelegramID.Value }, null);
            }
            set
            {
                TelegramID = value.User.ID;
            }
        }

        // Alias dictionary, thread-safe, just maps Telegram IDs to UIDs
        private static readonly ConcurrentDictionary<long, ulong> _telegramToUid = new ConcurrentDictionary<long, ulong>();

        private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> UserSemaphores = new();
        private static readonly ConcurrentDictionary<ulong, ConcurrentQueue<(DateTime LockTime, string OriginInfo, Task LockingTask)>> LockMetadata = new();

        public DateTime? cachedTime { get; set; } = null;
        public DateTime lastAccessed { get; private set; } = DateTime.Now;

        public DateTime? datePremiumExpires { get; private set; } = null;          //Date premium subscription expires.
        private bool lifetimeSubscription = false;            //Do they have a lifetime sub?
        private AccessLevel accessLevel = AccessLevel.BANNED; //Default to BANNED, just in case.

        public DateTime? DateTermsAccepted { get; set; } = null;

        private decimal? _totalPaid = null;

        // Set the default preferred language to English (United States)
        public string? PreferredLanguage { get; set; } = null;

        public FoxLocalization Strings { get; set; }

        public DateTime? FloodWaitUntil { get; private set; } = null;

        public FoxUser(ulong ID, string language)
        {
            this.UID = ID;
            this.PreferredLanguage = language;
            this.Strings = new FoxLocalization(this, PreferredLanguage ?? "en");
        }

        public static int CacheCount() => _userCacheByUID.Count;

        public void RecordError(Exception ex)
        {
            if (ex is RpcException rex && rex.Code == 420)
                this.FloodWaitUntil = DateTime.Now.AddSeconds(rex.X);
        }

        public async Task<DateTime?> GetFloodWait()
        {

            // If the cached FloodWait value hasn't expired yet, just use that.
            if (this.FloodWaitUntil is not null && this.FloodWaitUntil > DateTime.Now)
                return this.FloodWaitUntil;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
            await SQL.OpenAsync();
            using (var cmd = new MySqlCommand())
            {
                // Check if a flood wait error has been logged in the database in the last 48 hours.
                try
                {
                    var since = DateTime.Now.AddDays(-2);

                    cmd.Connection = SQL;
                    // Make sure message contains "FLOOD_WAIT" and the user_id matches the current user
                    cmd.CommandText = @"
                        SELECT date,exception_json
                        FROM log
                        WHERE user_id = @uid
                          AND date >= @since
                          AND tele_chatid IS NULL
                          AND type = 'ERROR'
                          AND message LIKE '%FLOOD_WAIT_X%'
                        ORDER BY date DESC
                        LIMIT 1";

                    cmd.Parameters.AddWithValue("@uid", this.UID);
                    cmd.Parameters.AddWithValue("@since", since);

                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            String exceptionJson = r.GetString("exception_json");
                            DateTime exceptionDate = r.GetDateTime("date");

                            var exception = JsonConvert.DeserializeObject<RpcException>(exceptionJson);
                            if (exception != null && exception.Code == 420)
                            {
                                this.FloodWaitUntil = exceptionDate.AddSeconds(exception.X);

                                return this.FloodWaitUntil;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);
                }
            }

            return null;
        }

        public static long ClearCache()
        {
            return 0;
        }

        //private static bool CheckIfCacheExpired(FoxUser user)
        //{
        //    // If the user is expired, remove it from the cache and return null
        //    if (user.CachedTime is null || DateTime.Now - user.CachedTime.Value >= CacheDuration)
        //    {
        //        FoxLog.WriteLine($"GetUserFromCache({user.UID}): User has expired from cache, removing.", LogLevel.DEBUG);
        //        userCacheByUID.Remove(user.UID);
        //        return true;
        //    }

        //    return false;
        //}

        public async Task LockAsync(
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            SemaphoreSlim semaphore = UserSemaphores.GetOrAdd(this.UID, _ => new SemaphoreSlim(1, 1));

            // Initialize metadata queue for this user if not already done
            var metadataQueue = LockMetadata.GetOrAdd(this.UID, _ => new ConcurrentQueue<(DateTime, string, Task)>());

            // Capture lock origin information
            string originInfo = $"{callerName} in {callerFilePath}:{lineNumber}";

            // Perform the 5-minute check for existing locks
            if (!metadataQueue.IsEmpty)
            {
                foreach (var metadata in metadataQueue)
                {
                    var lockAge = DateTime.Now - metadata.LockTime;

                    if (lockAge > TimeSpan.FromMinutes(5))
                    {
                        if (!metadata.LockingTask.IsCompleted)
                        {
                            // Task is still running
                            FoxLog.WriteLine($"Warning: Lock on user {this.UID} is over 5 minutes old and task is still running. Locked at {metadata.LockTime} by {metadata.OriginInfo}.", LogLevel.WARNING);
                        }
                        else
                        {
                            // Task is not running; kill the lock
                            if (metadataQueue.TryDequeue(out var staleLock))
                            {
                                FoxLog.WriteLine($"Warning: Lock on user {this.UID} is over 5 minutes old and task is not running. Removing stale lock. Locked at {staleLock.LockTime} by {staleLock.OriginInfo}.", LogLevel.WARNING);
                                semaphore.Release();
                            }
                        }
                    }
                }
            }

            // Create the locking task but do NOT await yet
            var lockingTask = semaphore.WaitAsync();

            // Enqueue the lock metadata with the locking task immediately
            metadataQueue.Enqueue((DateTime.Now, originInfo, lockingTask));

            // Log and await the semaphore acquisition
            FoxLog.WriteLine($"Locking user {this.UID}. Origin: {originInfo}", LogLevel.DEBUG);
            await lockingTask;
        }



        public void Unlock()
        {
            if (UserSemaphores.TryGetValue(this.UID, out var userSemaphore))
            {
                if (LockMetadata.TryGetValue(this.UID, out var metadataQueue) && metadataQueue.TryDequeue(out var metadata))
                {
                    FoxLog.WriteLine($"Unlocking user {this.UID}. Originally locked at {metadata.LockTime} by {metadata.OriginInfo}.", LogLevel.DEBUG);
                }
                else
                {
                    FoxLog.WriteLine($"Unlocking user {this.UID}. No metadata found or queue is empty, lock might have been prematurely released.", LogLevel.WARNING);
                }

                // Release the semaphore
                userSemaphore.Release();
            }
            else
            {
                FoxLog.WriteLine($"Attempt to unlock unlocked user {this.UID}!", LogLevel.WARNING);
            }
        }

        public async Task SetPreferredLanguage(string language)
        {
            this.PreferredLanguage = language;
            this.Strings = new FoxLocalization(this, language);

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = "UPDATE users SET language = @language WHERE id = @uid";
                cmd.Parameters.AddWithValue("@language", language);
                cmd.Parameters.AddWithValue("@uid", this.UID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task SetTermsAccepted(bool accepted = true)
        {
            if (accepted)
                this.DateTermsAccepted = DateTime.Now;
            else
                this.DateTermsAccepted = null;

            using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);

            await SQL.OpenAsync();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = "UPDATE users SET date_terms_accepted = @date_terms_accepted WHERE id = @uid";
                cmd.Parameters.AddWithValue("@date_terms_accepted", this.DateTermsAccepted);
                cmd.Parameters.AddWithValue("@uid", this.UID);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static FoxUser CreateFromRow(MySqlDataReader r)
        {
            var user = new FoxUser(
                ID: Convert.ToUInt64(r["id"]),
                language: (r["language"] == DBNull.Value) ? "en" : (string)r["language"]
            );

            user.Username = r["username"] == DBNull.Value ? null : (string)r["username"];
            user.DateTermsAccepted = r["date_terms_accepted"] == DBNull.Value ? null : (DateTime?)r["date_terms_accepted"];
            user.TelegramID = r["telegram_id"] == DBNull.Value ? null : (long?)r["telegram_id"];
            user.lifetimeSubscription = r["lifetime_subscription"] == DBNull.Value ? false : Convert.ToBoolean(r["lifetime_subscription"]);
            user.datePremiumExpires = r["date_premium_expires"] == DBNull.Value ? null : (DateTime?)r["date_premium_expires"];

            var accessLevelStr = r["access_level"].ToString();
            if (Enum.TryParse<AccessLevel>(accessLevelStr, out var accessLevel))
            {
                user.accessLevel = accessLevel;
            }

            _userCacheByUID.Put(user.UID, user);

            return user;
        }

        public static async Task<FoxUser?> GetByUID(ulong uid)
        {
            FoxUser? user = _userCacheByUID.Get(uid);

            if (user is not null)
            {
                user.lastAccessed = DateTime.Now;

                return user;
            }

            using var _ = await _cacheLocks.LockAsync(uid);

            user = _userCacheByUID.Get(uid);

            if (user is not null)
            {
                user.lastAccessed = DateTime.Now;

                return user;
            }

            try
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand("SELECT * FROM users WHERE id = @id", SQL))
                    {
                        cmd.Parameters.AddWithValue("@id", uid);

                        using (var r = await cmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                user = CreateFromRow(r);
                            }
                        }
                    }
                }
            }
            catch
            {
                // In case of any error, return null.
                return null;
            }

            if (user is not null)
            {
                _userCacheByUID.Put((ulong)uid, user);
                if (user.TelegramID is not null && user.TelegramID != 0)
                    _telegramToUid[user.TelegramID.Value] = user.UID;
            }

            return user;
        }

        public async Task SetUsername(string? newUsername)
        {
            // Don't bother updating if it didn't change.

            if (this.Username is null || newUsername != this.Username)
            {
                this.Username = newUsername;

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var updateCmd = new MySqlCommand("UPDATE users SET username = @username WHERE id = @uid", SQL))
                    {
                        updateCmd.Parameters.AddWithValue("@username", newUsername);
                        updateCmd.Parameters.AddWithValue("@uid", this.UID);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        public static async Task<FoxUser?> GetByTelegramUser(User tuser, bool autoCreateUser = false)
        {
            // First check alias dictionary
            if (_telegramToUid.TryGetValue(tuser.ID, out var uid))
                return await GetByUID(uid);

            FoxUser? user = null;

            // Load from database
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT id FROM users WHERE telegram_id = @id LIMIT 1", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", tuser.ID);

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        user = await GetByUID(Convert.ToUInt64(result)); // ensures cache hit or DB load
                    }
                }

                //if (!tuser.flags.HasFlag(User.Flags.min) && tuser.access_hash != user

                if (user != null && user.Username != tuser.username)
                {
                    // Telegram username changed, a db update is required
                    FoxLog.WriteLine($"Username change: {user.Username} > {tuser.username}");

                    using (var updateCmd = new MySqlCommand("UPDATE users SET username = @username WHERE id = @uid", SQL))
                    {
                        updateCmd.Parameters.AddWithValue("@username", tuser.username);
                        updateCmd.Parameters.AddWithValue("@uid", user.UID);
                        await updateCmd.ExecuteNonQueryAsync();

                        user.Username = tuser.username; // Update the user object with the new username
                    }
                }
            }

            // If the user was not found in the database or cache, create a new FoxUser from the Telegram user
            if (user is null && autoCreateUser)
                user = await CreateFromTelegramUser(tuser);

            if (user is not null)
            {
                user.lastAccessed = DateTime.Now;
                user.Telegram = new FoxTelegram(tuser, null);
            }

            return user;
        }

        public static async Task<FoxUser?> GetByTelegramUserID(long tuserid)
        {
            // Step 1: Check alias dictionary
            if (_telegramToUid.TryGetValue(tuserid, out var uid))
            {
                var cachedUser = await GetByUID(uid);
                if (cachedUser is not null)
                {
                    cachedUser.lastAccessed = DateTime.Now;
                    return cachedUser;
                }
            }

            FoxUser? user = null;

            // Step 2: Fall back to DB
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT id FROM users WHERE telegram_id = @id LIMIT 1", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", tuserid);

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        user = await GetByUID(Convert.ToUInt64(result));
                    }
                }
            }

            // Step 3: Update alias dictionary if we got a match
            if (user is not null)
            {
                user.lastAccessed = DateTime.Now;
            }

            return user;
        }

        public static async Task<FoxUser?> GetByUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
                return null;

            // First try the cache via LINQ
            var cachedUser = _userCacheByUID.Values
                .FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

            if (cachedUser is not null)
            {
                cachedUser.lastAccessed = DateTime.Now;
                return cachedUser;
            }

            // Fallback: database lookup
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT id FROM users WHERE username = @username LIMIT 1", SQL))
                {
                    cmd.Parameters.AddWithValue("@username", username);

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        var uid = Convert.ToUInt64(result);
                        return await GetByUID(uid); // ensures cache hit or DB load
                    }
                }
            }

            return null;
        }


        public static async Task<FoxUser?> CreateFromTelegramUser(User tuser)
        {
            ulong user_id = 0;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO users (telegram_id, username, type, date_last_seen, date_added) VALUES (@telegram_id, @username, 'TELEGRAM_USER', @now, @now)";
                    cmd.Parameters.AddWithValue("telegram_id", tuser.ID);
                    cmd.Parameters.AddWithValue("username", tuser.username);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                        user_id = (ulong)cmd.LastInsertedId;
                        FoxLog.WriteLine($"createUserFromTelegramID({tuser.ID}, \"{tuser.username}\"): User created, UID: " + user_id);
                    }
                    catch (MySqlException ex) when (ex.Number == 1062) // Catch the duplicate entry exception
                    {
                        FoxLog.WriteLine($"createUserFromTelegramID({tuser.ID}, \"{tuser.username}\"): Duplicate telegram_id, fetching existing user.");
                        // If a duplicate user is attempted to be created, fetch the existing user instead
                        return await GetByTelegramUser(tuser, false);
                    }
                }
            }

            if (user_id > 0)
            {
                // Fetch to read the defaults configured on the table
                var newUser = await GetByTelegramUser(tuser, false);

                return newUser;
            }
            else
            {
                // If the user creation was unsuccessful for reasons other than a duplicate entry, this line might not be reached due to the exception handling above.
                throw new Exception("Unable to create new user");
            }
        }

        static public async Task<FoxUser?> ParseUser(string input)
        {
            if (ulong.TryParse(input.Trim(), out ulong uid))
            {
                // Handle input that is a valid long number
                return await GetByUID(uid);
            }
            else if (input.Trim().StartsWith("@"))
            {
                // Remove '@' from the beginning
                string data = input.Substring(1).Trim();

                if (String.IsNullOrEmpty(data))
                    throw new Exception("Empty username");

                // Check if the rest is a long number
                if (long.TryParse(data, out long telegramUserId))
                {
                    return await GetByTelegramUserID(telegramUserId);
                }
                else
                {
                    // Assume it's a username
                    return await GetByUsername(data);
                }
            }

            return null; // Return null if no valid user is found
        }

        public async Task SetPremiumDate(DateTime newExpiry)
        {
            this.datePremiumExpires = newExpiry;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "UPDATE users SET date_premium_expires = @date_premium_expires WHERE id = @uid";
                    cmd.Parameters.AddWithValue("@date_premium_expires", newExpiry);
                    cmd.Parameters.AddWithValue("@uid", this.UID);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            FoxLog.WriteLine($"SetPremiumDate({this.UID}, {newExpiry})");
        }

        public async Task<ulong> RecordPayment(PaymentTypes type, int amount, string currency, int days, string? invoice_payload = null, string? telegram_charge_id = null, string? provider_charge_id = null, int replyMessageId = 0)
        {
            ulong payment_id = 0;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO user_payments (type, uid, date, amount, currency, days, invoice_payload, telegram_charge_id, provider_charge_id) VALUES (@type, @uid, @now, @amount, @currency, @days, @invoice_payload, @telegram_charge_id, @provider_charge_id)";
                    cmd.Parameters.AddWithValue("uid", this.UID);
                    cmd.Parameters.AddWithValue("type", type.ToString().ToUpperInvariant());
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    cmd.Parameters.AddWithValue("amount", amount);
                    cmd.Parameters.AddWithValue("currency", currency);
                    cmd.Parameters.AddWithValue("days", days);
                    cmd.Parameters.AddWithValue("invoice_payload", invoice_payload);
                    cmd.Parameters.AddWithValue("telegram_charge_id", telegram_charge_id);
                    cmd.Parameters.AddWithValue("provider_charge_id", provider_charge_id);

                    await cmd.ExecuteNonQueryAsync();
                    payment_id = (ulong)cmd.LastInsertedId;

                    FoxLog.WriteLine($"recordPayment({this.UID}, {amount} {currency}, {days} days)");
                }

                if (_totalPaid is null)
                    _totalPaid = 0m;

                if (currency == "XTR")
                    _totalPaid += (amount * 0.013m);
                else
                    _totalPaid += (amount / 100m);

                //using (var cmd = new MySqlCommand())
                //{
                //    cmd.Connection = SQL;
                //    cmd.CommandText = @"
                //        UPDATE users
                //        SET 
                //            date_premium_expires = IF(
                //                @days = -1, 
                //                '9999-12-31',
                //                IF(
                //                    date_premium_expires IS NULL OR date_premium_expires < @now OR lifetime_subscription = 1, 
                //                    DATE_ADD(@now, INTERVAL @days DAY), 
                //                    DATE_ADD(date_premium_expires, INTERVAL @days DAY)
                //                )
                //            ),
                //            lifetime_subscription = IF(@days = -1, 1, lifetime_subscription)
                //        WHERE id = @uid AND (lifetime_subscription IS NULL OR lifetime_subscription = 0);";
                //    cmd.Parameters.AddWithValue("uid", this.UID);
                //    cmd.Parameters.AddWithValue("now", DateTime.Now);
                //    cmd.Parameters.AddWithValue("days", days);

                //    await cmd.ExecuteNonQueryAsync();
                //    payment_id = (ulong)cmd.LastInsertedId;
                //}
            }

            if (days < 0)
                days = 99999; // Lifetime subscription (a cheap hack for now)

            if (this.datePremiumExpires > DateTime.Now)
            {
                await this.SetPremiumDate(this.datePremiumExpires.Value.AddDays(days));
            }
            else
            {
                await this.SetPremiumDate(DateTime.Now.AddDays(days));
            }

            //var teleUser = TelegramID is not null ? await FoxTelegram.GetUserFromID(TelegramID.Value) : null;
            //var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

            var msg = @$"
            <b>Thank You for Your Generous Support!</b>

            We are deeply grateful for your membership, which is vital for our platform's sustainability and growth. 

            Your contribution has granted you <b>{(days == -1 ? "lifetime" : $"{days} days of")} enhanced access</b>, improving your experience with increased limits and features. 

            We are committed to using your membership fees to further develop and maintain the service, supporting our mission to provide a creative and expansive platform for our users. Thank you for being an integral part of our journey and for empowering us to continue offering a high-quality service.

            <b>MakeFox Group, Inc.</b>
            ";

            var entities = FoxTelegram.Client.HtmlToEntities(ref msg);

            await Telegram.SendMessageAsync(
                        text: msg,
                        replyToMessageId: replyMessageId,
                        entities: entities,
                        disableWebPagePreview: true
                    );

            return payment_id;
        }

        public async Task UpdateTimestamps()
        {
            this.lastAccessed = DateTime.Now;
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "UPDATE users SET date_last_seen = @now WHERE id = " + this.UID;
                    cmd.Parameters.AddWithValue("now", this.lastAccessed);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task SetAccessLevel(AccessLevel level)
        {
            accessLevel = level;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "UPDATE users SET access_level = @level WHERE id = @uid";
                    cmd.Parameters.AddWithValue("uid", this.UID);
                    cmd.Parameters.AddWithValue("level", level.ToString());
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public bool CheckAccessLevel(AccessLevel requiredLevel)
        {
            // Check if the current user's access level is greater than or equal to the required level
            return this.GetAccessLevel() >= requiredLevel;
        }

        public AccessLevel GetAccessLevel()
        {

            // Check for premium upgrade conditions
            if ((this.accessLevel == AccessLevel.BASIC && this.lifetimeSubscription) ||
                (this.accessLevel == AccessLevel.BASIC && this.datePremiumExpires.HasValue && this.datePremiumExpires.Value >= DateTime.Now))
            {
                return AccessLevel.PREMIUM;
            }
            else
                return this.accessLevel;
        }

        public static Task<List<FoxQueue>> LoadAllActiveForUser(FoxUser user) =>
            LoadAllActiveForUser(user.UID);

        public static async Task<List<FoxQueue>> LoadAllActiveForUser(ulong UID)
        {
            var ids = new List<ulong>();

            using var conn = new MySqlConnection(FoxMain.sqlConnectionString);
            await conn.OpenAsync();

            string query = @"
                SELECT id FROM queue 
                WHERE uid = @uid AND status NOT IN ('CANCELLED', 'FINISHED')";

            using var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uid", UID);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(Convert.ToUInt64(reader["id"]));

            var list = new List<FoxQueue>();
            foreach (var id in ids)
            {
                var q = await FoxQueue.Get(id);
                if (q != null)
                    list.Add(q);
            }

            return list;
        }


        public async Task Ban(bool silent = false, string? reasonMessage = null)
        {
            await SetAccessLevel(AccessLevel.BANNED);

            var teleUser = TelegramID is not null ? await FoxTelegram.GetUserFromID(TelegramID.Value) : null;
            var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

            var matchingItems = await LoadAllActiveForUser(this.UID);

            foreach (var q in matchingItems)
            {
                int msg_id = q.MessageID;

                await q.Cancel();

                try
                {
                    if (t is not null)
                    {
                        _ = t.EditMessageAsync(
                            id: msg_id,
                            text: "❌ Cancelled."
                        );
                    }
                }
                catch (Exception ex)
                {
                    //Don't care about this failure.
                    FoxLog.WriteLine($"Failed to edit message {msg_id}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            try
            {
                if (!silent && t is not null)
                {

                    if (reasonMessage is null)
                    {
                        await t.SendMessageAsync(
                            text: $"⛔ You have been banned from using this service due to a content policy violation.  I will no longer respond to your commands.\r\n\r\n📞 You may contact @makefoxhelpbot for support."
                        );
                    }
                    else
                    {
                        await t.SendMessageAsync(
                            text: $"⛔ You have been banned from using this service.  I will no longer respond to your commands.\r\n\r\nReason: {reasonMessage}\r\n\r\n📞 You may contact @makefoxhelpbot for support."
                        );
                    }
                }
            }
            catch
            {
                // Ignore any errors
            }
        }

        public async Task UnBan(bool silent = false, string? reasonMessage = null)
        {
            await SetAccessLevel(AccessLevel.BASIC);

            var teleUser = TelegramID is not null ? await FoxTelegram.GetUserFromID(TelegramID.Value) : null;
            var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

            try
            {
                if (!silent && t is not null)
                {

                    if (reasonMessage is null)
                    {
                        await t.SendMessageAsync(
                            text: $"🎉 Your account restrictions have been lifted."
                        );
                    }
                    else
                    {
                        await t.SendMessageAsync(
                            text: $"🎉 Your account restrictions have been lifted.\n\nMessage from Admin: {reasonMessage}"
                        );
                    }
                }
            }
            catch
            {
                // Ignore any errors
            }
        }

        public async Task<decimal> GetTotalPaid()
        {
            if (_totalPaid is null)
            {
                using var sqlConn = new MySqlConnection(FoxMain.sqlConnectionString);
                await sqlConn.OpenAsync();

                var sqlcmd = new MySqlCommand("SELECT SUM(amount) as total_paid FROM user_payments WHERE uid = @uid", sqlConn);
                sqlcmd.Parameters.AddWithValue("@uid", this.UID);

                using (var reader = await sqlcmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        _totalPaid = reader.IsDBNull(reader.GetOrdinal("total_paid")) ? null : (reader.GetInt64("total_paid") / 100.0m);
                    }
                }
            }

            return _totalPaid ?? 0m;
        }

        public static int UserCount(DateTime? since = null)
        {
            int count = 0;

            if (since is null)
                since = DateTime.MinValue;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                SQL.Open();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT COUNT(*) FROM users WHERE date_added >= @since";
                    cmd.Parameters.AddWithValue("since", since);

                    count = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            return count;
        }

        static public async Task<List<(string Display, string Paste)>> GetSuggestions(string query)
        {
            var suggestions = new List<(string Display, string Paste)>();

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();
                MySqlCommand command;

                if (query.StartsWith("@"))
                {
                    query = query.Substring(1);
                    command = new MySqlCommand("SELECT username, id FROM users WHERE username LIKE @query LIMIT 30", connection);
                    command.Parameters.AddWithValue("@query", query + "%");

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var username = reader.GetString("username");
                            var display = $"@{username}";
                            suggestions.Add((display, display));
                        }
                    }
                }
                else if (query.StartsWith("#"))
                {
                    query = query.Substring(1);
                    command = new MySqlCommand("SELECT username, firstname, lastname, id FROM telegram_users WHERE id LIKE @query LIMIT 30", connection);
                    command.Parameters.AddWithValue("@query", query + "%");

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var id = reader.GetInt64("id");
                            var username = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString("username");
                            var firstname = reader.IsDBNull(reader.GetOrdinal("firstname")) ? string.Empty : reader.GetString("firstname");
                            var lastname = reader.IsDBNull(reader.GetOrdinal("lastname")) ? string.Empty : reader.GetString("lastname");
                            var fullname = $"{firstname} {lastname}".Trim();
                            var display = username != null ? $"@{username}" : (!string.IsNullOrEmpty(fullname) ? fullname : $"#{id}");
                            var paste = username != null ? $"@{username}" : $"#{id}";
                            suggestions.Add((display, paste));
                        }
                    }
                }
                else if (long.TryParse(query, out _))
                {
                    command = new MySqlCommand("SELECT username, id FROM users WHERE id LIKE @query OR username LIKE @query LIMIT 30", connection);
                    command.Parameters.AddWithValue("@query", query + "%");

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var id = reader.GetInt64("id");
                            var username = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString("username");

                            var display = username != null ? $"@{username}" : id.ToString();
                            var paste = username != null ? $"@{username}" : id.ToString();
                            suggestions.Add((display, paste));
                        }
                    }
                }
                else
                {
                    command = new MySqlCommand("SELECT firstname, lastname, username, id FROM telegram_users WHERE firstname LIKE @query OR lastname LIKE @query LIMIT 30", connection);
                    command.Parameters.AddWithValue("@query", query + "%");

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var firstname = reader.IsDBNull(reader.GetOrdinal("firstname")) ? string.Empty : reader.GetString("firstname");
                            var lastname = reader.IsDBNull(reader.GetOrdinal("lastname")) ? string.Empty : reader.GetString("lastname");
                            var fullname = $"{firstname} {lastname}".Trim();
                            var id = reader.GetInt64("id");
                            var username = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString("username");
                            var display = fullname;
                            var paste = username != null ? $"@{username}" : $"#{id}";
                            suggestions.Add((display, paste));
                        }
                    }
                }
            }

            return suggestions;
        }

    }
}
