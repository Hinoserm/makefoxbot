using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTelegram;
using makefoxsrv;
using TL;

public enum AccessLevel
{
    BANNED,
    BASIC,
    PREMIUM,
    ADMIN
}


namespace makefoxsrv
{
    internal class FoxTelegramUser
    {
        public string username = "";
        public string firstname = "";
        public string lastname = "";
        public string display_name = "";
        public bool is_premium = false;
        public DateTime date_updated;
        
        public static async Task<FoxTelegramUser?> Get(long tele_userid)
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    var u = new FoxTelegramUser();

                    cmd.Connection = SQL;
                    cmd.CommandText = cmd.CommandText = "SELECT * FROM telegram_users WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", tele_userid);

                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                    {
                        if (!(r["username"] is DBNull))
                            u.username = Convert.ToString(r["username"]);
                        if (!(r["firstname"] is DBNull))
                            u.firstname = Convert.ToString(r["firstname"]);
                        if (!(r["lastname"] is DBNull))
                            u.lastname = Convert.ToString(r["lastname"]);
                        if (!(r["is_premium"] is DBNull))
                            u.is_premium = Convert.ToBoolean(r["is_premium"]);
                        if (!(r["date_updated"] is DBNull))
                            u.date_updated = Convert.ToDateTime(r["date_updated"]);

                        string fullName = string.Empty;
                        if (!string.IsNullOrEmpty(u.firstname) || !string.IsNullOrEmpty(u.lastname))
                        {
                            fullName = "(" + $"{u.firstname} {u.lastname}".Replace("  ", " ").Trim() + ")";
                        }

                        string username = string.IsNullOrEmpty(u.username) ? string.Empty : $"{u.username} ";

                        u.display_name = username + fullName;

                        return u;
                    }
                }
            }

            return null;
        }

    }

    internal class FoxTelegramChat
    {
        public string username = "";
        public string firstname = "";
        public string lastname = "";
        public string title = "";
        public DateTime date_updated;

        public static async Task<FoxTelegramChat?> Get(long tele_userid)
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    var u = new FoxTelegramChat();

                    cmd.Connection = SQL;
                    cmd.CommandText = cmd.CommandText = "SELECT * FROM telegram_chats WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", tele_userid);

                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                    {
                        if (!(r["username"] is DBNull))
                            u.username = Convert.ToString(r["username"]);
                        if (!(r["firstname"] is DBNull))
                            u.firstname = Convert.ToString(r["firstname"]);
                        if (!(r["lastname"] is DBNull))
                            u.lastname = Convert.ToString(r["lastname"]);
                        if (!(r["title"] is DBNull))
                            u.title = Convert.ToString(r["title"]);
                        if (!(r["date_updated"] is DBNull))
                            u.date_updated = Convert.ToDateTime(r["date_updated"]);

                        if (string.IsNullOrEmpty(u.title))
                        {
                            string fullName = string.Empty;
                            if (!string.IsNullOrEmpty(u.firstname) || !string.IsNullOrEmpty(u.lastname))
                            {
                                fullName = "(" + $"{u.firstname} {u.lastname}".Replace("  ", " ").Trim() + ")";
                            }

                            string username = string.IsNullOrEmpty(u.username) ? string.Empty : $"{u.username} ";

                            u.title = username + fullName;
                        }

                        return u;
                    }
                }
            }

            return null;
        }

    }

    internal class FoxUser
    {
        public ulong UID;
        public string? Username;
        public long? TelegramID;

        private DateTime? datePremiumExpires = null;          //Date premium subscription expires.
        private bool lifetimeSubscription = false;            //Do they have a lifetime sub?
        private AccessLevel accessLevel = AccessLevel.BANNED; //Default to BANNED, just in case.

        public static async Task<FoxUser?> GetByUID(long uid)
        {
            FoxUser? user = null;

            try
            {
                using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand("SELECT id, username, access_level, telegram_id, lifetime_subscription, date_premium_expires FROM users WHERE id = @id", SQL))
                    {
                        cmd.Parameters.AddWithValue("@id", uid);

                        using (var r = await cmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                user = new FoxUser
                                {
                                    UID = Convert.ToUInt64(r["id"]),
                                    Username = r["username"] == DBNull.Value ? null : (string)r["username"],
                                    TelegramID = r["telegram_id"] == DBNull.Value ? null : (long?)r["telegram_id"],
                                    lifetimeSubscription = r["lifetime_subscription"] == DBNull.Value ? false : Convert.ToBoolean(r["lifetime_subscription"]),
                                    datePremiumExpires = r["date_premium_expires"] == DBNull.Value ? null : (DateTime?)r["date_premium_expires"]
                                };

                                var accessLevelStr = (string)r["access_level"];
                                if (Enum.TryParse<AccessLevel>(accessLevelStr, true, out var accessLevel))
                                {
                                    user.accessLevel = accessLevel;
                                }
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

            return user;
        }

        public static async Task<FoxUser?> GetByTelegramUser(User tuser, bool autoCreateUser = false)
        {
            FoxUser? user = null;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT id, username, access_level, lifetime_subscription, date_premium_expires FROM users WHERE telegram_id = @id", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", tuser.ID);

                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            user = new FoxUser
                            {
                                UID = Convert.ToUInt64(r["id"]),
                                Username = r["username"] == DBNull.Value ? null : (string)r["username"],
                                TelegramID = tuser.ID,
                                lifetimeSubscription = r["lifetime_subscription"] != DBNull.Value && Convert.ToBoolean(r["lifetime_subscription"]),
                                datePremiumExpires = r["date_premium_expires"] != DBNull.Value ? Convert.ToDateTime(r["date_premium_expires"]) : null
                            };

                            var accessLevelStr = r["access_level"].ToString();
                            if (Enum.TryParse<AccessLevel>(accessLevelStr, out var accessLevel))
                            {
                                user.accessLevel = accessLevel;
                            }
                        }
                    }
                }

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

            // If the user was not found in the database, create a new FoxUser from the Telegram user
            if (user is null && autoCreateUser)
                user = await CreateFromTelegramUser(tuser);

            return user;
        }


        public static async Task<FoxUser?> CreateFromTelegramUser(User tuser)
        {
            ulong user_id = 0;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO users (telegram_id, username, type, date_last_seen, date_added) VALUES (@telegram_id, @username, 'TELEGRAM_USER', CURRENT_TIMESTAMP(), CURRENT_TIMESTAMP())";
                    cmd.Parameters.AddWithValue("telegram_id", tuser.ID);
                    cmd.Parameters.AddWithValue("username", tuser.username);

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
                        return await GetByTelegramUser(tuser);
                    }
                }
            }

            if (user_id > 0)
                return await GetByTelegramUser(tuser); // Fetch to read the defaults configured on the table.
            else
                // If the user creation was unsuccessful for reasons other than a duplicate entry, this line might not be reached due to the exception handling above.
                throw new Exception("Unable to create new user");
        }

        public async Task<ulong> recordPayment(int amount, string currency, int days, string invoice_payload = null, string telegram_charge_id = null, string provider_charge_id = null)
        {
            ulong payment_id = 0;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO user_payments (uid, date, amount, currency, days, invoice_payload, telegram_charge_id, provider_charge_id) VALUES (@uid, @now, @amount, @currency, @days, @invoice_payload, @telegram_charge_id, @provider_charge_id)";
                    cmd.Parameters.AddWithValue("uid", this.UID);
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

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                        UPDATE users
                        SET 
                            date_premium_expires = IF(
                                @days = -1, 
                                '9999-12-31',
                                IF(
                                    date_premium_expires IS NULL OR date_premium_expires < @now OR lifetime_subscription = 1, 
                                    DATE_ADD(@now, INTERVAL @days DAY), 
                                    DATE_ADD(date_premium_expires, INTERVAL @days DAY)
                                )
                            ),
                            lifetime_subscription = IF(@days = -1, 1, lifetime_subscription)
                        WHERE id = @uid AND (lifetime_subscription IS NULL OR lifetime_subscription = 0);";
                    cmd.Parameters.AddWithValue("uid", this.UID);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);
                    cmd.Parameters.AddWithValue("days", days);

                    await cmd.ExecuteNonQueryAsync();
                    payment_id = (ulong)cmd.LastInsertedId;
                }
            }

            return payment_id;
        }

        public async Task UpdateTimestamps()
        {
            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "UPDATE users SET date_last_seen = CURRENT_TIMESTAMP() WHERE id = " + this.UID;
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
                (this.accessLevel == AccessLevel.BASIC && this.datePremiumExpires.HasValue && this.datePremiumExpires.Value >= DateTime.UtcNow))
            {
                return AccessLevel.PREMIUM;
            } else
                return this.accessLevel;
        }

        private FoxUser() {

        }
    }
}
