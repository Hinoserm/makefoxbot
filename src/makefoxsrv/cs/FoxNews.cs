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
using Terminal.Gui;

namespace makefoxsrv
{
    internal class FoxNews
    {
        public static async Task BroadcastNewsItem(long newsId)
        {
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
                    WHERE q.date_added >= NOW() - INTERVAL 30 DAY
                    GROUP BY q.uid
                    HAVING COUNT(q.uid) >= 10 AND COUNT(un.news_id) = 0;";

                var activeUsers = new List<long>();

                using (var userCommand = new MySqlCommand(userQuery, connection))
                {
                    userCommand.Parameters.AddWithValue("@newsId", newsId);

                    using (var reader = await userCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            activeUsers.Add(reader.GetInt64("uid"));
                        }
                    }
                }

                FoxLog.WriteLine($"Broadcasting news item {newsId} to {activeUsers.Count} active users.");

                var count = 0;

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

                            if (t is null)
                                continue; //Nothing we can do here.

                            FoxLog.WriteLine($"Sending news item {newsId} to user {uid}.");

                            var entities = FoxTelegram.Client.HtmlToEntities(ref message);

                            var msg = await t.SendMessageAsync(
                                text: message,
                                entities: entities,
                                disableWebPagePreview: true
                            );


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
                        }

                        await Task.Delay(2000); //Wait.
                    }
                }

                FoxLog.WriteLine($"Broadcasted news item {newsId} to {count} active users.");
            }
        }

        public async Task CheckAndSendUnseenMessages(long uid, Func<long, string, Task<long>> sendMessageToUserAsync)
        {
            string connectionString = "your_connection_string";

            using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string selectQuery = @"
                    SELECT nm.news_id, nm.title, nm.message
                    FROM news_messages nm
                    LEFT JOIN user_news un ON nm.news_id = un.news_id AND un.uid = @uid
                    WHERE un.news_id IS NULL
                    ORDER BY nm.date_added;";

                        using (var selectCommand = new MySqlCommand(selectQuery, connection, transaction))
                        {
                            selectCommand.Parameters.AddWithValue("@uid", uid);

                            using (var reader = await selectCommand.ExecuteReaderAsync())
                            {
                                var unseenMessages = new List<(long newsId, string title, string message)>();

                                while (await reader.ReadAsync())
                                {
                                    long newsId = reader.GetInt64("news_id");
                                    string title = reader.GetString("title");
                                    string message = reader.GetString("message");
                                    unseenMessages.Add((newsId, title, message));
                                }

                                await reader.CloseAsync();

                                foreach (var (newsId, title, message) in unseenMessages)
                                {
                                    long telegramMsgId = await sendMessageToUserAsync(uid, $"{title}\n\n{message}");

                                    string insertQuery = @"
                                INSERT INTO user_news (uid, news_id, telegram_msg_id)
                                VALUES (@uid, @newsId, @telegramMsgId);";

                                    using (var insertCommand = new MySqlCommand(insertQuery, connection, transaction))
                                    {
                                        insertCommand.Parameters.AddWithValue("@uid", uid);
                                        insertCommand.Parameters.AddWithValue("@newsId", newsId);
                                        insertCommand.Parameters.AddWithValue("@telegramMsgId", telegramMsgId);

                                        await insertCommand.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }
    }
}