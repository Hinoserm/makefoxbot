using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public enum LogLevel
{
    LOG_DEBUG,
    LOG_INFO,
    LOG_WARNING,
    LOG_ERROR
}

namespace makefoxsrv.cs
{
    internal class FoxLog
    {
        public static LogLevel CurrentLogLevel { get; set; } = LogLevel.LOG_INFO;

        // Logging function
        public static void Log(LogLevel level, string message)
        {
            // Check if the current log message level is greater than or equal to the application's log level
            if (level >= CurrentLogLevel)
            {
                // Log the message with a timestamp and the level
                Console.WriteLine($"{DateTime.Now} [{level}]: {message}");
            }
        }
    }
}
