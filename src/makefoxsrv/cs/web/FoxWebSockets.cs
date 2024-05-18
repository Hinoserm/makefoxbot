using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using EmbedIO.WebSockets;
using MySqlConnector;
using makefoxsrv;
using static System.Net.Mime.MediaTypeNames;
using EmbedIO.Actions;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using TL;
using EmbedIO.Files;
using EmbedIO.Net;
using Terminal.Gui;
using System.Data;
using WTelegram;
using Swan;
using Swan.Formatters;
using System.Reflection.PortableExecutable;

public static class JsonElementExtensions
{
    public static bool TryGetInt64(this JsonElement element, out long value)
    {
        try
        {
            value = element.GetInt64();
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }
}

class FoxWebSockets {

    private static readonly ConcurrentDictionary<IWebSocketContext, FoxUser?> ActiveConnections = new ConcurrentDictionary<IWebSocketContext, FoxUser?>();

    public class Handler : WebSocketModule
    {
        public Handler(string urlPath)
            : base(urlPath, true)
        {
        }

        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            var user = await GetUserFromRequest(context);

            var message = System.Text.Encoding.UTF8.GetString(buffer);
            Console.WriteLine("Received: " + message);

            System.Text.Json.Nodes.JsonObject jsonMessage = JsonNode.Parse(message).AsObject();

            //var jsonMessage = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
            if (jsonMessage != null && jsonMessage.ContainsKey("Command"))
            {
                var command = jsonMessage["Command"].ToString();

                switch (command)
                {
                    case "Ping":
                        {
                            var response = new { Command = "Pong" };
                            var responseMessage = JsonSerializer.Serialize(response);

                            await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(responseMessage), true);
                            break;
                        }
                    case "Autocomplete":
                        {
                            var query = jsonMessage["Query"].ToString();

                            if (String.IsNullOrEmpty(query))
                                return; //No point wasting our time if the query is empty

                            if (user is null || !user.CheckAccessLevel(AccessLevel.ADMIN))
                                return; //Only admins can use this feature

                            var suggestions = await FoxUser.GetSuggestions(query);
                            var response = new AutocompleteResponse
                            {
                                Command = "AutocompleteResponse",
                                Suggestions = suggestions.Select(s => new AutocompleteSuggestion { Display = s.Display, Paste = s.Paste }).ToList()
                            };

                            var responseMessage = JsonSerializer.Serialize(response);
                            await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(responseMessage), true);
                            break;
                        }
                    case "NewChat":
                        {
                            //long? tgPeerId = (long?)jsonMessage["tgPeerId"];

                            if (user is null)
                            {
                                await SendErrorResponse(context, "You must be logged in to use this feature.");
                                return;
                            }

                            if (!user.CheckAccessLevel(AccessLevel.ADMIN))
                            {
                                await SendErrorResponse(context, "Only admins can use this feature.");
                                return; //Only admins can use this feature
                            }

                            var username = jsonMessage["Username"].ToString();

                            if (username is null)
                            {
                                await SendErrorResponse(context, "Missing non-optional field (Username).");
                                return; //No point wasting our time, user is not an optional field.
                            }

                            var chatUser = await FoxUser.ParseUser(username);
                            
                            if (chatUser is null || chatUser.TelegramID is null)
                            {
                                await SendErrorResponse(context, "User not found or Telegram ID not set.");
                                return;
                            }

                            await SaveChat(context, user, chatUser, null);
                            await SendChatList(context, user);

                            break;
                        }
                    case "GetChatList":
                        {
                            if (user is null)
                            {
                                await SendErrorResponse(context, "You must be logged in to use this feature.");
                                return;
                            }

                            await SendChatList(context, user);

                            break;
                        }
                    case "DeleteChat":
                        {
                            if (user is null)
                            {
                                await SendErrorResponse(context, "You must be logged in to use this feature.");
                                return;
                            }

                            long? chatId = (long?)jsonMessage["ChatID"];

                            if (chatId is null)
                            {
                                await SendErrorResponse(context, "Missing non-optional field (ChatID).");
                                return;
                            }

                            await DeleteChat(context, user, chatId.Value);

                            break;
                        }

                    case "SendChatMessage":
                        {
                            if (user is null)
                            {
                                await SendErrorResponse(context, "You must be logged in to use this feature.");
                                return;
                            }

                            long? chatId = (long?)jsonMessage["ChatID"];

                            if (chatId is null)
                            {
                                await SendErrorResponse(context, "Missing non-optional field (ChatID).");
                                return;
                            }

                            var toUser = await GetUserFromChatId(context, user, chatId.Value);

                            if (toUser is null)
                            {
                                await SendErrorResponse(context, "Chat not found or user not found.");
                                return;
                            }

                            await SendChatMessage(context, user, toUser, null, jsonMessage["Message"].ToString());

                            break;
                        }
                    case "GetChatMessages":
                        {
                            if (user is null)
                            {
                                await SendErrorResponse(context, "You must be logged in to use this feature.");
                                return;
                            }

                            long? chatId = (long?)jsonMessage["ChatID"];

                            var toUser = await GetUserFromChatId(context, user, chatId.Value);
                            
                            if (toUser is null)
                            {
                                await SendErrorResponse(context, "Chat not found or user not found.");
                                return;
                            }

                            await GetChatMessages(context, user, toUser, null);

                            break;
                        }
                }
            }
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            var user = await GetUserFromRequest(context);
            ActiveConnections.TryAdd(context, user);
            Console.WriteLine($"WebSocket Connected. User: {(user?.Username ?? "(none)")}");
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            ActiveConnections.TryRemove(context, out _);
            Console.WriteLine("WebSocket disconnected!");
            return Task.CompletedTask;
        }
    }

    //This function retrieves the user associated with a chat tab
    private static async Task<FoxUser?> GetUserFromChatId(IWebSocketContext context, FoxUser user, long chatId)
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
    private static async Task SendChatMessage(IWebSocketContext context, FoxUser fromUser, FoxUser toUser, long? tgPeerId, string message)
    {
        var teleUser = toUser.TelegramID is not null ? await FoxTelegram.GetUserFromID(toUser.TelegramID.Value) : null;
        var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

        if (t is null)
        {
            await SendErrorResponse(context, "Failed to create telegram object.");
            return;
        }

        try
        {
            await t.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            await SendErrorResponse(context, $"Failed to send message to user on Telegram.  Error: {ex.Message}");
            return;
        }

        await BroadcastChatMessage(context, fromUser, toUser, tgPeerId, message);

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
    private static async Task GetChatMessages(IWebSocketContext context, FoxUser fromUser, FoxUser toUser, long? tgPeerId)
    {
        using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
        {
            await SQL.OpenAsync();

            var messageList = new List<Message>();

            using (var cmd = new MySqlCommand())
            {
                cmd.Connection = SQL;
                cmd.CommandText = "SELECT * FROM admin_chats WHERE from_uid = @fromUID AND to_uid = @toUID AND tg_peer_id IS NULL";
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

            var responseMessage = JsonSerializer.Serialize(response);
            await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(responseMessage), true);
        }
    }


    //This function broadcasts chat messages to all relevant clients
    private static async Task BroadcastChatMessage(IWebSocketContext context, FoxUser fromUser, FoxUser toUser, long? tgPeerId, string message)
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

        foreach (var kvp in ActiveConnections)
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

    //This function deletes a chat tab from the database
    private static async Task DeleteChat(IWebSocketContext context, FoxUser user, long chatId)
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
    private static async Task SaveChat(IWebSocketContext context, FoxUser fromUser, FoxUser toUser, long? tgPeerId)
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
    private static async Task SendChatList(IWebSocketContext context, FoxUser user)
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

                    var responseMessage = JsonSerializer.Serialize(response);
                    await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(responseMessage), true);
                }
            }
        }
    }

    public static async Task<FoxUser?> GetUserFromRequest(IWebSocketContext context)
    {
        // Check if the user is already in the active connections
        if (ActiveConnections.TryGetValue(context, out var cachedUser))
        {
            return cachedUser;
        }

        // Proceed to check the cookie and get user from the session if not found in active connections
        string? cookieHeader = context.Headers.GetValues("Cookie")?.FirstOrDefault();

        if (cookieHeader is not null)
        {
            var cookies = cookieHeader.Split(';')
                .Select(cookie => cookie.Split('='))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

            if (cookies.TryGetValue("PHPSESSID", out var sessionId))
            {
                var user = await FoxWebSessions.GetUserFromSession(sessionId);

                // Cache the user in ActiveConnections if not already cached
                if (user != null)
                {
                    ActiveConnections[context] = user;
                }

                return user;
            }
        }

        return null;
    }

    public static async Task SendErrorResponse(IWebSocketContext context, string errorMessage)
    {
        var msg = new
        {
            Error = new
            {
                Command = "Error",
                Message = errorMessage
            }
        };

        string jsonMessage = JsonSerializer.Serialize(msg);

        await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
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

        foreach (var kvp in ActiveConnections)
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

    public class AutocompleteResponse
    {
        public string Command { get; set; }
        public List<AutocompleteSuggestion> Suggestions { get; set; }
    }

    public class AutocompleteSuggestion
    {
        public string Display { get; set; }
        public string Paste { get; set; }
    }
}