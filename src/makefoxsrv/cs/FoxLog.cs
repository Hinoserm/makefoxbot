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

        private static async Task LogToDatabase(string message, LogLevel level = LogLevel.INFO, Exception ? ex = null, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                // Retrieve the current context
                var context = FoxContextManager.Current; 

                // Insert into the database
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = @"
                            INSERT INTO log
                            (date, type, user_id, queue_id, message_id, worker_id, command, message, stacktrace, tele_userid, tele_chatid, caller_name, caller_filepath, caller_linenumber, exception_json) 
                            VALUES 
                            (@date, @type, @user_id, @queue_id, @message_id, @worker_id, @command, @message, @stacktrace, @tele_userid, @tele_chatid, @caller_name, @caller_filepath, @caller_linenumber, @exception_json)";

                        cmd.Parameters.AddWithValue("@date", DateTime.Now);
                        cmd.Parameters.AddWithValue("@type", level.ToString());
                        cmd.Parameters.AddWithValue("@user_id", context.User?.UID);
                        cmd.Parameters.AddWithValue("@queue_id", context.Queue?.ID);
                        cmd.Parameters.AddWithValue("@message_id", context.Message?.ID);
                        cmd.Parameters.AddWithValue("@worker_id", context.Worker?.ID);
                        cmd.Parameters.AddWithValue("@command", context.Command);
                        cmd.Parameters.AddWithValue("@message", message);
                        cmd.Parameters.AddWithValue("@stacktrace", ex?.StackTrace);
                        cmd.Parameters.AddWithValue("@tele_userid", context.Telegram?.User.ID);
                        cmd.Parameters.AddWithValue("@tele_chatid", context.Telegram?.Chat?.ID);
                        cmd.Parameters.AddWithValue("@caller_name", callerName);
                        cmd.Parameters.AddWithValue("@caller_filepath", callerFilePath);
                        cmd.Parameters.AddWithValue("@caller_linenumber", lineNumber);
                        cmd.Parameters.AddWithValue("@exception_json", ex is null ? null : SerializeExceptionToJson(ex));


                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception e)
            {
                FoxLog.LogToFile($"Error logging exception to database.  Error: {e.Message}\r\n{e.StackTrace}\r\n");
            }
        }

        public static void LogException(Exception ex, string? customMessage = null, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            var context = FoxContextManager.Current; // Retrieve the current context

            var message = customMessage ?? ex.Message;

            FoxLog.LogToFile($"Error: {message}\r\n{ex.StackTrace}\r\n");

            _= FoxLog.LogToDatabase(message, LogLevel.ERROR, ex, callerName, callerFilePath, lineNumber);

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
            _ = FoxLog.LogToDatabase(message, level, null, callerName, callerFilePath, lineNumber);

            Write(message + "\r\n", level, callerName, callerFilePath, lineNumber);
        }

        // Logging function
        public static void Write(string message, LogLevel level = LogLevel.INFO, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            FoxLog.LogToFile(message, level, callerName, callerFilePath, lineNumber);
        }
    }
}
