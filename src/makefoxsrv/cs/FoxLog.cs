using MySqlConnector;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TL;

using Newtonsoft.Json;

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
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.INFO;

        private static readonly object fileLock = new object();

        public static string SerializeExceptionToJson(Exception ex)
        {
            return JsonConvert.SerializeObject(ex, Formatting.None, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore // Prevent loops during serialization
            });
        }

        private static void LogToDatabase(
           string message,
           LogLevel level = LogLevel.INFO,
           Exception? ex = null,
           [CallerMemberName] string callerName = "",
           [CallerFilePath] string callerFilePath = "",
           [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                // Capture context data upfront
                var context = FoxContextManager.Current;
                var contextData = new
                {
                    Date = DateTime.Now,
                    ContextId = context.GetHashCode(),
                    UserId = context.User?.UID,
                    QueueId = context.Queue?.ID,
                    MessageId = context.Message?.ID,
                    WorkerId = context.Worker?.ID,
                    Command = context.Command?.Length > 254 ? context.Command.Substring(0, 254) : context.Command,
                    Message = message,
                    StackTrace = ex?.StackTrace,
                    TelegramUserId = context.Telegram?.User.ID,
                    TelegramChatId = context.Telegram?.Chat?.ID,
                    CallerName = callerName,
                    CallerFilePath = callerFilePath,
                    CallerLineNumber = lineNumber,
                    ExceptionJson = ex is null ? null : SerializeExceptionToJson(ex)
                };

                // Run the logging task in the background
                _ = Task.Run(async () =>
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
                        (@date, @type, @context_id, @user_id, @queue_id, @message_id, @worker_id, @command, @message, @stacktrace, @tele_userid, @tele_chatid, @caller_name, @caller_filepath, @caller_linenumber, @exception_json)"
                        };

                        cmd.Parameters.AddWithValue("@date", contextData.Date);
                        cmd.Parameters.AddWithValue("@type", level.ToString());
                        cmd.Parameters.AddWithValue("@context_id", contextData.ContextId);
                        cmd.Parameters.AddWithValue("@user_id", contextData.UserId);
                        cmd.Parameters.AddWithValue("@queue_id", contextData.QueueId);
                        cmd.Parameters.AddWithValue("@message_id", contextData.MessageId);
                        cmd.Parameters.AddWithValue("@worker_id", contextData.WorkerId);
                        cmd.Parameters.AddWithValue("@command", contextData.Command);
                        cmd.Parameters.AddWithValue("@message", contextData.Message);
                        cmd.Parameters.AddWithValue("@stacktrace", contextData.StackTrace);
                        cmd.Parameters.AddWithValue("@tele_userid", contextData.TelegramUserId);
                        cmd.Parameters.AddWithValue("@tele_chatid", contextData.TelegramChatId);
                        cmd.Parameters.AddWithValue("@caller_name", contextData.CallerName);
                        cmd.Parameters.AddWithValue("@caller_filepath", contextData.CallerFilePath);
                        cmd.Parameters.AddWithValue("@caller_linenumber", contextData.CallerLineNumber);
                        cmd.Parameters.AddWithValue("@exception_json", contextData.ExceptionJson);

                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception e)
                    {
                        FoxLog.LogToFile($"Error logging to database: {e.Message}\n{e.StackTrace}");
                    }
                });
            }
            catch (Exception e)
            {
                FoxLog.LogToFile($"Error preparing log to database: {e.Message}\n{e.StackTrace}");
            }
        }


        public static void LogException(Exception ex, string? customMessage = null, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            var message = customMessage ?? ex.Message;

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
