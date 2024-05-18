﻿using System;
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
using System.Linq.Expressions;

class FoxWebSockets {

    public static readonly ConcurrentDictionary<IWebSocketContext, FoxUser?> ActiveConnections = new ConcurrentDictionary<IWebSocketContext, FoxUser?>();

    public class Handler : WebSocketModule
    {
        public Handler(string urlPath)
            : base(urlPath, true)
        {
        }

        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            try {
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

                                await FoxWebChat.SaveChat(user, chatUser, null);

                                var output = await FoxWebChat.GetChatList(user);

                                await SendResponse(context, output);

                                break;
                            }
                        case "GetChatList":
                            {
                                if (user is null)
                                {
                                    await SendErrorResponse(context, "You must be logged in to use this feature.");
                                    return;
                                }

                                var output = await FoxWebChat.GetChatList(user);

                                await SendResponse(context, output);

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

                                await FoxWebChat.DeleteChat(user, chatId.Value);

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

                                var toUser = await FoxWebChat.GetUserFromChatId(user, chatId.Value);

                                if (toUser is null)
                                {
                                    await SendErrorResponse(context, "Chat not found or user not found.");
                                    return;
                                }

                                await FoxWebChat.SendChatMessage(user, toUser, null, jsonMessage["Message"].ToString());

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

                                var toUser = await FoxWebChat.GetUserFromChatId(user, chatId.Value);

                                if (toUser is null)
                                {
                                    await SendErrorResponse(context, "Chat not found or user not found.");
                                    return;
                                }

                                var output = await FoxWebChat.GetChatMessages(user, toUser, null);

                                await SendResponse(context, output);

                                break;
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                await SendErrorResponse(context, $"Error: {ex.Message}");
                Console.WriteLine($"Websocket Error: {ex.Message}\r\n{ex.StackTrace}");
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

    public static async Task SendResponse(IWebSocketContext context, object response)
    {
        string jsonMessage = JsonSerializer.Serialize(response);

        await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(jsonMessage), true);
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