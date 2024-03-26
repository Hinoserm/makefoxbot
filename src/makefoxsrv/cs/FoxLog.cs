using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public enum LogLevel
{
    LOG_DEBUG,
    LOG_PEDANTIC,
    LOG_INFO,
    LOG_WARNING,
    LOG_ERROR
}

namespace makefoxsrv
{
    internal class FoxLog
    {
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.LOG_INFO;

        public static void WriteLine(string message, LogLevel level = LogLevel.LOG_INFO)
        {
            Write(message + "\r\n", level);
        }

        // Logging function
        public static void Write(string message, LogLevel level = LogLevel.LOG_INFO)
        {
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
