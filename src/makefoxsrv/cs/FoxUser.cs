using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;


namespace makefoxbot
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
        public ulong UID = 0;
        public string? Username;
        public string AccessLevel = "BASIC";
        public long TelegramID = 0;

        public static async Task<FoxUser?> GetByTelegramUser(User tuser)
        {
            FoxUser? user = null;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT id, username, access_level FROM users WHERE telegram_id = @id";
                    cmd.Parameters.AddWithValue("id", tuser.Id);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (r.HasRows && await r.ReadAsync())
                    {
                        user = new FoxUser();

                        user.UID = r.GetUInt64(0);
                        if (!r.IsDBNull(1))
                            user.Username = r.GetString(1);
                        if (!r.IsDBNull(2))
                            user.AccessLevel = r.GetString(2);
                    }
                }

                if (user is not null && user.Username != tuser.Username)
                {
                    //Telegram username changed, a db update is required

                    Console.WriteLine($"Username change: {user.Username} > {tuser.Username}");

                    user.Username = tuser.Username;

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = $"UPDATE users SET username = @username WHERE id = {user.UID}";
                        cmd.Parameters.AddWithValue("username", user.Username);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }

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
                    cmd.Parameters.AddWithValue("telegram_id", tuser.Id);
                    cmd.Parameters.AddWithValue("username", tuser.Username);
                    await cmd.ExecuteNonQueryAsync();
                    user_id = (ulong)cmd.LastInsertedId;

                    Console.WriteLine($"createUserFromTelegramID({tuser.Id}, \"{tuser.Username}\"): User created, UID: " + user_id);
                }
            }

            if (user_id > 0)
                return await GetByTelegramUser(tuser); //We do it this way so that it can read the defaults that are configured on the table.
            else
                throw new Exception("Unable to create new user");
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

        private FoxUser() {

        }
    }
}
