using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Reflection.Metadata;
using System.Linq.Expressions;
using System.Drawing;
using MySqlConnector;
using WTelegram;
using TL;
using System.Collections;
using PayPalCheckoutSdk.Orders;
using System.Diagnostics;

// A bunch of useful commands/functions for admins to use

namespace makefoxsrv
{
    internal class FoxAdmin
    {
        [BotCommand(cmd: "admin", sub: "archive", adminOnly: true)]
        public static async Task HandleRunArchiver(FoxTelegram t, FoxUser user, Message message, string? argument)
        {
            if (!Directory.Exists("../data/archive/images"))
            {
                // Refuse to run if the archive directory doesn't exist
                await t.SendMessageAsync(text: "❌ Image archiving is not enabled; archive directory is unavailable.", replyToMessage: message);
                return;
            }

            var archiveTime = FoxSettings.Get<int?>("ImageArchiveDays");

            // If the argument is a valid int, override the setting
            if (!string.IsNullOrWhiteSpace(argument) && int.TryParse(argument, out int daysArg) && daysArg > 0)
            {
                archiveTime = daysArg;
            }

            if (archiveTime is null || archiveTime <= 1)
            {
                await t.SendMessageAsync(text: "❌ Image archiving is not enabled. Set ImageArchiveDays in settings.", replyToMessage: message);
                return;
            }

            // Run the archiver in a separate thread so we don't block the main thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await FoxImageArchiver.ArchiveOlderThanAsync(DateTime.Now.AddDays(-archiveTime.Value), t, message);
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);
                }
            });
        }

        [BotCommand(cmd: "admin", sub: "leavegroup", adminOnly: true)]
        public static async Task HandleLeaveGroup(FoxTelegram t, FoxUser user, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a group ID to leave.\r\n\r\nFormat:\r\n  /admin #leave <gid>",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a group ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            long groupID = long.Parse(args[0]);

            var group = await FoxTelegram.GetChatFromID(groupID);

            if (group is null)
            {
                await t.SendMessageAsync(text: "❌ Unable to parse group ID.", replyToMessageId: message.ID);
            }

            await FoxTelegram.Client.LeaveChat(group);

            await t.SendMessageAsync(
                    text: $"✅ Okay.",
                    replyToMessageId: message.ID
                );
        }

        public static async Task HandleUncache(FoxTelegram t, Message message, string? argument)
        {
            if (string.IsNullOrEmpty(argument) || argument.ToLower().Split(' ').Contains("all"))
            {
                long usersRemoved = FoxUser.ClearCache();
                long queueRemoved = FoxQueue.Cache.Clear();
                long modelsUpdated = 0; // await FoxModel.ClearCache();

                await t.SendMessageAsync(
                    text: $"✅ Cleared all caches. Removed {usersRemoved + queueRemoved + modelsUpdated} items.",
                    replyToMessageId: message.ID
                );
            }
            else
            {
                var elements = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long totalRemoved = 0;

                foreach (var element in elements)
                {
                    switch (element.ToLower())
                    {
                        case "users":
                        case "user":
                            totalRemoved += FoxUser.ClearCache();
                            break;
                        case "queue":
                            totalRemoved += FoxQueue.Cache.Clear(true);
                            break;
                        case "model":
                        case "models":
                            //totalRemoved += await FoxModel.ClearCache();
                            break;
                        default:
                            await t.SendMessageAsync(
                                text: $"❌ Unknown cache element: {element}",
                                replyToMessageId: message.ID
                            );
                            return;
                    }
                }

                await t.SendMessageAsync(
                    text: $"✅ Cleared selected caches. Removed {totalRemoved} items.",
                    replyToMessageId: message.ID
                );
            }
        }

        [BotCommand(cmd: "admin", sub: "showgroups", adminOnly: true)]
        public static async Task HandleShowGroups(FoxTelegram t, FoxUser user, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID. Format:\r\n  /admin #showgroups <uid>\r\n  /admin #showgroups @username",
                    replyToMessage: message
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessage: message
                );

                return;
            }

            var findUser = await FoxUser.ParseUser(args[0]);

            if (findUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessage: message
                );

                return;
            }

            var groups = new Dictionary<long, string>();

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                // Check admin groups
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                SELECT tc.id, tc.title, tca.type AS admin_type
                FROM telegram_chats tc
                INNER JOIN telegram_chat_admins tca ON tc.id = tca.chatid
                WHERE tca.userid = @teleUserId";
                    cmd.Parameters.AddWithValue("@teleUserId", findUser.TelegramID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long chatId = reader.GetInt64("id");
                            string groupName = reader.GetString("title");
                            string adminType = reader.IsDBNull(reader.GetOrdinal("admin_type")) ? "" : " (Admin)";
                            if (!groups.ContainsKey(chatId))
                            {
                                groups.Add(chatId, groupName + adminType);
                            }
                        }
                    }
                }

                // Check telegram_log for messages sent by the user in groups
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                SELECT DISTINCT tc.id, tc.title
                FROM telegram_chats tc
                INNER JOIN telegram_log tl ON tl.chat_id = tc.id
                WHERE tl.user_id = @teleUserId AND tc.type IN ('GROUP', 'SUPERGROUP', 'GIGAGROUP')";
                    cmd.Parameters.AddWithValue("@teleUserId", findUser.TelegramID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long chatId = reader.GetInt64("id");
                            string groupName = reader.GetString("title");
                            if (!groups.ContainsKey(chatId))
                            {
                                groups.Add(chatId, groupName);
                            }
                        }
                    }
                }

                // Check queue for user entries in groups
                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                SELECT DISTINCT tc.id, tc.title
                FROM telegram_chats tc
                INNER JOIN queue q ON q.tele_chatid = tc.id
                WHERE q.tele_id = @teleUserId AND tc.type IN ('GROUP', 'SUPERGROUP', 'GIGAGROUP')";
                    cmd.Parameters.AddWithValue("@teleUserId", findUser.TelegramID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long chatId = reader.GetInt64("id");
                            string groupName = reader.GetString("title");
                            if (!groups.ContainsKey(chatId))
                            {
                                groups.Add(chatId, groupName);
                            }
                        }
                    }
                }
            }

            if (groups.Count == 0)
            {
                await t.SendMessageAsync(
                    text: $"ℹ️ User {findUser.UID} is not a member of any groups.",
                    replyToMessageId: message.ID
                );
            }
            else
            {
                var groupList = string.Join("\n", groups.Values);
                await t.SendMessageAsync(
                    text: $"📋 User {findUser.UID} is a member of the following groups:\n{groupList}",
                    replyToMessageId: message.ID
                );
            }
        }
    }
}
