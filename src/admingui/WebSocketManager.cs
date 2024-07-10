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
    private ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _responseTcs;
    private long _seqId;

    public string? Username { get; private set; }
    public string? SessionID { get; private set; }

    public static WebSocketManager Instance => _instance ??= new WebSocketManager();

    public event EventHandler<JsonObject> MessageReceived;

    private WebSocketManager()
    {
        _responseTcs = new ConcurrentDictionary<long, TaskCompletionSource<JsonObject>>();
        _seqId = 0;
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

        long seqId = GetNextSeqID();
        request["SeqID"] = seqId;

        var tcs = new TaskCompletionSource<JsonObject>();
        _responseTcs[seqId] = tcs;

        string jsonRequest = request.ToJsonString();
        Console.WriteLine($"Sending request: {jsonRequest}");
        await _webSocketClient.SendInstant(jsonRequest);
        return await tcs.Task; // Wait for the response
    }

    public event EventHandler<MessageEventArgs> NewMessageReceived;

    private void HandleMessage(string message)
    {
        Console.WriteLine($"Handling message: {message}");
        var response = JsonNode.Parse(message)?.AsObject();

        if (response != null)
        {
            if (response["SeqID"]?.GetValue<long>() is long seqId)
            {
                if (_responseTcs.TryRemove(seqId, out var tcs))
                {
                    tcs.TrySetResult(response);
                }
            }

            var command = response["Command"]?.ToString();
            switch (command)
            {
                case "Chat:NewMessage":
                    var chatID = response["ChatID"]?.GetValue<int>() ?? 0;
                    var messageObject = response["Message"]?.AsObject();
                    var newMessage = new Message
                    {
                        FromUID = messageObject["FromUID"]?.GetValue<int>() ?? 0,
                        ToUID = messageObject["ToUID"]?.GetValue<int?>(),
                        TgPeer = messageObject["TgPeer"]?.ToString(),
                        MessageText = messageObject["Text"]?.ToString(),
                        Date = messageObject["Date"]?.GetValue<DateTime>() ?? DateTime.MinValue,
                        Username = messageObject["Username"]?.ToString(),
                        IsOutgoing = messageObject["isOutgoing"]?.GetValue<bool>() ?? false
                    };
                    NewMessageReceived?.Invoke(this, new MessageEventArgs(chatID, newMessage));
                    break;
                default:
                    MessageReceived?.Invoke(this, response);
                    break;
            }
        }
    }

    public class MessageEventArgs : EventArgs
    {
        public int ChatID { get; }
        public Message Message { get; }

        public MessageEventArgs(int chatID, Message message)
        {
            ChatID = chatID;
            Message = message;
        }
    }

    private long GetNextSeqID()
    {
        return ++_seqId;
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

    public async Task<bool> DeleteChatAsync(int chatID)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var deleteChatRequest = new JsonObject
        {
            ["Command"] = "Chat:Delete",
            ["SessionID"] = SessionID,
            ["ChatID"] = chatID
        };

        var response = await SendRequestAsync(deleteChatRequest);

        return response["Success"]?.GetValue<bool>() == true;
    }
    public async Task SendMessageAsync(int chatID, string message)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var sendMessageRequest = new JsonObject
        {
            ["Command"] = "Chat:SendMessage",
            ["SessionID"] = SessionID,
            ["ChatID"] = chatID,
            ["Message"] = message
        };

        await SendRequestAsync(sendMessageRequest);
    }

    public async Task<int> CreateNewChatAsync(string username)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var newChatRequest = new JsonObject
        {
            ["Command"] = "Chat:New",
            ["SessionID"] = SessionID,
            ["Username"] = username
        };

        var response = await SendRequestAsync(newChatRequest);

        if (response["Success"]?.GetValue<bool>() == true)
        {
            return response["ChatID"]?.GetValue<int>() ?? 0;
        }

        throw new Exception(response["Error"]?.ToString() ?? "Failed to create new chat.");
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
        public long ChatID { get; set; }
        public long ToUID { get; set; }
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

    public async Task<List<Suggestion>> GetAutocompleteSuggestionsAsync(string query)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var autocompleteRequest = new JsonObject
        {
            ["Command"] = "Autocomplete",
            ["SessionID"] = SessionID,
            ["Query"] = query
        };

        var response = await SendRequestAsync(autocompleteRequest);

        if (response["Command"]?.ToString() == "AutocompleteResponse")
        {
            var suggestions = response["Suggestions"]?.AsArray();
            if (suggestions != null)
            {
                var result = new List<Suggestion>();
                foreach (var suggestion in suggestions)
                {
                    if (suggestion?["Display"]?.ToString() is string display &&
                        suggestion?["Paste"]?.ToString() is string paste)
                    {
                        result.Add(new Suggestion
                        {
                            Display = display,
                            Paste = paste
                        });
                    }
                }
                return result;
            }
        }

        throw new Exception(response["Error"]?.ToString() ?? "Failed to retrieve autocomplete suggestions.");
    }

    public class Suggestion
    {
        public string Display { get; set; }
        public string Paste { get; set; }
    }

    public async Task<List<Dictionary<string, object>>> GetQueueItemsAsync(int pageNumber, int pageSize, long? uid = null, string? status = null, string? type = null, List<string>? columns = null)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var getQueueItemsRequest = new JsonObject
        {
            ["Command"] = "Queue:List",
            ["SessionID"] = SessionID,
            ["PageNumber"] = pageNumber,
            ["PageSize"] = pageSize
        };

        if (status != null)
        {
            getQueueItemsRequest["Status"] = status;
        }

        if (type != null)
        {
            getQueueItemsRequest["Type"] = type;
        }

        if (uid.HasValue)
        {
            getQueueItemsRequest["UID"] = uid.Value;
        }

        if (columns != null && columns.Any())
        {
            getQueueItemsRequest["Columns"] = JsonSerializer.SerializeToNode(columns);
        }

        var response = await SendRequestAsync(getQueueItemsRequest);

        if (response["Success"]?.GetValue<bool>() == true)
        {
            var queueItemsArray = response["QueueItems"]?.AsArray();
            if (queueItemsArray != null)
            {
                var queueItems = new List<Dictionary<string, object>>();
                foreach (var queueItemNode in queueItemsArray)
                {
                    var queueItemObject = queueItemNode?.AsObject();
                    if (queueItemObject != null)
                    {
                        var queueItemDict = new Dictionary<string, object>();
                        foreach (var kvp in queueItemObject)
                        {
                            queueItemDict[kvp.Key] = kvp.Value;
                        }
                        queueItems.Add(queueItemDict);
                    }
                }
                return queueItems;
            }
        }

        throw new Exception(response["Error"]?.ToString() ?? "Failed to retrieve queue items.");
    }

    public async Task<byte[]> GetImageAsync(long imageId, int? maxSize = null)
    {
        if (string.IsNullOrEmpty(SessionID))
        {
            throw new InvalidOperationException("SessionID is not set. Please log in first.");
        }

        var getImageRequest = new JsonObject
        {
            ["Command"] = "Image:Get",
            ["SessionID"] = SessionID,
            ["ImageID"] = imageId
        };

        if (maxSize.HasValue)
        {
            getImageRequest["MaxSize"] = maxSize.Value;
        }

        var response = await SendRequestAsync(getImageRequest);

        if (response["Success"]?.GetValue<bool>() == true)
        {
            var imageDataBase64 = response["Image"]?.ToString();
            if (!string.IsNullOrEmpty(imageDataBase64))
            {
                return Convert.FromBase64String(imageDataBase64);
            }
            throw new Exception("Image data is missing in the response.");
        }

        throw new Exception(response["Error"]?.ToString() ?? "Failed to retrieve the image.");
    }


}
