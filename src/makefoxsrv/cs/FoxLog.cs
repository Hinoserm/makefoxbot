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

        public static string SerializeExceptionToJson(Exception ex)
        {
            return JsonConvert.SerializeObject(ex, Formatting.None, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore // Prevent loops during serialization
            });
        }

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
        public static void StartLoggingThread()
        {
            // Spin up a background Task to handle all DB writes
            logWorkerTask = Task.Run(async () =>
            {
                while (keepLogging)
                {
                    try
                    {
                        // Wait until there's at least one log to process
                        await logSemaphore.WaitAsync();

                        // Dequeue and process all available logs
                        while (logQueue.TryDequeue(out var logEntry))
                        {
                            await WriteLogEntryAsync(logEntry);
                        }
                    }
                    catch (Exception ex)
                    {
                        // If something goes wrong, log it to file or console
                        LogToFile("Error in logWorkerTask: " + ex);
                    }
                }
            });
        }

        // Call this once at application shutdown (optional)
        public static async Task StopLoggingThreadAsync()
        {
            keepLogging = false;
            // Release the semaphore so it can exit
            logSemaphore.Release();
            if (logWorkerTask != null)
                await logWorkerTask;
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
                } else if (context.Message is TL.UpdateBotCallbackQuery tlCallback)
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
                    ExceptionJson = ex is null ? null : SerializeExceptionToJson(ex),
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
    }
}
