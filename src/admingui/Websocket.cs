using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Websocket.Client;

public class WebSocketManager
{
    private static WebSocketManager _instance;
    private WebsocketClient _webSocketClient;
    private ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _responseTcs;
    public string? Username { get; private set; }
    public string? SessionID { get; private set; }

    public static WebSocketManager Instance => _instance ??= new WebSocketManager();

    public event EventHandler<JsonObject> MessageReceived;

    private WebSocketManager()
    {
        _responseTcs = new ConcurrentDictionary<string, TaskCompletionSource<JsonObject>>();
    }

    public async Task ConnectAsync(string url)
    {
        var uri = new Uri(url);
        _webSocketClient = new WebsocketClient(uri);

        _webSocketClient.MessageReceived.Subscribe(msg => HandleMessage(msg.Text));
        _webSocketClient.DisconnectionHappened.Subscribe(info =>
        {
            Console.WriteLine($"Disconnection happened, type: {info.Type}, reason: {info.CloseStatusDescription}");
        });

        await _webSocketClient.Start();
    }

    public async Task<JsonObject> SendRequestAsync(JsonObject request)
    {
        var command = request["Command"]?.ToString();
        if (string.IsNullOrEmpty(command))
            throw new ArgumentException("Request must contain a Command.");

        var tcs = new TaskCompletionSource<JsonObject>();
        _responseTcs[command] = tcs;

        string jsonRequest = request.ToJsonString();
        Console.WriteLine($"Sending request: {jsonRequest}");
        await _webSocketClient.SendInstant(jsonRequest);
        return await tcs.Task; // Wait for the response
    }

    private void HandleMessage(string message)
    {
        Console.WriteLine($"Handling message: {message}");
        var response = JsonNode.Parse(message)?.AsObject();

        if (response != null)
        {
            var command = response["Command"]?.ToString();
            if (!string.IsNullOrEmpty(command))
            {
                if (_responseTcs.TryRemove(command, out var tcs))
                {
                    tcs.TrySetResult(response);
                }
                MessageReceived?.Invoke(this, response);
            }
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var loginRequest = new JsonObject
        {
            ["Command"] = "Auth:UserLogin",
            ["Username"] = username,
            ["Password"] = password
        };

        var response = await SendRequestAsync(loginRequest);

        if (response["Success"]?.GetValue<bool>() == true)
        {
            Username = response["Username"]?.ToString();
            SessionID = response["SessionID"]?.ToString();
            return true;
        }
        else
        {
            var error = response["Error"]?.ToString() ?? "Unknown error";
            throw new Exception(error);
        }
    }

    public async Task<List<Chat>> GetChatListAsync()
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var chatListRequest = new JsonObject
        {
            ["Command"] = "Chat:List",
            ["SessionID"] = SessionID
        };

        var response = await SendRequestAsync(chatListRequest);

        if (response["Success"]?.GetValue<bool>() == true)
        {
            var chatList = response["Chats"]?.AsArray();
            if (chatList != null)
            {
                var chats = new List<Chat>();
                foreach (var chatNode in chatList)
                {
                    var chatObject = chatNode?.AsObject();
                    if (chatObject != null)
                    {
                        var chat = new Chat
                        {
                            ChatID = chatObject["ChatID"]?.GetValue<int>() ?? 0,
                            ToUID = chatObject["ToUID"]?.GetValue<int>() ?? 0,
                            TgPeer = chatObject["TgPeer"]?.ToString(),
                            DisplayName = chatObject["DisplayName"]?.ToString(),
                            Date = chatObject["Date"]?.GetValue<DateTime>() ?? DateTime.MinValue
                        };
                        chats.Add(chat);
                    }
                }
                return chats;
            }
        }

        throw new Exception(response["Error"]?.ToString() ?? "Failed to retrieve chat list.");
    }

    public class Chat
    {
        public int ChatID { get; set; }
        public int ToUID { get; set; }
        public string? TgPeer { get; set; }
        public string? DisplayName { get; set; }
        public DateTime Date { get; set; }
    }

    public async Task<List<Message>> GetMessagesAsync(int chatID)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var getMessagesRequest = new JsonObject
        {
            ["Command"] = "Chat:GetMessages",
            ["SessionID"] = SessionID,
            ["ChatID"] = chatID
        };

        var response = await SendRequestAsync(getMessagesRequest);

        if (response["Success"]?.GetValue<bool>() == true)
        {
            var messageList = response["Messages"]?.AsArray();
            if (messageList != null)
            {
                var messages = new List<Message>();
                foreach (var messageNode in messageList)
                {
                    var messageObject = messageNode?.AsObject();
                    if (messageObject != null)
                    {
                        var message = new Message
                        {
                            FromUID = messageObject["FromUID"]?.GetValue<int>() ?? 0,
                            ToUID = messageObject["ToUID"]?.GetValue<int?>(),
                            TgPeer = messageObject["TgPeer"]?.ToString(),
                            MessageText = messageObject["Text"]?.ToString(),
                            Date = messageObject["Date"]?.GetValue<DateTime>() ?? DateTime.MinValue,
                            Username = messageObject["Username"]?.ToString(),
                            IsOutgoing = messageObject["isOutgoing"]?.GetValue<bool>() ?? false
                        };
                        messages.Add(message);
                    }
                }
                return messages;
            }
        }

        throw new Exception(response["Error"]?.ToString() ?? "Failed to retrieve messages.");
    }

    public class Message
    {
        public int FromUID { get; set; }
        public int? ToUID { get; set; }
        public string? TgPeer { get; set; }
        public string? Username { get; set; }
        public string? MessageText { get; set; }
        public DateTime Date { get; set; }
        public bool IsOutgoing { get; set; }
    }



}
