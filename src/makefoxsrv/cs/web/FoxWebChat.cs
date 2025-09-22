using EmbedIO;
using EmbedIO.WebSockets;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TL;
using System.Text.Json.Nodes;

using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using System.CodeDom;

namespace makefoxsrv
{
    public class FoxWebChat {
        private class Message
        {
            public ulong? FromUID { get; set; }
            public ulong? ToUID { get; set; }
            public long? TgPeer { get; set; }
            public string Text { get; set; } = "";
            public DateTime Date { get; set; }
            public string? Username { get; set; } = null;
            public bool isOutgoing { get; set; } = false;
        }

        // This function retrieves the last x messages from a chat and sends them to the client
        [WebFunctionName("GetMessages")]    // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(true)]            // User must be logged in to use this function
        [WebAccessLevel(AccessLevel.ADMIN)] // Minimum access level required to use this function
        public static async Task<JsonObject?> GetMessages(FoxWebContext context, JsonObject jsonMessage)
        {
            var session = context.session;

            long chatId = FoxJsonHelper.GetLong(jsonMessage, "ChatID", false)!.Value;

            int msgCount = FoxJsonHelper.GetInt(jsonMessage, "Count", true) ?? 30;

            if (session?.user is null)
                throw new Exception("User not logged in.");

            FoxUser fromUser = session.user;

            if (msgCount < 1)
                throw new Exception("Invalid message count.");

            var toUser = await FoxWebChat.GetUserFromChatId(fromUser, chatId);

            if (toUser is null)
                throw new Exception("Failed to get user from chat ID.");

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                var messageList = new List<Message>();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT * FROM admin_chats WHERE to_uid = @toUID AND tg_peer_id IS NULL";
                    cmd.Parameters.AddWithValue("fromUID", fromUser.UID);
                    cmd.Parameters.AddWithValue("toUID", toUser.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var user = await FoxUser.GetByUID(reader.GetUInt64("from_uid"));

                            var username = user?.Username ?? ($"#{reader.GetUInt64("from_uid")}");

                            var message = new Message
                            {
                                FromUID = reader.GetUInt64("from_uid"),
                                ToUID = reader.GetUInt64("to_uid"),
                                TgPeer = reader.IsDBNull("tg_peer_id") ? (long?)null : reader.GetInt64("tg_peer_id"),
                                Text = reader.GetString("message"),
                                Username = username,
                                Date = reader.GetDateTime("message_date"),
                                isOutgoing = true
                            };

                            messageList.Add(message);
                        }
                    }
                }

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT * FROM telegram_log WHERE user_id = @toTGID AND chat_id IS NULL ORDER BY date_added DESC LIMIT " + msgCount;
                    cmd.Parameters.AddWithValue("toTGID", toUser.TelegramID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var message = new Message
                            {
                                FromUID = toUser.UID,
                                ToUID = null,
                                TgPeer = reader.IsDBNull("chat_id") ? (long?)null : reader.GetInt64("chat_id"),
                                Text = reader.GetString("message_text"),
                                Username = toUser.Username ?? ($"#{toUser.TelegramID}"),
                                Date = reader.GetDateTime("date_added")
                            };

                            messageList.Add(message);
                        }
                    }
                }

                // Sort the messages by date
                messageList.Sort((a, b) => a.Date.CompareTo(b.Date));

                var jsonArray = JsonSerializer.Deserialize<JsonArray>(JsonSerializer.Serialize(messageList));

                var response = new JsonObject
                {
                    ["Command"] = "Chat:GetMessages",
                    ["Success"] = true,
                    ["ChatID"] = chatId,
                    ["Messages"] = jsonArray
                };

                return response;
            }
        }

        //This function retrieves the user associated with a chat tab
        public static async Task<FoxUser?> GetUserFromChatId(FoxUser user, long chatId)
        {
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT to_uid FROM admin_open_chats WHERE from_uid = @uid AND id = @chatId";
                    cmd.Parameters.AddWithValue("uid", user.UID);
                    cmd.Parameters.AddWithValue("chatId", chatId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var userId = reader.GetUInt64("to_uid");

                            return await FoxUser.GetByUID(userId);
                        }
                    }
                }
            }

            return null;
        }

        //This function sends a chat message to a user on telegram and stores it in the database
        [WebFunctionName("SendMessage")]    // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(true)]            // User must be logged in to use this function
        [WebAccessLevel(AccessLevel.ADMIN)] // Minimum access level required to use this function
        public static async Task<JsonObject?> SendMessage(FoxWebContext context, JsonObject jsonMessage)
        {
            var session = context.session;


            long chatId = FoxJsonHelper.GetLong(jsonMessage, "ChatID", false).Value;
            string message = FoxJsonHelper.GetString(jsonMessage, "Message", false);

            var fromUser = session.user;

            var toUser = await FoxWebChat.GetUserFromChatId(fromUser, chatId);

            if (toUser is null)
                throw new Exception("Chat or user not found.");

            var teleUser = toUser.TelegramID is not null ? await FoxTelegram.GetUserFromID(toUser.TelegramID.Value) : null;
            var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

            if (t is null)
                throw new Exception("Failed to create telegram object.");

            await t.SendMessageAsync(message);
 
            await BroadcastChatMessage(fromUser, toUser, null, message);

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO admin_chats (from_uid, to_uid, tg_peer_id, message, message_date) VALUES (@fromUID, @toUID, @peer, @message, @now)";
                    cmd.Parameters.AddWithValue("fromUID", fromUser.UID);
                    cmd.Parameters.AddWithValue("toUID", toUser.UID);
                    cmd.Parameters.AddWithValue("peer", null); //Allowed to be null if the chat is user-to-user.
                    cmd.Parameters.AddWithValue("message", message);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            var response = new JsonObject
            {
                ["Command"] = "Chat:SendMessage",
                ["Success"] = true
            };

            return response;

        }

        //This function deletes a chat tab from the database
        [WebFunctionName("Delete")]         // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(true)]            // User must be logged in to use this function
        [WebAccessLevel(AccessLevel.ADMIN)] // Minimum access level required to use this function
        public static async Task<JsonObject?> Delete(FoxWebContext context, JsonObject jsonMessage)
        {
            var session = context.session;
            var fromUser = session.user;

            long chatId = FoxJsonHelper.GetLong(jsonMessage, "ChatID", false).Value;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "DELETE FROM admin_open_chats WHERE from_uid = @uid AND id = @chatId";
                    cmd.Parameters.AddWithValue("uid", fromUser.UID);
                    cmd.Parameters.AddWithValue("chatId", chatId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            var response = new JsonObject
            {
                ["Command"] = "Chat:Delete",
                ["Success"] = true
            };

            return response;
        }

        //Returns the details of a specified ChatID
        [WebFunctionName("Get")]            // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(true)]            // User must be logged in to use this function
        [WebAccessLevel(AccessLevel.ADMIN)] // Minimum access level required to use this function
        public static async Task<JsonObject> Get(FoxWebContext context, JsonObject jsonMessage)
        {
            var session = context.session;

            long chatId = FoxJsonHelper.GetLong(jsonMessage, "ChatID", false)!.Value;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT * FROM admin_open_chats WHERE id = @chat_id LIMIT 1";
                    cmd.Parameters.AddWithValue("chat_id", chatId);
                    cmd.Parameters.AddWithValue("uid", session.user.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var fromUID = reader.GetUInt64("to_uid");

                            if (fromUID != session.user.UID)
                            { 
                                return new JsonObject
                                {
                                    ["Command"] = "Chat:List",
                                    ["Success"] = false,
                                    ["Error"] = $"ChatID '{chatId}' does not belong to current user '{session.user.UID}'."
                                };
                            }

                            var chatUser = await FoxUser.GetByUID(reader.GetUInt64("to_uid"));

                            var displayName = chatUser?.Username ?? ($"#{reader.GetUInt64("to_uid")}");

                            var chatResponse = new JsonObject
                            {
                                ["Command"] = "Chat:List",
                                ["Success"] = true,
                                ["ChatID"] = reader.GetUInt64("id"),
                                ["FromUID"] = fromUID,
                                ["ToUID"] = reader.GetInt64("to_uid"),
                                ["TgPeer"] = reader.IsDBNull("tg_peer_id") ? (long?)null : reader.GetInt64("tg_peer_id"),
                                ["DisplayName"] = displayName,
                                ["Date"] = reader.GetDateTime("date_opened")
                            };

                            return chatResponse;
                        }

                        var errorResponse = new JsonObject
                        {
                            ["Command"] = "Chat:List",
                            ["Success"] = false,
                            ["Error"] = $"ChatID '{chatId}' not found."
                        };

                        return errorResponse;
                    }
                }
            }
        }

        //This function saves the chat tab to the database
        [WebFunctionName("New")]    // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(true)]            // User must be logged in to use this function
        [WebAccessLevel(AccessLevel.ADMIN)] // Minimum access level required to use this function
        public static async Task<JsonObject?> New(FoxWebContext context, JsonObject jsonMessage)
        {
            var session = context.session;

            string username = FoxJsonHelper.GetString(jsonMessage, "Username", false)!;

            var fromUser = session.user;

            var chatUser = await FoxUser.ParseUser(username);

            if (chatUser is null || chatUser.TelegramID is null)
            {
                var response = new JsonObject
                {
                    ["Command"] = "Chat:New",
                    ["Success"] = false,
                    ["Error"] = $"User '{username}' not found."
                };

                return response;
            }

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO admin_open_chats (from_uid, to_uid, tg_peer_id, date_opened) VALUES (@uid, @toUID, @tgPeerId, @now)";
                    cmd.Parameters.AddWithValue("uid", fromUser.UID);
                    cmd.Parameters.AddWithValue("toUID", chatUser.UID);
                    cmd.Parameters.AddWithValue("tgPeerId", null); //Allowed to be null if the chat is user-to-user.
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();

                    var lastInsertedId = cmd.LastInsertedId;

                    if (lastInsertedId <= 0)
                        throw new Exception("Failed to create new chat.");

                    var response = new JsonObject
                    {
                        ["Command"] = "Chat:New",
                        ["Success"] = true,
                        ["ChatID"] = lastInsertedId
                    };

                    return response;
                }
            }
        }

        //This function sends the current list of open chats back to the client
        [WebFunctionName("List")]    // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(true)]            // User must be logged in to use this function
        [WebAccessLevel(AccessLevel.ADMIN)] // Minimum access level required to use this function
        public static async Task<JsonObject> List(FoxWebContext context, JsonObject jsonMessage)
        {
            var session = context.session;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT * FROM admin_open_chats WHERE from_uid = @uid ORDER BY date_opened";
                    cmd.Parameters.AddWithValue("uid", session.user.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        var chatTabs = new List<object>();

                        while (await reader.ReadAsync())
                        {
                            var chatUser = await FoxUser.GetByUID(reader.GetUInt64("to_uid"));

                            var displayName = chatUser?.Username ?? ($"#{reader.GetUInt64("to_uid")}");

                            var chatTab = new
                            {
                                ChatID = reader.GetUInt64("id"),
                                ToUID = reader.GetInt64("to_uid"),
                                TgPeer = reader.IsDBNull("tg_peer_id") ? (long?)null : reader.GetInt64("tg_peer_id"),
                                DisplayName = displayName,
                                Date = reader.GetDateTime("date_opened")
                            };

                            chatTabs.Add(chatTab);
                        }

                        var jsonArray = JsonSerializer.Deserialize<JsonArray>(JsonSerializer.Serialize(chatTabs));

                        var response = new JsonObject
                        {
                            ["Command"] = "Chat:List",
                            ["Success"] = true,
                            ["Chats"] = jsonArray
                        };

                        return response;
                    }
                }
            }
        }

        //This function broadcasts chat messages to all relevant clients
        public static async Task BroadcastChatMessage(FoxUser fromUser, FoxUser toUser, long? tgPeerId, string message)
        {
            var username = fromUser.Username;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT from_uid, id FROM admin_open_chats WHERE to_uid = @toUID AND tg_peer_id IS NULL ORDER BY date_opened";
                    cmd.Parameters.AddWithValue("toUID", toUser.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var chatId = reader.GetUInt64("id");

                            foreach (var (context, connectedSession) in FoxWebSession.GetActiveWebSocketSessions())
                            {
                                if (connectedSession.user is not null && (connectedSession.user.CheckAccessLevel(AccessLevel.ADMIN)))
                                {
                                    var msg = new Message
                                    {
                                        ToUID = toUser.UID,
                                        FromUID = fromUser.UID,
                                        TgPeer = tgPeerId,
                                        Text = message,
                                        Date = DateTime.Now,
                                        Username = username,
                                        isOutgoing = true
                                    };

                                    var response = new
                                    {
                                        Command = "Chat:NewMessage",
                                        ChatID = chatId, //Put the chatId here.
                                        Message = msg
                                    };

                                    string jsonMessage = JsonSerializer.Serialize(response);
                                    await context.wsContext.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
                                }
                            }
                        }

                    }
                }
            }
        }


        // Function to broadcast message to all WebSocket clients
        public static async Task BroadcastMessageAsync(FoxUser? user, TL.InputUser? tgUser, TL.InputPeer? tgPeer, string message)
        {
            if (tgUser is null || tgUser.UserId is null)
                return;

            if (tgPeer?.ID != tgUser?.UserId)
                return; //Not ready to handle group chats.

            var chatUser = await FoxUser.GetByTelegramUserID(tgUser.UserId.Value);

            if (chatUser is null)
                return;

            var username = chatUser.Username ?? ($"#{tgUser.UserId}");

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT from_uid, id FROM admin_open_chats WHERE to_uid = @toUID AND tg_peer_id IS NULL ORDER BY date_opened";
                    cmd.Parameters.AddWithValue("toUID", chatUser.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var fromUser = await FoxUser.GetByUID(reader.GetUInt64("from_uid"));
                            var chatId = reader.GetUInt64("id");

                            foreach (var (context, connectedSession) in FoxWebSession.GetActiveWebSocketSessions())
                            {
                                if (connectedSession.user is not null && (connectedSession.user.TelegramID == tgUser?.UserId || connectedSession.user.CheckAccessLevel(AccessLevel.ADMIN)))
                                {
                                    var msg = new Message
                                    {
                                        ToUID = chatUser.UID,
                                        FromUID = fromUser.UID,
                                        TgPeer = tgPeer?.ID,
                                        Text = message,
                                        Date = DateTime.Now,
                                        Username = username,
                                        isOutgoing = false
                                    };

                                    var response = new
                                    {
                                        Command = "Chat:NewMessage",
                                        ChatID = chatId, //Put the chatId here.
                                        Message = msg
                                    };

                                    string jsonMessage = JsonSerializer.Serialize(response);
                                    await context.wsContext.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
                                }
                            }
                        }
                    }
                }
            }
        }

    }
}
