using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public class WebSocketManager
{
    private static WebSocketManager _instance;
    private ClientWebSocket _webSocket;
    private TaskCompletionSource<JsonObject> _responseTcs;
    public string? Username { get; private set; }
    public string? SessionID { get; private set; }

    public static WebSocketManager Instance => _instance ??= new WebSocketManager();

    private WebSocketManager() { }

    public async Task ConnectAsync(string url)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(url), CancellationToken.None);
        _ = ReceiveMessagesAsync();
    }

    public async Task<JsonObject> SendRequestAsync(JsonObject request)
    {
        _responseTcs = new TaskCompletionSource<JsonObject>();
        string jsonRequest = request.ToJsonString();
        await SendMessageAsync(jsonRequest);
        return await _responseTcs.Task; // Wait for the response
    }

    private async Task SendMessageAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var buffer = new ArraySegment<byte>(bytes);
        await _webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[1024 * 4];
        while (_webSocket.State == WebSocketState.Open)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            HandleMessage(message);
        }
    }

    private void HandleMessage(string message)
    {
        var response = JsonNode.Parse(message)?.AsObject();

        if (response != null && response["Command"]?.ToString() == "Auth:UserLogin")
        {
            _responseTcs.TrySetResult(response);
        }

        // Handle other messages if necessary
        // ...
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
}
