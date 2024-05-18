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

namespace makefoxsrv
{
    public class FoxWebChat {
        private class Message
        {
            public ulong TabID { get; set; }
            public ulong? FromUID { get; set; }
            public ulong? ToUID { get; set; }
            public long? TgPeer { get; set; }
            public string MessageText { get; set; } = "";
            public DateTime Date { get; set; }
            public string Username { get; set; } = "";
            public bool isOutgoing { get; set; } = false;
        }

        // This function retrieves the last x messages from a chat and sends them to the client
        public static async Task<object> GetChatMessages(FoxUser fromUser, FoxUser toUser, long? tgPeerId)
        {
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
                    cmd.Parameters.AddWithValue("peerid", tgPeerId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var user = await FoxUser.GetByUID(reader.GetInt64("from_uid"));

                            var username = user?.Username ?? ($"#{reader.GetUInt64("from_uid")}");

                            var message = new Message
                            {
                                FromUID = reader.GetUInt64("from_uid"),
                                ToUID = reader.GetUInt64("to_uid"),
                                TgPeer = reader.IsDBNull("tg_peer_id") ? (long?)null : reader.GetInt64("tg_peer_id"),
                                MessageText = reader.GetString("message"),
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
                    cmd.CommandText = "SELECT * FROM telegram_log WHERE user_id = @toTGID AND chat_id IS NULL ORDER BY date_added DESC LIMIT 100";
                    cmd.Parameters.AddWithValue("toTGID", toUser.TelegramID);
                    cmd.Parameters.AddWithValue("peerid", tgPeerId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var message = new Message
                            {
                                FromUID = toUser.UID,
                                ToUID = null,
                                TgPeer = reader.IsDBNull("chat_id") ? (long?)null : reader.GetInt64("chat_id"),
                                MessageText = reader.GetString("message_text"),
                                Username = toUser.Username ?? ($"#{toUser.TelegramID}"),
                                Date = reader.GetDateTime("date_added")
                            };

                            messageList.Add(message);
                        }
                    }
                }

                // Sort the messages by date
                messageList.Sort((a, b) => a.Date.CompareTo(b.Date));

                var response = new
                {
                    Command = "GetChatMessages",
                    Chats = messageList
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
                            var userId = reader.GetInt64("to_uid");

                            return await FoxUser.GetByUID(userId);
                        }
                    }
                }
            }

            return null;
        }

        //This function sends a chat message to a user on telegram and stores it in the database
        public static async Task SendChatMessage(FoxUser fromUser, FoxUser toUser, long? tgPeerId, string message)
        {
            var teleUser = toUser.TelegramID is not null ? await FoxTelegram.GetUserFromID(toUser.TelegramID.Value) : null;
            var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

            if (t is null)
                throw new Exception("Failed to create telegram object.");

            await t.SendMessageAsync(message);
 
            await BroadcastChatMessage(fromUser, toUser, tgPeerId, message);

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO admin_chats (from_uid, to_uid, tg_peer_id, message, message_date) VALUES (@fromUID, @toUID, @peer, @message, @now)";
                    cmd.Parameters.AddWithValue("fromUID", fromUser.UID);
                    cmd.Parameters.AddWithValue("toUID", toUser.UID);
                    cmd.Parameters.AddWithValue("peer", tgPeerId); //Allowed to be null if the chat is user-to-user.
                    cmd.Parameters.AddWithValue("message", message);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        //This function deletes a chat tab from the database
        public static async Task DeleteChat(FoxUser user, long chatId)
        {
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "DELETE FROM admin_open_chats WHERE from_uid = @uid AND id = @chatId";
                    cmd.Parameters.AddWithValue("uid", user.UID);
                    cmd.Parameters.AddWithValue("chatId", chatId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        //This function saves the chat tab to the database
        public static async Task SaveChat(FoxUser fromUser, FoxUser toUser, long? tgPeerId)
        {
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "INSERT INTO admin_open_chats (from_uid, to_uid, tg_peer_id, date_opened) VALUES (@uid, @toUID, @tgPeerId, @now)";
                    cmd.Parameters.AddWithValue("uid", fromUser.UID);
                    cmd.Parameters.AddWithValue("toUID", toUser.UID);
                    cmd.Parameters.AddWithValue("tgPeerId", tgPeerId); //Allowed to be null if the chat is user-to-user.
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        //This function sends the current list of open chats back to the client
        public static async Task<object> GetChatList(FoxUser user)
        {
            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "SELECT * FROM admin_open_chats WHERE from_uid = @uid";
                    cmd.Parameters.AddWithValue("uid", user.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        var chatTabs = new List<object>();

                        while (await reader.ReadAsync())
                        {
                            var chatTab = new
                            {
                                TabID = reader.GetUInt64("id"),
                                toUser = reader.GetInt64("to_uid"),
                                tgPeerId = reader.IsDBNull("tg_peer_id") ? (long?)null : reader.GetInt64("tg_peer_id"),
                                dateOpened = reader.GetDateTime("date_opened")
                            };

                            chatTabs.Add(chatTab);
                        }

                        var response = new
                        {
                            Command = "ListChatTabs",
                            Chats = chatTabs
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

            var msg = new Message
            {
                ToUID = toUser.UID,
                FromUID = fromUser.UID,
                TgPeer = tgPeerId,
                MessageText = message,
                Date = DateTime.Now,
                Username = username,
                isOutgoing = true
            };

            var response = new
            {
                Command = "ChatMessage",
                Content = msg
            };

            string jsonMessage = JsonSerializer.Serialize(response);

            foreach (var kvp in FoxWebSockets.ActiveConnections)
            {
                var outContext = kvp.Key;
                var connectedUser = kvp.Value; // Get the user associated with the context (can be null)

                // You can add your logic here using the connectedUser
                if (connectedUser != null && connectedUser.CheckAccessLevel(AccessLevel.ADMIN))
                {
                    await outContext.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
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

            var msg = new Message
            {
                ToUID = chatUser.UID,
                FromUID = null,
                TgPeer = tgPeer?.ID,
                MessageText = message,
                Date = DateTime.Now,
                Username = username,
                isOutgoing = false
            };

            var response = new
            {
                Command = "ChatMessage",
                Content = msg
            };

            string jsonMessage = JsonSerializer.Serialize(response);

            foreach (var kvp in FoxWebSockets.ActiveConnections)
            {
                var context = kvp.Key;
                var connectedUser = kvp.Value; // Get the user associated with the context (can be null)

                // You can add your logic here using the connectedUser
                if (connectedUser != null && (connectedUser.TelegramID == tgUser?.UserId || connectedUser.CheckAccessLevel(AccessLevel.ADMIN)))
                {
                    await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
                }
            }
        }
    }
}
