using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    internal class FoxWebSessions
    {
        public static async Task<FoxUser?> GetUserFromSession(string sessionId)
        {
            try
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand("SELECT * FROM sessions WHERE session_id = @session_id", SQL))
                    {
                        cmd.Parameters.AddWithValue("@session_id", sessionId);

                        using (var r = await cmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                return await FoxUser.GetByUID(r.GetInt64("uid"));
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                // In case of any error, return null.
                FoxLog.WriteLine($"SQL error while getting user for session id {sessionId}: {ex.Message}\r\n{ex.StackTrace}", LogLevel.ERROR);
                return null;
            }

            return null;
        }

        public static async Task<String> CreateSession(long uid)
        {
            try
            {
                var sessionId = GenerateSessionId();

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand("INSERT INTO sessions (uid, session_id) VALUES (@uid, @session_id)", SQL))
                    {
                        cmd.Parameters.AddWithValue("@uid", uid);
                        cmd.Parameters.AddWithValue("@session_id", sessionId);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return sessionId;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        private static string GenerateSessionId(int length = 26)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[length];
            var sessionId = new StringBuilder(length);

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            foreach (var b in bytes)
            {
                sessionId.Append(chars[b % chars.Length]);
            }

            return sessionId.ToString();
        }
    }
}
