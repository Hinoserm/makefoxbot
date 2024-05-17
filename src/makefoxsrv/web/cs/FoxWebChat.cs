using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using Microsoft.AspNetCore.SignalR;

namespace makefoxsrv
{
    public static class DatabaseHandler
    {
        private static IHubContext<ChatHub> _hubContext;

        public static void Initialize(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // Method to fetch existing open conversations
        public static async Task<List<ChatInstance>> GetOpenChatsAsync()
        {
            var chatInstances = new List<ChatInstance>();

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                // Fetch open chats
                var command = new MySqlCommand("SELECT user_uid, csr_uid, telegram_id FROM support_open_chats", connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var userUid = reader.GetInt64("user_uid");
                        var csrUid = reader.GetInt64("csr_uid");
                        var telegramID = reader.GetInt64("telegram_id");

                        var user = await FoxUser.GetByUID(userUid);
                        var csrUser = await FoxUser.GetByUID(csrUid);

                        var chatInstance = new ChatInstance
                        {
                            User = user,
                            Messages = new List<ChatMessage>()
                        };

                        // Fetch messages from telegram_log and support_chats
                        var telegramMessagesTask = FetchMessagesFromTelegramLog(telegramID);
                        var supportMessagesTask = FetchMessagesFromSupportChats(userUid);

                        await Task.WhenAll(telegramMessagesTask, supportMessagesTask);

                        // Combine and sort messages by date
                        chatInstance.Messages = telegramMessagesTask.Result.Concat(supportMessagesTask.Result)
                                                                       .OrderBy(m => m.Date)
                                                                       .ToList();

                        chatInstances.Add(chatInstance);
                    }
                }
            }

            return chatInstances;
        }

        public static async Task<List<ChatMessage>> FetchMessagesFromTelegramLog(long telegramID)
        {
            var messages = new List<ChatMessage>();

            using (var messagesConnection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await messagesConnection.OpenAsync();
                var messagesCommand = new MySqlCommand("SELECT message_text, date_added FROM telegram_log WHERE user_id = @user_id ORDER BY date_added DESC LIMIT 100", messagesConnection);
                messagesCommand.Parameters.AddWithValue("@user_id", telegramID);

                using (var messagesReader = await messagesCommand.ExecuteReaderAsync())
                {
                    while (await messagesReader.ReadAsync())
                    {
                        var messageText = messagesReader.GetString("message_text");
                        var messageDate = messagesReader.GetDateTime("date_added");

                        messages.Add(new ChatMessage
                        {
                            User = null, // The user for telegram messages can be set here if necessary
                            Text = $"<p>{messageText}</p>",
                            Date = messageDate,
                            Source = "telegram_log"
                        });
                    }
                }
            }

            return messages;
        }

        public static async Task<List<ChatMessage>> FetchMessagesFromSupportChats(long userUid)
        {
            var messages = new List<ChatMessage>();

            using (var messagesConnection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await messagesConnection.OpenAsync();
                var messagesCommand = new MySqlCommand("SELECT csr_uid, message, message_date FROM support_chats WHERE user_uid = @user_uid ORDER BY message_date DESC LIMIT 100", messagesConnection);
                messagesCommand.Parameters.AddWithValue("@user_uid", userUid);

                using (var messagesReader = await messagesCommand.ExecuteReaderAsync())
                {
                    while (await messagesReader.ReadAsync())
                    {
                        var csrUid = messagesReader.GetInt64("csr_uid");
                        var messageText = messagesReader.GetString("message");
                        var messageDate = messagesReader.GetDateTime("message_date");

                        var csrUser = await FoxUser.GetByUID(csrUid);

                        messages.Add(new ChatMessage
                        {
                            User = csrUser,
                            Text = $"<p>{messageText}</p>",
                            Date = messageDate,
                            Source = "support_chats"
                        });
                    }
                }
            }

            return messages;
        }

        // Method to save a message to the database
        public static async Task SaveMessageAsync(FoxUser user, FoxUser csrUser, long telegramPeerId, string message)
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                var command = new MySqlCommand("INSERT INTO support_chats (user_uid, csr_uid, telegram_peer_id, message, message_date) VALUES (@user_uid, @csr_uid, @telegram_peer_id, @message, @message_date)", connection);

                command.Parameters.AddWithValue("@user_uid", user.UID);
                command.Parameters.AddWithValue("@csr_uid", csrUser.UID);
                command.Parameters.AddWithValue("@telegram_peer_id", telegramPeerId);
                command.Parameters.AddWithValue("@message", message);
                command.Parameters.AddWithValue("@message_date", DateTime.Now);

                await command.ExecuteNonQueryAsync();
            }
        }

        // Method to add a new open chat
        public static async Task AddOpenChatAsync(FoxUser user, long telegramID)
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                var command = new MySqlCommand("INSERT INTO support_open_chats (user_uid, csr_uid, telegram_id) VALUES (@user_uid, @csr_uid, @telegram_id)", connection);

                command.Parameters.AddWithValue("@user_uid", user.UID);
                command.Parameters.AddWithValue("@csr_uid", 1); // For demo purposes
                command.Parameters.AddWithValue("@telegram_id", telegramID);

                await command.ExecuteNonQueryAsync();
            }
        }

        // Method to remove an open chat
        public static async Task RemoveOpenChatAsync(FoxUser user)
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                var command = new MySqlCommand("DELETE FROM support_open_chats WHERE user_uid = @user_uid AND csr_uid = @csr_uid", connection);

                command.Parameters.AddWithValue("@user_uid", user.UID);
                command.Parameters.AddWithValue("@csr_uid", 1); // For demo purposes

                await command.ExecuteNonQueryAsync();
            }
        }

        // Method to display received Telegram message
        public static async Task DisplayReceivedTelegramMessage(long telegramID, string messageText)
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                var command = new MySqlCommand("SELECT user_uid FROM support_open_chats WHERE telegram_id = @telegramID", connection);
                command.Parameters.AddWithValue("@telegramID", telegramID);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var userUid = reader.GetInt64("user_uid");
                        var user = await FoxUser.GetByUID(userUid);

                        var chatMessage = new ChatMessage
                        {
                            User = user,
                            Text = $"<p>{messageText}</p>",
                            Date = DateTime.Now,
                            Source = "telegram_log"
                        };

                        if (_hubContext != null)
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveTelegramMessage", user.Username, chatMessage.Text, chatMessage.Date.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                    }
                }
            }
        }
    }

    public class ChatInstance
    {
        public FoxUser? User { get; set; }
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatMessage
    {
        public FoxUser? User { get; set; }
        public string Text { get; set; }
        public DateTime Date { get; set; }
        public string Source { get; set; } // New property to identify the source table
    }

    public class ChatHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public async Task ReceiveTelegramMessage(string user, string message, string date)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message, date);
        }
    }
}
