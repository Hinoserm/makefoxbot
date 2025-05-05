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
using System.Data;
using WTelegram;
using Swan;
using Swan.Formatters;
using System.Reflection.PortableExecutable;
using System.Linq.Expressions;
using static FoxWeb;
using System.Reflection;

using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using System.Xml;
using EmbedIO.Sessions;

class FoxWebSockets {

    public static readonly ConcurrentDictionary<IWebSocketContext, FoxWebSession?> ActiveConnections = new ConcurrentDictionary<IWebSocketContext, FoxWebSession?>();

    public class Handler : WebSocketModule
    {
        public Handler(string urlPath)
            : base(urlPath, true)
        {
        }

        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            JsonNode? seqID = null;
            string? command = null;
            JsonObject? responseMessage = null;
            FoxWebSession? session = null;

            try
            {
                var message = System.Text.Encoding.UTF8.GetString(buffer);

                Console.WriteLine("Received: " + message);

                System.Text.Json.Nodes.JsonObject? jsonMessage = JsonNode.Parse(message)?.AsObject();

                if (jsonMessage is null)
                    throw new Exception("Invalid JSON message received!");

                // Extract and store the "SeqID"
                seqID = jsonMessage.ContainsKey("SeqID") ? jsonMessage["SeqID"].Deserialize<JsonNode>() : null;

                string? sessionID = FoxJsonHelper.GetString(jsonMessage, "SessionID", true);

                session = await FoxWebSession.LoadFromContext(context, sessionID);

                if (!jsonMessage.ContainsKey("Command"))
                    throw new Exception("No Command specified in message.");

                command = jsonMessage["Command"]?.ToString();

                switch (command)
                {
                    case "Ping":
                        {
                            responseMessage = new JsonObject
                            {
                                ["Command"] = "Pong"
                            };

                            break;
                        }
                    case "Session:Info":
                        {
                            if (session is null)
                                throw new Exception("No session.");

                            responseMessage = new JsonObject
                            {
                                ["SessionID"] = session.Id,
                                ["LoggedIn"] = session.user is not null,
                                ["Success"] = true
                            };

                            if (session.user is not null)
                            {
                                responseMessage["UID"] = session.user.UID;
                                responseMessage["AccessLevel"] = session.user.GetAccessLevel().ToString();
                            }

                            break;
                        }
                    case "Session:End":
                        {
                            // Mark the session as ended and forget it completely.

                            if (session is null)
                                throw new Exception("No session.");

                            await session.Save();
                            session.End();

                            responseMessage = new JsonObject
                            {
                                ["SessionID"] = session.Id,
                                ["Success"] = true
                            };

                            break;
                        }
                    case "Session:Forget":
                        {
                            // Remove the session for only this context, leaving it valid for re-use.

                            if (session is null)
                                throw new Exception("No session.");

                            FoxWebSession.RemoveByContext(context);

                            responseMessage = new JsonObject
                            {
                                ["SessionID"] = session.Id,
                                ["Success"] = true
                            };

                            break;
                        }
                    case "Autocomplete":
                        {
                            var query = jsonMessage["Query"].ToString();

                            if (string.IsNullOrEmpty(query))
                                return; // No point wasting our time if the query is empty

                            if (session.user is null || !session.user.CheckAccessLevel(AccessLevel.ADMIN))
                                return; // Only admins can use this feature

                            var suggestions = await FoxUser.GetSuggestions(query);
                            var response = new AutocompleteResponse
                            {
                                Command = "AutocompleteResponse",
                                Suggestions = suggestions.Select(s => new AutocompleteSuggestion { Display = s.Display, Paste = s.Paste }).ToList()
                            };

                            var responseJson = JsonSerializer.SerializeToNode(response).AsObject();
                            responseMessage = responseJson;
                            break;
                        }
                    default:
                        {
                            JsonObject response;

                            try
                            {
                                // Use the CallMethod function to invoke the method dynamically
                                JsonObject? output = await MethodLookup.CallMethod(command, session, jsonMessage);

                                if (output is null)
                                {
                                    // Send a generic Success response if the method didn't return anything
                                    response = new JsonObject
                                    {
                                        ["Command"] = command,
                                        ["Success"] = true
                                    };
                                }
                                else
                                {
                                    response = output;
                                }
                            }
                            catch (TargetInvocationException tie)
                            {
                                // Log and respond with the underlying cause if it's a reflection-invoked error
                                var realError = tie.InnerException ?? tie; // Fallback to the outer exception if no inner
                                Console.WriteLine($"Error invoking method: {realError.Message}\r\n{realError.StackTrace}");

                                response = new JsonObject
                                {
                                    ["Command"] = command,
                                    ["Success"] = false,
                                    ["Error"] = $"{realError.Message}"
                                };
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error invoking method: {ex.Message}\r\n{ex.StackTrace}");

                                response = new JsonObject
                                {
                                    ["Command"] = command,
                                    ["Success"] = false,
                                    ["Error"] = $"{ex.Message}"
                                };
                            }

                            responseMessage = response;
                            break;
                        }
                }

                if (responseMessage is null)
                    throw new Exception("No response was generated!");
            }
            catch (Exception ex)
            {
                responseMessage = new JsonObject
                {
                    ["Command"] = command ?? "Error",
                    ["Success"] = false,
                    ["Error"] = $"{ex.Message}"
                };

                Console.WriteLine($"Websocket Error: {ex.Message}\r\n{ex.StackTrace}");
            }

            try
            {
                if (responseMessage is not null)
                {
                    if (!responseMessage.ContainsKey("Command"))
                        responseMessage["Command"] = command;

                    if (!responseMessage.ContainsKey("Success"))
                        responseMessage["Success"] = true;

                    if (seqID is not null)
                        responseMessage["SeqID"] = JsonNode.Parse(seqID.ToJsonString());

                    await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(responseMessage.ToJsonString()), true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Websocket error: {ex.Message}\r\n{ex.StackTrace}");
            }
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            FoxWebSession? session = await FoxWebSession.LoadFromContext(context, createNew: false);

            Console.WriteLine($"WebSocket Connected. User: {(session?.user?.Username ?? "(none)")}");
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            var removedSessions = FoxWebSession.RemoveByContext(context);

            // Print comma-delimited list of usernames or "(none)"
            var usernames = string.Join(", ", removedSessions.Select(s => s.user?.Username ?? "(none)"));

            Console.WriteLine($"WebSocket disconnected! User: {usernames}");
            return Task.CompletedTask;
        }
    }

    public static async Task SendResponse(IWebSocketContext context, object response)
    {
        string jsonMessage = JsonSerializer.Serialize(response);

        await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
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