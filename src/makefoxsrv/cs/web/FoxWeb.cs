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
using System.Text;
using TL;
using EmbedIO.Files;
using EmbedIO.Net;

class FoxWeb
{

    private static readonly ConcurrentDictionary<IWebSocketContext, FoxUser?> ActiveConnections = new ConcurrentDictionary<IWebSocketContext, FoxUser?>();

    public static WebServer StartWebServer(string url = "http://*:5555/", CancellationToken cancellationToken = default)
    {
        EndPointManager.UseIpv6 = false; //Otherwise this crashes on systems with ipv

        var server = new WebServer(o => o
                .WithUrlPrefix(url)
                .WithMode(HttpListenerMode.EmbedIO))
            .WithCors()
            .WithLocalSessionManager()
            .WithWebApi("/api", module => module.WithController<ApiController>())
            .WithModule(new WebSocketHandler("/ws"))
            .WithStaticFolder("/", "../wwwroot", true); // Serve static files from wwwroot directory

        server.StateChanged += (s, e) => Console.WriteLine($"WebServer New State - {e.NewState}");

        server.Start(cancellationToken);

        return server;
    }

    private class WebSocketHandler : WebSocketModule
    {
        public WebSocketHandler(string urlPath)
            : base(urlPath, true)
        {
        }

        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            var user = await GetUserFromRequest(context);

            var message = System.Text.Encoding.UTF8.GetString(buffer);
            Console.WriteLine("Received: " + message);

            var jsonMessage = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
            if (jsonMessage != null && jsonMessage.ContainsKey("Command"))
            {
                var command = jsonMessage["Command"].ToString();
                var query = jsonMessage["Query"].ToString();

                if (command == "Autocomplete")
                {
                    if (String.IsNullOrEmpty(query))
                        return; //No point wasting our time if the query is empty

                    if (user is null || !user.CheckAccessLevel(AccessLevel.ADMIN))
                        return; //Only admins can use this feature

                    var suggestions = await FoxUser.GetSuggestions(query);
                    var response = new AutocompleteResponse
                    {
                        Command = "AutocompleteResponse",
                        Suggestions = suggestions.Select(s => new Suggestion { Display = s.Display, Paste = s.Paste }).ToList()
                    };

                    var responseMessage = JsonSerializer.Serialize(response);
                    await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(responseMessage), true);
                }
            }
        }

        public class AutocompleteResponse
        {
            public string Command { get; set; }
            public List<Suggestion> Suggestions { get; set; }
        }

        public class Suggestion
        {
            public string Display { get; set; }
            public string Paste { get; set; }
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
    }

    private class ApiController : WebApiController
    {
        [Route(HttpVerbs.Any, "/test")]
        public async Task Test()
        {
            var user = await GetUserFromRequest(HttpContext);

            Console.WriteLine("Ping!" + HttpContext.Request.QueryString["Test"]);
            HttpContext.Response.ContentType = "text/plain";
            await HttpContext.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("Pong!"));
        }

        public static async Task<FoxUser?> GetUserFromRequest(IHttpContext context)
        {
            string? cookieHeader = context.Request.Headers.GetValues("Cookie")?.First();

            if (cookieHeader is not null)
            {
                var cookies = cookieHeader.Split(';')
                    .Select(cookie => cookie.Split('='))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                if (cookies.TryGetValue("PHPSESSID", out var sessionId))
                {
                    return await FoxWebSessions.GetUserFromSession(sessionId);
                }
            }

            return null;
        }
    }

    // Function to broadcast message to all WebSocket clients
    public static async Task BroadcastMessageAsync(FoxUser? user, TL.InputUser? tgUser, TL.InputPeer? tgPeer, string message)
    {
        var msg = new
        {
            ChatMsgRecv = new
            {
                UID = user?.UID,
                tgUser = tgUser?.UserId,
                tgPeer = tgPeer?.ID,
                Message = message
            }
        };

        string jsonMessage = JsonSerializer.Serialize(msg);

        foreach (var kvp in ActiveConnections)
        {
            var context = kvp.Key;
            var connectedUser = kvp.Value; // Get the user associated with the context (can be null)

            // You can add your logic here using the connectedUser
            if (connectedUser != null && (connectedUser.UID == user?.UID || connectedUser.GetAccessLevel() >= AccessLevel.ADMIN))
            {
                await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
            }
        }
    }
}