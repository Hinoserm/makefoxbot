using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public enum LogLevel
{
    DEBUG,
    PEDANTIC,
    INFO,
    WARNING,
    ERROR
}

namespace makefoxsrv
{
    internal class FoxLog
    {
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.INFO;

        public static void WriteLine(string message, LogLevel level = LogLevel.INFO, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            Write(message + "\r\n", level, callerName, callerFilePath, lineNumber);
        }

        // Logging function
        public static void Write(string message, LogLevel level = LogLevel.INFO, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
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

                File.AppendAllLines("../logs/output.txt", lines.Select(line => $"{DateTime.Now.ToString(dateFormat)} {level}> {line}").ToArray());
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error writing log: {e.Message}");
            }

            // Check if the current log message level is greater than or equal to the application's log level
            if (level >= CurrentLogLevel)
            {
                // Log the message with a timestamp and the level
                //FoxLog.WriteLine();
                //FoxUI.AppendLog($"{DateTime.Now} [{level}]: {message}");
                FoxUI.AppendLog(message);
            }
        }
    }
}
