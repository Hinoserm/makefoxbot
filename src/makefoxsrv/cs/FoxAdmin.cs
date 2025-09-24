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
        public static async Task HandleRunArchiver(FoxTelegram t, Message message, string? argument)
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

        public static async Task HandleQueueStatus(FoxTelegram t, Message message, string? argument)
        {
            var queueStatus = FoxQueue.GenerateQueueStatusMessage();

            var statusMessage = $"📊 Queue Status:\n\n" +
                                $"{queueStatus}\n";

            var originalMsg = await t.SendMessageAsync(text: statusMessage, replyToMessage: message);

            _ = Task.Run(async () =>
            {
                DateTime startTime = DateTime.Now;
                while (DateTime.Now - startTime < TimeSpan.FromMinutes(60))
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(8));

                        string updatedStatus = $"📊 Queue Status:\n\n{FoxQueue.GenerateQueueStatusMessage()}\n";

                        await t.EditMessageAsync(originalMsg.ID, updatedStatus);
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine($"Error updating queue status: {ex.Message}");
                        break; //Stop trying.
                    }
                }
            });
        }

        public static async Task HandleLeaveGroup(FoxTelegram t, Message message, string? argument)
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
                long modelsUpdated = await FoxModel.ClearCache();

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
                            totalRemoved += await FoxModel.ClearCache();
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

        public static async Task HandleBan(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to ban.\r\n\r\nFormat:\r\n  /admin #ban <uid>\r\n  /admin #ban @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            string? banMessage = args.Length == 2 ? args[1] : null;

            if (banUser.CheckAccessLevel(AccessLevel.PREMIUM) || banUser.CheckAccessLevel(AccessLevel.ADMIN))
            {
                await t.SendMessageAsync(
                    text: "❌ You can't ban an admin or premium user!",
                    replyToMessageId: message.ID
                );

                return;
            }

            if (banUser.GetAccessLevel() == AccessLevel.BANNED)
            {
                await t.SendMessageAsync(
                    text: "❌ User is already banned.",
                    replyToMessageId: message.ID
                );

                return;
            }

            await banUser.Ban(reasonMessage: banMessage);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} banned.",
                replyToMessageId: message.ID
            );
        }

        public static async Task HandleUnban(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID to unban.\r\n\r\nFormat:\r\n  /admin #unban <uid>\r\n  /admin #unban @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            string? banMessage = args.Length == 2 ? args[1] : null;

            if (banUser.GetAccessLevel() != AccessLevel.BANNED)
            {
                await t.SendMessageAsync(
                    text: "❌ User isn't currently banned!",
                    replyToMessageId: message.ID
                );

                return;
            }

            await banUser.UnBan(reasonMessage: banMessage);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} unbanned.",
                replyToMessageId: message.ID
            );
        }

        public static async Task HandleResetTerms(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID Format:\r\n  /admin #resetterms <uid>\r\n  /admin #resettos @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var banUser = await FoxUser.ParseUser(args[0]);

            if (banUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }


            await banUser.SetTermsAccepted(false);

            await t.SendMessageAsync(
                text: $"✅ User {banUser.UID} must now re-agree to the terms on their next command.",
                replyToMessageId: message.ID
            );
        }

        public static async Task HandlePremium(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID and a time span. Format:\r\n  /admin #premium <uid> <timespan>\r\n  /admin #premium @username <timespan>",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 2)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username and a time span.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var premiumUser = await FoxUser.ParseUser(args[0]);

            if (premiumUser is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            try
            {
                TimeSpan timeSpan = FoxStrings.ParseTimeSpan(args[1]);
                string action = timeSpan.TotalSeconds >= 0 ? "added to" : "subtracted from";

                DateTime oldExpiry = premiumUser.datePremiumExpires ?? DateTime.Now;
                DateTime newExpiry = oldExpiry.Add(timeSpan);

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "UPDATE users SET date_premium_expires = @date_premium_expires WHERE id = @uid";
                        cmd.Parameters.AddWithValue("@date_premium_expires", newExpiry);
                        cmd.Parameters.AddWithValue("@uid", premiumUser.UID);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await premiumUser.SetPremiumDate(newExpiry);

                string responseMessage = $"✅ {Math.Abs(timeSpan.TotalSeconds)} seconds have been {action} user {premiumUser.UID}'s premium membership.\n" +
                                         $"New Expiration Date: {newExpiry}";

                if (newExpiry < DateTime.Now)
                {
                    responseMessage += "\n⚠️ The new expiration date is in the past!";
                }

                await t.SendMessageAsync(
                    text: responseMessage,
                    replyToMessageId: message.ID
                );
            }
            catch (ArgumentException ex)
            {
                await t.SendMessageAsync(
                    text: $"❌ Invalid time span: {ex.Message}",
                    replyToMessageId: message.ID
                );
            }
        }

        public static async Task HandleShowGroups(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID. Format:\r\n  /admin #showgroups <uid>\r\n  /admin #showgroups @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var user = await FoxUser.ParseUser(args[0]);

            if (user is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
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
                    cmd.Parameters.AddWithValue("@teleUserId", user.TelegramID);

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
                    cmd.Parameters.AddWithValue("@teleUserId", user.TelegramID);

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
                    cmd.Parameters.AddWithValue("@teleUserId", user.TelegramID);

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
                    text: $"ℹ️ User {user.UID} is not a member of any groups.",
                    replyToMessageId: message.ID
                );
            }
            else
            {
                var groupList = string.Join("\n", groups.Values);
                await t.SendMessageAsync(
                    text: $"📋 User {user.UID} is a member of the following groups:\n{groupList}",
                    replyToMessageId: message.ID
                );
            }
        }

        public static async Task HandleShowPayments(FoxTelegram t, Message message, string? argument)
        {
            if (String.IsNullOrEmpty(argument))
            {
                await t.SendMessageAsync(
                    text: "❌ You must provide a user ID. Format:\r\n  /admin #payments <uid>\r\n  /admin #payments @username",
                    replyToMessageId: message.ID
                );

                return;
            }

            var args = argument.Split(' ', 2);

            if (args.Length < 1)
            {
                await t.SendMessageAsync(
                    text: "❌ You must specify a user ID or username.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var user = await FoxUser.ParseUser(args[0]);

            if (user is null)
            {
                await t.SendMessageAsync(
                    text: "❌ Unable to parse user ID.",
                    replyToMessageId: message.ID
                );

                return;
            }

            var payments = new List<(long id, DateTime date, decimal amount, int days, string currency, string provider)>();
            decimal total = 0;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = @"
                SELECT *
                FROM user_payments
                WHERE uid = @userId
                ORDER BY id ASC";
                    cmd.Parameters.AddWithValue("@userId", user.UID);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long id = reader.GetInt64("id");
                            DateTime date = reader.GetDateTime("date");
                            int days = reader.GetInt32("days");
                            string currency = reader.GetString("currency");
                            string provider = reader.GetString("type");

                            int inAmount = reader.GetInt32("amount");
                            double amount = currency == "XTR" ? inAmount * 0.013 : inAmount / 100; // Convert cents to decimal format
                            total += (decimal)amount;

                            payments.Add((id, date, (decimal)amount, days, currency, provider));
                        }
                    }
                }
            }

            if (payments.Count == 0)
            {
                await t.SendMessageAsync(
                    text: $"ℹ️ User {user.UID} has no recorded payments.",
                    replyToMessageId: message.ID
                );
            }
            else
            {
                var paymentDetails = payments.Select(p => $"{p.id}: ${p.amount:F2} {p.currency}, {p.days} days, {p.provider}, {p.date}");
                var paymentList = string.Join("\n", paymentDetails);

                await t.SendMessageAsync(
                    text: $"📋 Payment history for user {user.UID}:\n{paymentList}\n\nTotal: {payments.Count()} transactions (${total:F2})",
                    replyToMessageId: message.ID
                );
            }
        }

        public static async Task HandleForward(FoxTelegram telegram, Message message, string? argument)
        {
            // We need to turn the argument into an int number of days
            if (String.IsNullOrEmpty(argument))
            {
                await telegram.SendMessageAsync(
                    text: "❌ You must provide a duration. Format:\r\n\r\n  /admin #forward <days> days",
                    replyToMessageId: message.ID
                );
                return;
            }

            if (message.ReplyHeader is null || message.ReplyHeader.reply_to_msg_id == 0)
            {
                await telegram.SendMessageAsync(
                    text: "❌ You must reply to a message to forward it.",
                    replyToMessageId: message.ID
                );
                return;
            }

            if (!FoxStrings.TryParseDuration(argument, out var duration))
            {
                await telegram.SendMessageAsync(
                    text: "❌ Invalid duration. Format:\r\n\r\n  /admin #forward <days> days",
                    replyToMessageId: message.ID
                );
                return;
            }

            int forwardMsgId = message.ReplyHeader.reply_to_msg_id;

            var activeUsers = new List<long>();

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                // Retrieve active users
                string userQuery = @"
                    SELECT id
                    FROM users
                    WHERE
                       date_last_seen >= @date
                       AND access_level != 'BANNED'";

                using (var userCommand = new MySqlCommand(userQuery, connection))
                {
                    userCommand.Parameters.AddWithValue("@date", DateTime.Now - duration);

                    using (var reader = await userCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            activeUsers.Add(reader.GetInt64("id"));
                        }
                    }
                }
            }

            var totalUserCount = activeUsers.Count;
            var count = 0;
            var lastUpdate = DateTime.Now;
            var errorCount = 0;

            var totalStopwatch = Stopwatch.StartNew(); // Start tracking total time

            var statusMsg = await telegram.SendMessageAsync($"Forwarding message to {activeUsers.Count} active users.", message.ID);

            // Broadcast the news message to active users
            foreach (var uid in activeUsers)
            {
                try
                {
                    var user = await FoxUser.GetByUID((ulong)uid);
                    FoxContextManager.Current.User = user;

                    var teleUser = user?.TelegramID is not null ? await FoxTelegram.GetUserFromID(user.TelegramID.Value) : null;
                    var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

                    FoxContextManager.Current.Telegram = t;

                    if (teleUser is null || t is null) 
                        continue; //Nothing we can do here.

                    TL.InputPeer inputPeer = telegram.Peer;

                    await FoxTelegram.Client.ForwardMessagesAsync(inputPeer, new int[] { forwardMsgId }, teleUser, drop_author: true);

                    count++;
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);
                    errorCount++;
                }

                await Task.Delay(300); //Wait.

                try
                {
                    // Update the progress and ETA every 5 seconds
                    if ((DateTime.Now - lastUpdate).TotalSeconds >= 5)
                    {
                        lastUpdate = DateTime.Now;

                        // Calculate average time per user
                        var completedUsers = count + errorCount;
                        var averageTimePerUser = totalStopwatch.Elapsed.TotalSeconds / completedUsers;

                        // Calculate remaining time
                        var remainingUsers = totalUserCount - completedUsers;
                        var estimatedTimeRemaining = TimeSpan.FromSeconds(remainingUsers * averageTimePerUser);

                        var percentageComplete = (int)((completedUsers / (double)totalUserCount) * 100);
                        var statusMessage = $"Sent to {count}/{totalUserCount} users ({percentageComplete}% complete)";

                        if (errorCount > 0)
                        {
                            statusMessage += $", {errorCount} errored.";
                        }

                        statusMessage += $" ETA: {estimatedTimeRemaining:hh\\:mm\\:ss}";

                        try
                        {
                            await telegram.EditMessageAsync(statusMsg.ID, statusMessage);
                        }
                        catch (Exception ex)
                        {
                            FoxLog.WriteLine($"Error updating progress message: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"Error updating progress message: {ex.Message}");
                }
            }

            // Stop the total stopwatch as broadcasting is complete
            totalStopwatch.Stop();
            // Final edit with totals and elapsed time
            var totalElapsedTime = totalStopwatch.Elapsed;
            var finalMessage = $"Broadcast complete. Sent to {count} users successfully.";
            if (errorCount > 0)
            {
                finalMessage += $" {errorCount} users errored.";
            }
            finalMessage += $" Total time elapsed: {totalElapsedTime:hh\\:mm\\:ss}.";

            await telegram.EditMessageAsync(statusMsg.ID, finalMessage);

            FoxLog.WriteLine($"Broadcasted forwarded message to {count} active users.");



        }
    }
}
