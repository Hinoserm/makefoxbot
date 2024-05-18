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
    public static WebServer StartWebServer(string url = "http://*:5555/", CancellationToken cancellationToken = default)
    {
        EndPointManager.UseIpv6 = false; //Otherwise this crashes on systems with ipv

        var server = new WebServer(o => o
                .WithUrlPrefix(url)
                .WithMode(HttpListenerMode.EmbedIO))
            .WithCors()
            .WithLocalSessionManager()
            .WithWebApi("/api", module => module.WithController<ApiController>())
            .WithModule(new FoxWebSockets.Handler("/ws"))
            .WithStaticFolder("/", "../wwwroot", false) // Serve static files from wwwroot directory
            .WithModule(new FileCacheInvalidationModule("/")); // Custom middleware to set no-cache headers

        server.StateChanged += (s, e) => Console.WriteLine($"WebServer New State - {e.NewState}");

        server.Start(cancellationToken);

        return server;
    }

    private class FileCacheInvalidationModule : WebModuleBase
    {
        public FileCacheInvalidationModule(string baseRoute) : base(baseRoute)
        {
        }

        public override bool IsFinalHandler => false;

        protected override async Task OnRequestAsync(IHttpContext context)
        {
            // Set headers to prevent caching
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";

            //await SendNextAsync(context); // Ensure the next handler in the pipeline is executed
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

        [Route(HttpVerbs.Any, "/chat/getchatlist")]
        public async Task GetChatList()
        {
            var user = await GetUserFromRequest(HttpContext);

            if (user is null)
            {
                await SendErrorResponse(HttpContext, "You must be logged in to use this function.");
                return;
            }

            var output = await FoxWebChat.GetChatList(user);

            await SendResponse(HttpContext, output);
        }

        [Route(HttpVerbs.Any, "/chat/getchatmessages")]
        public async Task GetChatMessages()
        {
            var user = await GetUserFromRequest(HttpContext);

            if (user is null)
            {
                await SendErrorResponse(HttpContext, "You must be logged in to use this function.");
                return;
            }

            var toUserStr = HttpContext.Request.QueryString["toUID"];

            long? toUID = toUserStr is null ? null : long.Parse(toUserStr);

            if (toUID is null)
            {
                await SendErrorResponse(HttpContext, "toUID parameter is required.");
                return;
            }

            var toUser = await FoxUser.GetByUID(toUID.Value);

            if (toUser is null)
            {
                await SendErrorResponse(HttpContext, "User not found.");
                return;
            }

            var output = await FoxWebChat.GetChatMessages(user, toUser, null);

            await SendResponse(HttpContext, output);
        }

        public static async Task SendResponse(IHttpContext context, object response)
        {
            string jsonMessage = JsonSerializer.Serialize(response);

            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(jsonMessage));
        }

        public static async Task SendErrorResponse(IHttpContext context, string errorMessage)
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

            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(jsonMessage));
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
}