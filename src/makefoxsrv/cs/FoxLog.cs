using MySqlConnector;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using TL;

using Newtonsoft.Json;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

public enum LogLevel
{
    DEBUG,
    INFO,
    WARNING,
    ERROR
}

namespace makefoxsrv
{
    internal class FoxLog
    {
        private static readonly ConcurrentQueue<LogEntry> logQueue = new ConcurrentQueue<LogEntry>();
        private static readonly SemaphoreSlim logSemaphore = new SemaphoreSlim(0, int.MaxValue);
        private static volatile bool keepLogging = true;
        private static Task? logWorkerTask;
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.INFO;

        private static readonly object fileLock = new object();
        private static readonly object dbLock = new object();

        private static CancellationTokenSource? cancellationTokenSource;

        // The log entry record used by our new approach
        public class LogEntry
        {
            public DateTime Date { get; set; }
            public LogLevel Level { get; set; }
            public int ContextId { get; set; }
            public ulong? UserId { get; set; }
            public ulong? QueueId { get; set; }
            public int? MessageId { get; set; }
            public int? WorkerId { get; set; }
            public string? Command { get; set; }
            public string? Message { get; set; }
            public string? StackTrace { get; set; }
            public long? TelegramUserId { get; set; }
            public long? TelegramChatId { get; set; }
            public string? CallerName { get; set; }
            public string? CallerFilePath { get; set; }
            public int CallerLineNumber { get; set; }
            public string? ExceptionJson { get; set; }
        }

        // Call this once at application startup
        public static void StartLoggingThread(CancellationToken? cancellationToken = null)
        {
            if (logWorkerTask != null)
                throw new InvalidOperationException("Logging thread is already running.");

            // Create a new CancellationTokenSource for this task
            cancellationTokenSource = new CancellationTokenSource();

            // Combine the internal token with the external one, if provided
            var linkedTokenSource = cancellationToken != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken.Value)
                : cancellationTokenSource;

            // Spin up a background Task to handle all DB writes
            logWorkerTask = Task.Run(async () =>
            {
                WriteLine("Started logging task.");

                while (keepLogging)
                {
                    try
                    {
                        // Wait until there's at least one log to process
                        await logSemaphore.WaitAsync(linkedTokenSource.Token);

                        // Dequeue and process all available logs
                        while (logQueue.TryDequeue(out var logEntry))
                        {
                            await WriteLogEntryAsync(logEntry);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Handle cancellation gracefully
                        Console.WriteLine("Log processing task was cancelled.");
                        break; // Exit the loop when canceled
                    }
                    catch (Exception ex)
                    {
                        // If something goes wrong, log it to file or console
                        LogToFile("Error in logWorkerTask: " + ex);
                    }
                }
            }, linkedTokenSource.Token);
        }

        // Call this once at application shutdown (optional)
        public static async Task StopLoggingThreadAsync()
        {
            if (cancellationTokenSource is null || logWorkerTask is null)
                return; // No task to stop

            // Cancel the token to signal the task to stop
            cancellationTokenSource.Cancel();

            keepLogging = false;

            // Release the semaphore so it can exit
            logSemaphore.Release();

            if (logWorkerTask != null)
                await logWorkerTask;

            logWorkerTask = null;

            WriteLine("Stopped logging task.");
        }

        // The new log-writing method that enqueues a log entry
        public static void LogToDatabase(
            string message,
            LogLevel level = LogLevel.INFO,
            Exception? ex = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerName = "",
            [System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                int? teleMsgId = null;
                var context = FoxContextManager.Current;

                if (context.Message is TL.Message tlMsg)
                {
                    teleMsgId = tlMsg.ID;
                }
                else if (context.Message is TL.UpdateBotCallbackQuery tlCallback)
                {
                    teleMsgId = tlCallback.msg_id;
                }

                //StackTrace stackTrace = new StackTrace(true);

                var strStackTrace = ex?.StackTrace;

                if (ex is null)
                    strStackTrace = (new StackTrace(fNeedFileInfo: false, skipFrames: 2)).ToString();

                var logEntry = new LogEntry
                {
                    Date = DateTime.Now,
                    Level = level,
                    ContextId = context.GetHashCode(),
                    UserId = context.User?.UID,
                    QueueId = context.Queue?.ID,
                    MessageId = teleMsgId,
                    WorkerId = context.Worker?.ID,
                    Command = (context.Command?.Length ?? 0) > 254 ? context.Command?.Substring(0, 254) : context.Command,
                    Message = message,
                    StackTrace = strStackTrace,
                    TelegramUserId = context.Telegram?.User.ID,
                    TelegramChatId = context.Telegram?.Chat?.ID,
                    CallerName = callerName,
                    CallerFilePath = callerFilePath,
                    CallerLineNumber = lineNumber,
                    ExceptionJson = ex is null ? null : FoxStrings.SerializeToJson(ex),
                };

                // Enqueue the log entry, signal the background thread
                logQueue.Enqueue(logEntry);

                logSemaphore.Release();
            }
            catch (Exception e)
            {
                LogToFile($"Error preparing log to database: {e.Message}\n{e.StackTrace}");
            }
        }

        // The actual DB write (done in the background task, one at a time)
        private static async Task WriteLogEntryAsync(LogEntry entry)
        {
            try
            {
                using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
                await SQL.OpenAsync();

                using var cmd = new MySqlCommand
                {
                    Connection = SQL,
                    CommandText = @"
                    INSERT INTO log
                    (date, type, context_id, user_id, queue_id, message_id, worker_id, command, message, stacktrace, tele_userid, tele_chatid, caller_name, caller_filepath, caller_linenumber, exception_json) 
                    VALUES 
                    (@date, @type, @context_id, @user_id, @queue_id, @message_id, @worker_id, @command, @message, @stacktrace, @tele_userid, @tele_chatid, @caller_name, @caller_filepath, @caller_linenumber, @exception_json)
                "
                };

                cmd.Parameters.AddWithValue("@date", entry.Date);
                cmd.Parameters.AddWithValue("@type", entry.Level.ToString());
                cmd.Parameters.AddWithValue("@context_id", entry.ContextId);
                cmd.Parameters.AddWithValue("@user_id", entry.UserId);
                cmd.Parameters.AddWithValue("@queue_id", entry.QueueId);
                cmd.Parameters.AddWithValue("@message_id", entry.MessageId);
                cmd.Parameters.AddWithValue("@worker_id", entry.WorkerId);
                cmd.Parameters.AddWithValue("@command", entry.Command);
                cmd.Parameters.AddWithValue("@message", entry.Message);
                cmd.Parameters.AddWithValue("@stacktrace", entry.StackTrace);
                cmd.Parameters.AddWithValue("@tele_userid", entry.TelegramUserId);
                cmd.Parameters.AddWithValue("@tele_chatid", entry.TelegramChatId);
                cmd.Parameters.AddWithValue("@caller_name", entry.CallerName);
                cmd.Parameters.AddWithValue("@caller_filepath", entry.CallerFilePath);
                cmd.Parameters.AddWithValue("@caller_linenumber", entry.CallerLineNumber);
                cmd.Parameters.AddWithValue("@exception_json", entry.ExceptionJson);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // If the DB write fails, you might want to do some fallback logging
                LogToFile($"Error logging to DB: {ex.Message}\n{ex.StackTrace}");
            }
        }


        public static void LogException(Exception ex, string? customMessage = null, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            var message = customMessage ?? ex.Message;

            // Don't log cancellation exceptions to file
            if (ex is not OperationCanceledException)
                FoxLog.LogToFile($"Error: {message}\r\n{ex.StackTrace}\r\n");

            FoxLog.LogToDatabase(message, LogLevel.ERROR, ex, callerName, callerFilePath, lineNumber);

        }

        private static void LogToFile(string message, LogLevel level = LogLevel.INFO, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            if (level >= CurrentLogLevel)
            {
                // Log the message with a timestamp and the level
                //FoxLog.WriteLine();
                //FoxUI.AppendLog($"{DateTime.Now} [{level}]: {message}");
                //FoxUI.AppendLog(message);
                Console.Write(message);
            }

            _ = Task.Run(() =>
            {
                try
                {
                    string dateFormat = "dd MMM yyyy hh:mm:ss.ff tt";

                    string[] lines = Regex.Split(message, @"\r\n|\r|\n");

                    // Check if the last item is an empty string and remove it if necessary
                    if (lines.Length > 1 && lines[^1] == "")
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }

                    lock (fileLock) // Ensures one thread writes to the file at a time.
                    {
                        File.AppendAllLines("../logs/output.txt", lines.Select(line => $"{DateTime.Now.ToString(dateFormat)} {level}> {line}").ToArray());
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error writing log: {e.Message}");
                }
            });
        }

        public static void WriteLine(string message, LogLevel level = LogLevel.INFO, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            FoxLog.LogToDatabase(message, level, null, callerName, callerFilePath, lineNumber);

            Write(message + "\r\n", level, callerName, callerFilePath, lineNumber);
        }

        // Logging function
        public static void Write(string message, LogLevel level = LogLevel.INFO, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            FoxLog.LogToFile(message, level, callerName, callerFilePath, lineNumber);
        }

        public static class LogRotator
        {
            public static async Task Rotate()
            {
                await StopLoggingThreadAsync();

                using var connection = new MySqlConnection(FoxMain.sqlConnectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Get the current date and calculate the base new table name
                    var now = DateTime.Now;
                    var month = now.ToString("MMMM").ToLower(); // e.g., "december"
                    var weekNum = (int)Math.Ceiling(now.Day / 7.0);   // Week number within the current month
                    var baseTableName = $"log_{month}_{weekNum}";
                    var newTableName = GetUniqueTableName(connection, baseTableName, transaction);

                    // Step 1: Rename the current "log" table to the new unique table name
                    var renameTableQuery = $"RENAME TABLE `log` TO `{newTableName}`;";
                    using (var renameCommand = new MySqlCommand(renameTableQuery, connection, transaction))
                    {
                        await renameCommand.ExecuteNonQueryAsync();
                        Console.WriteLine($"Table `log` renamed to `{newTableName}`.");
                    }

                    // Step 2: Create the new "log" table by copying the schema from the old table
                    var createTableQuery = $"CREATE TABLE `log` LIKE `{newTableName}`;";
                    using (var createCommand = new MySqlCommand(createTableQuery, connection, transaction))
                    {
                        await createCommand.ExecuteNonQueryAsync();
                        Console.WriteLine("New `log` table created using the schema of the old table.");
                    }

                    await transaction.CommitAsync();
                    Console.WriteLine("Log rotation completed successfully.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Error rotating logs: {ex.Message}");
                }
                StartLoggingThread();
            }

            private static string GetUniqueTableName(MySqlConnection connection, string baseName, MySqlTransaction transaction)
            {
                string tableName = baseName;
                int suffix = 1;

                while (TableExists(connection, tableName, transaction))
                {
                    suffix++;
                    tableName = $"{baseName}_{suffix}";
                }

                return tableName;
            }

            private static bool TableExists(MySqlConnection connection, string tableName, MySqlTransaction transaction)
            {
                var query = $"SHOW TABLES LIKE '{tableName}';";
                using var command = new MySqlCommand(query, connection, transaction);
                using var reader = command.ExecuteReader();
                return reader.HasRows;
            }
        }
    }
}
