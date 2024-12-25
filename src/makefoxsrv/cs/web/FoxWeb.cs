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
using TL;
using System.Text.Json;
using System.Text;

using EmbedIO.Files;
using EmbedIO.Net;
using System.Reflection;
using static FoxWeb.MethodLookup;
using System.Text.Json.Nodes;

using JsonObject = System.Text.Json.Nodes.JsonObject;

class FoxWeb
{
    private static WebServer? server = null;

    public static WebServer StartWebServer(string url = "http://*:5555/", CancellationToken cancellationToken = default)
    {
        try
        {
            EndPointManager.UseIpv6 = false;

            MethodLookup.BuildFunctionLookup();

            server = new WebServer(o => o
                        .WithUrlPrefix(url)
                        .WithMode(HttpListenerMode.EmbedIO))
                    .WithCors()
                    .WithLocalSessionManager()
                    .WithModule(new FoxWebSockets.Handler("/ws"))
                    .WithWebApi("/api", module => module.WithController<DynamicController>())
                    //.WithStaticFolder("/", "../react/dist", false)
                    .WithStaticFolder("/", "../wwwroot", false)
                    .WithModule(new FileCacheInvalidationModule("/"));


            server.StateChanged += (s, e) => Console.WriteLine($"WebServer New State - {e.NewState}");

            server.Start(cancellationToken);

            return server;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting web server: {ex.Message}\r\n{ex.StackTrace}");
        }

        return null;
    }

    public static void StopWebServer ()
    {
        if (server is null)
            throw new Exception("Server is not running");

        server.Listener.Stop();
        server.Dispose();
    }

    // Define the dynamic controller that can handle all methods based on the lookup table
    public class DynamicController : WebApiController
    {
        public DynamicController()
        {

        }

        [Route(HttpVerbs.Any, "/{className}/{functionName}")]
        public async Task HandleDynamicRoute(string className, string functionName)
        {
            // Construct the command key to identify the method
            string command = $"{className}:{functionName}".ToLower();

            try
            {
                // Get the FoxUser from the request context or other appropriate source
                FoxUser? user = await GetUserFromRequest(HttpContext); // Ensure this method gets the FoxUser correctly
                JsonObject parameters = await ParseParameters(HttpContext);

                // Use the CallMethod function to invoke the method dynamically
                // FIXME: JsonObject? result = await MethodLookup.CallMethod(command, user, parameters);

                JsonObject? result = null;

                // Determine the appropriate response based on the result
                HttpContext.Response.ContentType = "application/json";
                if (result != null)
                {
                    string json = result.ToString() ?? "{}";
                    byte[] responseData = Encoding.UTF8.GetBytes(json);
                    await HttpContext.Response.OutputStream.WriteAsync(responseData, 0, responseData.Length);
                }
                else
                {
                    // Handle null result which implies no output is required
                    await HttpContext.Response.OutputStream.WriteAsync(new byte[0], 0, 0);
                }
            }
            catch (TargetInvocationException tie)
            {
                // Log and respond with the underlying cause if it's a reflection-invoked error
                var realError = tie.InnerException ?? tie; // Fallback to the outer exception if no inner
                Console.WriteLine($"Error invoking method: {realError.Message}\r\n{realError.StackTrace}");
                HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                string errorMessage = $"Error invoking method: {realError.Message}\r\n{realError.StackTrace}";
                byte[] errorData = Encoding.UTF8.GetBytes(errorMessage);
                await HttpContext.Response.OutputStream.WriteAsync(errorData, 0, errorData.Length);
            }
            catch (Exception ex)
            {
                // Handle other types of exceptions similarly
                Console.WriteLine($"Error invoking method: {ex.Message}\r\n{ex.StackTrace}");
                HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                string errorMessage = $"Error invoking method: {ex.Message}\r\n{ex.StackTrace}";
                byte[] errorData = Encoding.UTF8.GetBytes(errorMessage);
                await HttpContext.Response.OutputStream.WriteAsync(errorData, 0, errorData.Length);
            }
        }


    }



    private static async Task<JsonObject> ParseParameters(IHttpContext ctx)
    {
        // Initialize a dictionary to collect parameters
        var data = new Dictionary<string, JsonNode>();

        // Check if the method is GET or if we're dealing with query parameters
        if (ctx.Request.HttpMethod == HttpVerbs.Get.ToString() || ctx.Request.QueryString.Count > 0)
        {
            foreach (var key in ctx.Request.QueryString.AllKeys)
            {
                // Ensure key is not null before adding to dictionary
                if (key != null)
                {
                    data[key] = JsonValue.Create(ctx.Request.QueryString[key]);
                }
            }
        }

        // If the method is POST, determine the content type
        if (ctx.Request.HttpMethod == HttpVerbs.Post.ToString())
        {
            string contentType = ctx.Request.ContentType.Split(';')[0]; // Handle cases like "application/json; charset=utf-8"
            if (contentType == "application/json")
            {
                string jsonBody = await ctx.GetRequestBodyAsStringAsync();
                JsonObject jsonObj = JsonSerializer.Deserialize<JsonObject>(jsonBody) ?? new JsonObject();
                foreach (var prop in jsonObj)
                {
                    data[prop.Key] = prop.Value;
                }
            }
            else if (contentType == "application/x-www-form-urlencoded")
            {
                var form = await ctx.GetRequestFormDataAsync();
                foreach (var key in form.AllKeys)
                {
                    // Ensure key is not null before adding to dictionary
                    if (key != null)
                    {
                        data[key] = JsonValue.Create(form[key]);
                    }
                }
            }
        }

        // Convert the collected data to a JsonObject
        return new JsonObject(data);
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
                //return await FoxWebSession.GetUserFromSession(sessionId);
            }
        }

        return null;
    }

    public static class MethodLookup
    {
        public static Dictionary<string, MethodDetails> LookupTable = new Dictionary<string, MethodDetails>(StringComparer.OrdinalIgnoreCase);

        // Structure to hold the method information
        public class MethodDetails
        {
            public string ClassName { get; set; }
            public string FunctionName { get; set; }
            public MethodInfo MethodInfo { get; set; }
            public bool LoginRequired { get; set; } // Indicates if login is required
            public AccessLevel? AccessLevel { get; set; } = null; // Default to the most restrictive
        }

        // Initialize the lookup table with method details
        public static void BuildFunctionLookup()
        {
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (!type.Name.StartsWith("FoxWeb")) continue;

                string className = type.Name.Replace("FoxWeb", "");
                foreach (var method in type.GetMethods())
                {
                    var functionNameAttribute = method.GetCustomAttribute<WebFunctionNameAttribute>();
                    if (functionNameAttribute != null)
                    {
                        string key = $"{className}:{functionNameAttribute.FunctionName}".ToLower();
                        LookupTable[key] = new MethodDetails
                        {
                            ClassName = className,
                            FunctionName = functionNameAttribute.FunctionName,
                            MethodInfo = method,
                            LoginRequired = method.GetCustomAttribute<WebLoginRequiredAttribute>()?.LoginRequired ?? true,
                            AccessLevel = method.GetCustomAttribute<WebAccessLevelAttribute>()?.RequiredLevel ?? null
                        };
                    }
                }
            }
        }

        public static async Task<JsonObject?> CallMethod(string command, FoxWebSession session, JsonObject jsonParameters)
        {
            string key = command.ToLower();

            if (LookupTable.TryGetValue(key, out MethodDetails details))
            {
                // Check if user login is required and if the user is logged in
                // Setting the AccessLevel implies the login check is also required
                if ((details.LoginRequired || details.AccessLevel is not null) && session.user is null)
                {
                    throw new Exception("User must be logged in to use this function.");
                }

                // Check if the user meets the required access level
                // Make sure to only check the AccessLevel if it's set
                if (details.AccessLevel is not null && (session.user is null || !session.user.CheckAccessLevel(details.AccessLevel.Value)))
                {
                    throw new Exception($"User does not have the required access level. Required: {details.AccessLevel}");
                }

                object[] args = new object[] { session, jsonParameters };

                object? result = details.MethodInfo.IsStatic
                    ? details.MethodInfo.Invoke(null, args)
                    : details.MethodInfo.Invoke(Activator.CreateInstance(details.MethodInfo.DeclaringType), args);

                if (result is Task taskResult)
                {
                    await taskResult;
                    if (taskResult.GetType().IsGenericType)
                    {
                        var taskValue = ((dynamic)taskResult).Result;
                        return taskValue as JsonObject;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return result as JsonObject;
                }
            }
            else
            {
                throw new Exception("Method not found.");
            }
        }

    }
}

// Attribute to specify the function name within the method
[AttributeUsage(AttributeTargets.Method)]
public class WebFunctionNameAttribute : Attribute
{
    public string FunctionName { get; }

    public WebFunctionNameAttribute(string functionName)
    {
        FunctionName = functionName;
    }
}

// Attribute to specify if user login is required
[AttributeUsage(AttributeTargets.Method)]
public class WebLoginRequiredAttribute : Attribute
{
    public bool LoginRequired { get; }

    public WebLoginRequiredAttribute(bool loginRequired)
    {
        LoginRequired = loginRequired;
    }
}

// Attribute to specify the minimum access level required
[AttributeUsage(AttributeTargets.Method)]
public class WebAccessLevelAttribute : Attribute
{
    public AccessLevel RequiredLevel { get; }

    public WebAccessLevelAttribute(AccessLevel requiredLevel)
    {
        RequiredLevel = requiredLevel;
    }
}