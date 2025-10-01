using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv.commands
{
    internal class CmdAdminBroadcast
    {
        [BotCommand(cmd: "admin", sub: "forward", adminOnly: true)]
        public static async Task HandleForward(FoxTelegram telegram, FoxUser user, TL.Message message, string? argument)
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
                    var targetUser = await FoxUser.GetByUID((ulong)uid);
                    FoxContextManager.Current.User = targetUser;

                    var teleUser = targetUser?.TelegramID is not null ? await FoxTelegram.GetUserFromID(targetUser.TelegramID.Value) : null;
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
