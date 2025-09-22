using Autofocus;
using Autofocus.Models;
using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WTelegram;
using makefoxsrv;
using TL;
using System.Security.Policy;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace makefoxsrv
{
    internal class FoxNews
    {
        public static async Task BroadcastNewsItem(FoxTelegram telegram, long newsId, TL.InputPhoto? photo)
        {
            var replyMsg = await telegram.SendMessageAsync($"Thinking...");

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                // Retrieve the news message
                string newsQuery = "SELECT title, message FROM news_messages WHERE news_id = @newsId";
                string title = string.Empty;
                string message = string.Empty;

                using (var newsCommand = new MySqlCommand(newsQuery, connection))
                {
                    newsCommand.Parameters.AddWithValue("@newsId", newsId);

                    using (var reader = await newsCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            title = reader.GetString("title");
                            message = reader.GetString("message");
                        }
                        else
                        {
                            throw new Exception("News item not found.");
                        }
                    }
                }

                // Retrieve active users
                string userQuery = @"
                    SELECT q.uid
                    FROM queue q
                    LEFT JOIN user_news un ON q.uid = un.uid AND un.news_id = @newsId
                    WHERE q.date_added >= NOW() - INTERVAL 90 DAY
                    GROUP BY q.uid
                    HAVING COUNT(q.uid) >= 10 AND COUNT(un.news_id) = 0;";

                var activeUsers = new List<ulong>();

                using (var userCommand = new MySqlCommand(userQuery, connection))
                {
                    userCommand.Parameters.AddWithValue("@newsId", newsId);

                    using (var reader = await userCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            activeUsers.Add(reader.GetUInt64("uid"));
                        }
                    }
                }

                FoxLog.WriteLine($"Broadcasting news item {newsId} to {activeUsers.Count} active users.");
                
                var totalUserCount = activeUsers.Count;
                var count = 0;
                var lastUpdate = DateTime.Now;
                var errorCount = 0;

                var totalStopwatch = Stopwatch.StartNew(); // Start tracking total time

                await telegram.EditMessageAsync(replyMsg.ID, $"Sending news item {newsId} to {activeUsers.Count} active users.");

                // Broadcast the news message to active users
                foreach (var uid in activeUsers)
                {
                    using (var transaction = await connection.BeginTransactionAsync())
                    {
                        try
                        {
                            var user = await FoxUser.GetByUID(uid);
                            var teleUser = user?.TelegramID is not null ? await FoxTelegram.GetUserFromID(user.TelegramID.Value) : null;
                            var t = teleUser is not null ? new FoxTelegram(teleUser, null) : null;

                            TL.Message? msg = null;

                            if (t is null)
                                continue; //Nothing we can do here.

                            FoxLog.WriteLine($"Sending news item {newsId} to user {uid}.");

                            var entities = FoxTelegram.Client.HtmlToEntities(ref message);

                            if (photo is null)
                            {
                                msg = await t.SendMessageAsync(
                                    text: message,
                                    entities: entities,
                                    disableWebPagePreview: true
                                );
                            }
                            else
                            {
                                msg = await t.SendMessageAsync(
                                    text: message,
                                    entities: entities,
                                    media: photo,
                                    disableWebPagePreview: true
                                );
                            }

                            try
                            {
                                await t.PinMessage(msg.ID);
                            }
                            catch (Exception ex)
                            {
                                FoxLog.WriteLine($"Error pinning message for user {uid}: {ex.Message}");
                            }

                            string insertQuery = @"
                        REPLACE INTO user_news (uid, news_id, telegram_msg_id)
                        VALUES (@uid, @newsId, @telegramMsgId);";

                            using (var insertCommand = new MySqlCommand(insertQuery, connection, transaction))
                            {
                                insertCommand.Parameters.AddWithValue("@uid", uid);
                                insertCommand.Parameters.AddWithValue("@newsId", newsId);
                                insertCommand.Parameters.AddWithValue("@telegramMsgId", msg.ID);

                                await insertCommand.ExecuteNonQueryAsync();
                            }

                            await transaction.CommitAsync();

                            count++;
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            // Optionally, log the error or handle it as needed
                            FoxLog.WriteLine($"Error sending message to user {uid}: {ex.Message}");
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
                                    await telegram.EditMessageAsync(replyMsg.ID, statusMessage);
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

                await telegram.EditMessageAsync(replyMsg.ID, finalMessage);

                FoxLog.WriteLine($"Broadcasted news item {newsId} to {count} active users.");
            }
        }
    }
}