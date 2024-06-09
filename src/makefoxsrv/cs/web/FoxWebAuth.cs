using EmbedIO;
using EmbedIO.WebSockets;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TL;
using System.Text.Json.Nodes;

using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using System.CodeDom;
using System.Security.Cryptography;
using System.Net;
using System.Xml.Linq;
using EmbedIO.Sessions;

namespace makefoxsrv
{
    internal class FoxWebAuth
    {
        //This function deletes a chat tab from the database
        [WebFunctionName("UserLogin")]      // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(false)]           // User must be logged in to use this function
        public static async Task<JsonObject?> UserLogin(FoxWebSession session, JsonObject jsonMessage)
        {
            string Username = FoxJsonHelper.GetString(jsonMessage, "Username", false)!;
            string Password = FoxJsonHelper.GetString(jsonMessage, "Password", false)!;

            var result = await session.Login(Username, Password);

            if (!result)
                throw new Exception("Authentication failure.");

            if (session.user is null)
                throw new Exception("Unexpected error loading user.");

            var response = new JsonObject
            {
                ["Command"] = "Auth:UserLogin",
                ["Success"] = true,
                ["SessionID"] = session.Id,
                ["Username"] = Username,
                ["AccessLevel"] = session.user.GetAccessLevel().ToString(),
                ["UID"] = session.user.UID
            };

            return response;
        }
    }
}
