using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using EmbedIO.WebSockets;
using System.Runtime.CompilerServices;
using EmbedIO.Sessions;
using static System.Collections.Specialized.BitVector32;
using System.Text.Json.Nodes;

#pragma warning disable CS1998

namespace makefoxsrv
{
    public class FoxWebSession
    {
        public string Id { get; private set; }
        public FoxUser? user { get; private set; } = null;

        public List<IHttpContext> HttpContexts { get; private set; } = new List<IHttpContext>();

        public class WebsocketConnection
        {
            public IWebSocketContext wsContext { get; set; } = default!;
            public List<QueueSubscription> QueueSubscriptions { get; } = new();

            public WebsocketConnection()
            {
                // Do nothing
            }

            public WebsocketConnection(IWebSocketContext wsContext)
            {
                this.wsContext = wsContext;
            }
        }

        public List<WebsocketConnection> Websockets { get; private set; } = new List<WebsocketConnection>();

        private static readonly LinkedList<FoxWebSession> sessions = new LinkedList<FoxWebSession>();
        private static readonly object sessionLock = new object();

        public class QueueSubscription
        {
            public string Channel { get; set; } = default!;
            public JsonObject? Filters { get; set; } = null;
        }

        

        public FoxWebSession(string sessionId)
        {
            Id = sessionId;
            AddSession(this);
        }

        ~FoxWebSession()
        {
            RemoveSession(this);
        }

        private static void AddSession(FoxWebSession session)
        {
            lock (sessionLock)
            {
                if (!sessions.Any(s => s.Id == session.Id))
                {
                    sessions.AddLast(session);
                }
                else
                {
                    throw new InvalidOperationException($"Session with ID {session.Id} already exists.");
                }
            }
        }

        private static void RemoveSession(FoxWebSession session)
        {
            lock (sessionLock)
            {
                var node = sessions.FirstOrDefault(s => s.Id == session.Id);
                if (node != null)
                {
                    sessions.Remove(node);
                }
            }
        }

        private static FoxWebSession? FindSessionById(string sessionId)
        {
            lock (sessionLock)
            {
                return sessions.FirstOrDefault(s => s.Id == sessionId);
            }
        }

        public static FoxWebSession? FindSessionByContext(IWebSocketContext wsContext)
        {
            lock (sessionLock)
            {
                return sessions.FirstOrDefault(s => s.Websockets.Any(w => w.wsContext == wsContext));
            }
        }

        public static FoxWebSession? FindSessionByContext(IHttpContext httpContext)
        {
            lock (sessionLock)
            {
                return sessions.FirstOrDefault(s => s.HttpContexts.Contains(httpContext));
            }
        }

        public static async Task<FoxWebSession?> LoadFromSessionId(string sessionId)
        {
            var session = FindSessionById(sessionId); // Attempt to load from cache first

            if (session is not null)
                return session;

            // Not in memory, load from DB
            session = new FoxWebSession(sessionId);

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
                            if (r["uid"] is not DBNull)
                                session.user = await FoxUser.GetByUID(r.GetInt64("uid"));
                        }
                        else
                            return null;
                    }
                }
            }

            return session;
        }

        public static async Task<FoxWebSession?> LoadFromSessionId(IHttpContext context, string sessionId)
        {
            var session = await LoadFromSessionId(sessionId);

            if (session is null)
                return null;

            if (!session.HttpContexts.Contains(context))
                session.HttpContexts.Add(context);

                return session;
        }

        public static async Task<FoxWebSession?> LoadFromSessionId(IWebSocketContext context, string sessionId)
        {
            var session = await LoadFromSessionId(sessionId);

            if (session is null)
                return null;

            if (!session.Websockets.Any(w => w.wsContext == context))
                session.Websockets.Add(new WebsocketConnection { wsContext = context });

            return session;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "CS1998:Async method lacks 'await'", Justification = "Designed for potential asynchronous extension.")]
        public static async Task<FoxWebSession?> CreateNewSession()
        {
            var session = new FoxWebSession(GenerateSessionId());

            // We don't need to bother saving it to the database just yet, wait until it has some useful data in it.

            //try
            //{
            //    using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            //    {
            //        await SQL.OpenAsync();

            //        using (var cmd = new MySqlCommand("INSERT INTO sessions (uid, session_id) VALUES (@uid, @session_id)", SQL))
            //        {
            //            cmd.Parameters.AddWithValue("@uid", session.user is null ? null : session.user.UID);
            //            cmd.Parameters.AddWithValue("@session_id", session.Id);

            //            await cmd.ExecuteNonQueryAsync();
            //        }
            //    }

            //    return session;
            //}
            //catch (Exception ex)
            //{
            //    throw;
            //}

            return session;
        }

        public async Task<bool> Login(string username, string password)
        {
            if (this.user is not null)
                throw new InvalidOperationException("Session already logged in.");

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"SELECT uid, username
                                        FROM user_auth
                                        WHERE username = @username
                                        AND password_hash = SHA2(CONCAT(@password, salt), 256);";

                    cmd.Parameters.AddWithValue("username", username);
                    cmd.Parameters.AddWithValue("password", password);
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            long UID = r.GetInt64("uid");

                            FoxUser? fUser = await FoxUser.GetByUID(UID);

                            if (fUser is null)
                                throw new Exception("Failed to load user from database.");

                            this.user = fUser;

                            await this.Save();

                            return true;
                        }
                    }
                }
            }

            return false; // False indicates failure to log in.
        }
        public static async Task<FoxWebSession?> LoadFromContext(IWebSocketContext context, string? sessionId = null, bool createNew = true)
        {
            var session = FoxWebSession.FindSessionByContext(context);

            if (session is not null)
            {
                if (sessionId is not null && session.Id.ToLower() != sessionId.ToLower())
                {
                    throw new InvalidOperationException("Attempting to change session ID mid-connection.");
                }

                return session;
            }

            if (sessionId is null)
            {
                // Proceed to check the cookie and get user from the session if not found in active connections
                string? cookieHeader = context.Headers.GetValues("Cookie")?.FirstOrDefault();

                if (cookieHeader is not null)
                {
                    var cookies = cookieHeader.Split(';')
                        .Select(cookie => cookie.Split(new[] { '=' }, 2)) // Use 2 to ensure it only splits on the first '='
                        .Where(parts => parts.Length == 2)
                        .Select(parts => new { Name = parts[0].Trim(), Value = parts[1].Trim() })
                        .GroupBy(cookie => cookie.Name)
                        .ToDictionary(group => group.Key, group => group.Last().Value); // Get the last occurrence of each cookie name

                    cookies.TryGetValue("PHPSESSID", out sessionId);
                }
            }

            if (sessionId is not null)
                session = await FoxWebSession.LoadFromSessionId(sessionId);

            // If we still can't find a session, make a new one.
            if (createNew && session is null)
                session = await FoxWebSession.CreateNewSession();

            if (session is not null && !session.Websockets.Any(w => w.wsContext == context))
            {
                session.Websockets.Add(new WebsocketConnection(context));
            }

            return session;
        }

        public static List<(WebsocketConnection, FoxWebSession)> GetActiveWebSocketSessions()
        {
            lock (sessionLock)
            {
                var activeSessions = new List<(WebsocketConnection, FoxWebSession)>();
                foreach (var session in sessions)
                {
                    foreach (var webSocket in session.Websockets)
                    {
                        activeSessions.Add((webSocket, session));
                    }
                }
                return activeSessions;
            }
        }

        public static List<FoxWebSession> RemoveByContext(IWebSocketContext wsContext)
        {
            var removedSessions = new List<FoxWebSession>();

            lock (sessionLock)
            {
                foreach (var session in sessions.ToList())
                {
                    if (session.Websockets.Any(w => w.wsContext == wsContext))
                    {
                        session.Websockets.RemoveAll(w => w.wsContext == wsContext);

                        // If no contexts remain, remove the session
                        if (!session.HttpContexts.Any() && !session.Websockets.Any())
                        {
                            sessions.Remove(session);
                        }

                        // Add to removed sessions list
                        if (!removedSessions.Contains(session))
                        {
                            removedSessions.Add(session);
                        }
                    }
                }
            }

            // Run Save for each removed session
            foreach (var session in removedSessions)
            {
                session.Save().Wait();
            }

            return removedSessions;
        }


        public async Task Save()
        {
            try
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand("INSERT INTO sessions (session_id, uid, date_accessed) VALUES (@session_id, @uid, @date_accessed) ON DUPLICATE KEY UPDATE uid = VALUES(uid), date_accessed = VALUES(date_accessed);", SQL))
                    {
                        cmd.Parameters.AddWithValue("@uid", this.user?.UID ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@session_id", this.Id);
                        cmd.Parameters.AddWithValue("@date_accessed", DateTime.Now);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"SQL error while saving session id {this.Id}: {ex.Message}\r\n{ex.StackTrace}", LogLevel.ERROR);
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

        public void End()
        {
            lock (sessionLock)
            {
                HttpContexts.Clear();
                Websockets.Clear();
                var node = sessions.FirstOrDefault(s => s.Id == this.Id);

                if (node is not null)
                    sessions.Remove(node);
            }
        }
    }
}
