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

namespace makefoxsrv
{
    internal class FoxWebAuth
    {
        //This function deletes a chat tab from the database
        [WebFunctionName("UserLogin")]      // Function name as seen in the URL or WebSocket command
        [WebLoginRequired(false)]           // User must be logged in to use this function
        public static async Task<JsonObject?> UserLogin(FoxUser fromUser, JsonObject jsonMessage)
        {
            string Username = FoxJsonHelper.GetString(jsonMessage, "Username", false)!;
            string Password = FoxJsonHelper.GetString(jsonMessage, "Password", false)!;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"SELECT uid, username
                                        FROM user_auth
                                        WHERE username = @username
                                        AND password_hash = SHA2(CONCAT(@password, salt), 256);";

                    cmd.Parameters.AddWithValue("username", Username);
                    cmd.Parameters.AddWithValue("password", Password);
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            long UID = r.GetInt64("uid");
                            Username = r.GetString("username"); // To standardize the case of the username

                            FoxUser? fUser = await FoxUser.GetByUID(UID);

                            if (fUser is null)
                                throw new Exception("Failed to load user from database.");

                            string SessionID = await FoxWebSessions.CreateSession(UID);

                            //var cookie = new Cookie("PHPSESSID", SessionID);
                            //context.Cookies.Append(cookie);

                            var response = new JsonObject
                            {
                                ["Command"] = "Auth:UserLogin",
                                ["Success"] = true,
                                ["SessionID"] = SessionID,
                                ["Username"] = Username,
                                ["AccessLevel"] = fUser.GetAccessLevel().ToString(),
                                ["UID"] = fUser.UID,
                            };

                            return response;
                        }
                        else
                        {
                            throw new Exception("Unknown username or wrong password.");
                        }
                    }
                }
            }

            throw new Exception("Unexpected error while authenticating user.");
        }
    }
}
