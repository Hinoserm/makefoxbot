using Autofocus;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

using Config.Net;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WTelegram;
using makefoxsrv;
using TL;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;
using System.Runtime.Intrinsics.Arm;
using Castle.Core.Smtp;
using System.Timers;
using Stripe;
using System.ComponentModel;

public interface IMySettings
{
    [Option(Alias = "Telegram.BOT_TOKEN")]
    string TelegramBotToken { get; }

    [Option(Alias = "Telegram.BOT_USERNAME")]
    string TelegramBotUsername { get; }

    [Option(Alias = "Telegram.PAYMENT_TOKEN")]
    string TelegramPaymentToken { get; }

    [Option(Alias = "Telegram.API_URL")]
    string TelegramApiUrl { get; }

    [Option(Alias = "Telegram.API_ID")]
    int? TelegramApiId { get; }

    [Option(Alias = "Telegram.API_HASH")]
    string TelegramApiHash { get; }

    [Option(Alias = "MySQL.USERNAME")]
    string MySQLUsername { get; }
    [Option(Alias = "MySQL.PASSWORD")]
    string MySQLPassword { get; }
    [Option(Alias = "MySQL.SERVER")]
    string MySQLServer { get; }
    [Option(Alias = "MySQL.DATABASE")]
    string MySQLDatabase { get; }



    [Option(Alias = "Stripe.PRIVATE_KEY")]
    string StripePrivateKey { get; }

    [Option(Alias = "PayPal.CLIENT_ID")]
    string PayPalClientId { get; }

    [Option(Alias = "PayPal.SECRET_KEY")]
    string PayPalSecretKey { get; }

    [Option(Alias = "PayPal.SANDBOX", DefaultValue = true)]
    bool PayPalSandboxMode { get; }

    [Option(Alias = "Web.LOCAL_PORT", DefaultValue = 5555)]
    int WebLocalPort { get; }

    [Option(Alias = "Web.WEBROOT_URL")]
    string WebRootUrl { get; }

    [Option(Alias = "Web.WEBSOCKET_URL")]
    string WebSocketUrl { get; }
}

public static class TimeSpanExtensions
{
    public static string ToPrettyFormat(this TimeSpan span)
    {
        if (span == TimeSpan.Zero) return "0 seconds";

        var sb = new StringBuilder();
        if (span.Days > 0)
            sb.AppendFormat("{0} day{1} ", span.Days, span.Days > 1 ? "s" : string.Empty);
        if (span.Hours > 0)
            sb.AppendFormat("{0} hour{1} ", span.Hours, span.Hours > 1 ? "s" : string.Empty);
        if (span.Minutes > 0)
            sb.AppendFormat("{0} minute{1} ", span.Minutes, span.Minutes > 1 ? "s" : string.Empty);
        if (span.TotalSeconds > 0)
        {
            // Use TotalSeconds for more precision and format it to show up to two decimal places
            double seconds = span.TotalSeconds % 60; // Use modulo 60 to get only the remainder of seconds
            sb.AppendFormat("{0:0.##} second{1} ", seconds, seconds != 1 ? "s" : string.Empty);
        }

        return sb.ToString().Trim();
    }

    public static string ToShortPrettyFormat(this TimeSpan span)
    {
        if (span == TimeSpan.Zero) return "0s";

        var sb = new StringBuilder();
        if (span.Days > 0)
            return $"{span.Days}d";
        if (span.Hours > 0)
            return $"{span.Hours}h";
        if (span.Minutes > 0)
            return $"{span.Minutes}m";
        //if (span.TotalSeconds > 0)
        //{
            // Use TotalSeconds for more precision and format it to show up to two decimal places
            //double seconds = span.TotalSeconds % 60; // Use modulo 60 to get only the remainder of seconds
            return $"{span.Seconds}s";
        //}
    }
}

namespace makefoxsrv
{
    public static class StringExtensions
    {
        public static string Left(this string str, int length)
        {
            return str.Substring(0, Math.Min(length, str.Length));
        }

        public static string Right(this string str, int length)
        {
            return str.Substring(str.Length - Math.Min(length, str.Length));
        }
    }

    internal class FoxMain
    {
        public static IMySettings? settings = new ConfigurationBuilder<IMySettings>()
        .UseIniFile("../conf/settings.ini")
        .Build();

        public static string sqlConnectionString =
            $"Server={settings.MySQLServer};" +
            $"User ID={settings.MySQLUsername};" +
            $"Password={settings.MySQLPassword};" +
            $"Database={settings.MySQLDatabase};" +
            $"charset=utf8mb4;" +
            $"keepalive=60;" +
            $"pooling=true;" +
            $"minpoolsize=10;" +
            $"maxpoolsize=150;" +
            $"default command timeout=180;";

        public static CancellationToken sqlCancellationToken = new();

        public static DateTime startTime = DateTime.Now;

        static string sha1hash(byte[] input)
        {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(input));
        }

        static void LoadSettings(string filename = "conf/settings.ini")
        {
            if (settings is null)
                throw new Exception($"Settings file {filename} could not be loaded.");

            if (string.IsNullOrEmpty(settings.TelegramBotToken))
                throw new Exception("Missing setting Telegram.bot_token is not optional.");

            if (string.IsNullOrEmpty(settings.TelegramBotUsername))
                throw new Exception("Missing setting Telegram.bot_username is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLUsername))
                throw new Exception("Missing setting MySQL.username is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLPassword))
                throw new Exception("Missing setting MySQL.password is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLServer))
                throw new Exception("Missing setting MySQL.server is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLDatabase))
                throw new Exception("Missing setting MySQL.database is not optional.");

        }

        public static string GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
            return attribute?.InformationalVersion; // This will include the version and Git revision
        }

        static async Task Main(string[] args)
        {
            using CancellationTokenSource cts = new();

            sqlCancellationToken = cts.Token;

            FoxContextManager.Current = new FoxContext();

            //Console.BufferHeight = Int16.MaxValue - 1;

            //FoxUI.Init(); //Must be the first thing we do, before printing to the console.

            FoxLog.WriteLine($"Hello, World!  Version {GetVersion()}");

            string currentDirectory = Directory.GetCurrentDirectory();
            FoxLog.WriteLine($"Current Working Directory: {currentDirectory}");

            FoxLog.WriteLine("Loading configuration...");
            try
            {
                LoadSettings();

                if (settings is null)
                    throw new Exception("Unable to load settings.ini");

                // Check for required Telegram settings
                if (settings.TelegramApiId is null || settings.TelegramApiId == 0)
                    throw new Exception("Required Telegram.API_ID setting not set.");
                if (String.IsNullOrEmpty(settings.TelegramApiHash))
                    throw new Exception("Required Telegram.API_HASH setting not set.");
                if (String.IsNullOrEmpty(settings.TelegramBotToken))
                    throw new Exception("Required Telegram.BOT_TOKEN setting not set.");

                // Check for required MySQL settings
                if (String.IsNullOrEmpty(settings.MySQLUsername))
                    throw new Exception("Required MySQL.USERNAME setting not set.");
                if (String.IsNullOrEmpty(settings.MySQLPassword))
                    throw new Exception("Required MySQL.PASSWORD setting not set.");
                if (String.IsNullOrEmpty(settings.MySQLServer))
                    throw new Exception("Required MySQL.SERVER setting not set.");
                if (String.IsNullOrEmpty(settings.MySQLDatabase))
                    throw new Exception("Required MySQL.DATABASE setting not set.");

                // Check for web-related settings
                if (String.IsNullOrEmpty(settings.WebRootUrl))
                    FoxLog.WriteLine(GetVersion() + "Web.WEBROOT_URL setting not set.  Web services may not be available.");
                if (String.IsNullOrEmpty(settings.WebSocketUrl))
                    FoxLog.WriteLine(GetVersion() + "Web.WEBSOCKET_URL setting not set.  Web services may not be available.");

                // Check for optional payment settings
                if (settings.StripePrivateKey is not null)
                    StripeConfiguration.ApiKey = settings.StripePrivateKey;
                else
                    FoxLog.WriteLine("Stripe private key not set.  Stripe payments will be disabled.");

                if (settings.PayPalClientId is null || settings.PayPalSecretKey is null)
                    FoxLog.WriteLine("PayPal credentials not set.  PayPal payments will be disabled.");

                if (settings.PayPalSandboxMode)
                    FoxLog.WriteLine("!!! PayPal is running in sandbox mode.");
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("error: " + ex.Message);

                return;
            }
            FoxLog.WriteLine("Done loading configuration.");

            FoxLog.Write("Connecting to database... ");
            try
            {
                MySqlConnection sql = new MySqlConnection(FoxMain.sqlConnectionString);
                await sql.OpenAsync(cts.Token);
                await sql.PingAsync(cts.Token);
                await sql.DisposeAsync();
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("error: " + ex.Message);
                return;
            }
            FoxLog.WriteLine("done.");

            FoxLog.StartLoggingThread();

            try
            {
                await FoxSettings.LoadSettingsAsync();
            } catch (Exception ex) {
                FoxLog.WriteLine("Error: " + ex.Message);
                return;
            }

            Console.CancelKeyPress += (sender, e) =>
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    // If we hit it again, be more aggressive.
                    Environment.Exit(1);
                };

                // Initialize an empty FoxContext so logging can work
                FoxContextManager.Current = new FoxContext();

                FoxLog.WriteLine("CTRL+C detected. Initiating shutdown...");
                // Signal the cancellation token
                cts.Cancel();

                // Prevent the application from terminating immediately,
                // allowing cleanup code to run
                e.Cancel = true;
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                // Initialize an empty FoxContext so logging can work
                FoxContextManager.Current = new FoxContext();

                if (!cts.IsCancellationRequested)
                {
                    FoxLog.WriteLine("System termination detected. Initiating shutdown...");

                    cts.Cancel();
                }
            };

            //FoxWorker.OnTaskProgress += (sender, e) =>
            //{
            //    var w = (FoxWorker)sender!;

            //    FoxLog.WriteLine($"Worker {w.name} progress: {e.Percent}%");
            //};

            //_ = Task.Run(async () =>
            //{
            try
            {
                FoxWeb.StartWebServer(cancellationToken: cts.Token);

                //Load workers BEFORE processing input from telegram.
                //This is important in order to handle queued messages properly, otherwise users will be told the workers are offline.
                await FoxWorker.LoadWorkers(cts.Token);

                await FoxTelegram.Connect(settings.TelegramApiId.Value, settings.TelegramApiHash, settings.TelegramBotToken, "../conf/telegram.session");

                //await Task.Delay(1000); //Wait a bit for telegram to settle.

                await FoxWorker.StartWorkers();

                await FoxQueue.EnqueueOldItems();

                FoxCron.Start(cts.Token);

                FoxQueue.StartTaskLoop(cts.Token);
            }
            catch (Exception ex)
            {
                cts.Cancel();

                FoxLog.WriteLine($"Error: {ex.Message}\r\n{ex.StackTrace}");
            }
            //});

            //FoxUI.Start(cts);

            try
            {
                //Wait forever, or until cancellation is requested.
                await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
            }
            catch (TaskCanceledException)
            {
                //Don't need to do anything special here.
            }
            catch (Exception ex)
            {
                //If, SOMEHOW, we get here, we need to cancel the cancellation token and shut down.
                FoxLog.WriteLine($"Unexpected error in main function: {ex.Message}\r\n{ex.StackTrace}", LogLevel.ERROR);
                cts.Cancel();
            }

            FoxWeb.StopWebServer();
            FoxTelegram.StopUpdates();
            await FoxWorker.StopWorkers();
            FoxCron.Stop();

            var shutdownStart = DateTime.Now;
            var shutdownTimeout = TimeSpan.FromSeconds(3); // Adjust as needed

            while (!FoxWorker.GetWorkers().IsEmpty && DateTime.Now - shutdownStart < shutdownTimeout)
            {
                // Wait for all workers to complete, with timeout
                // This gives them a chance to finish storing their state if they were in the middle of a task.
                await Task.Delay(100); 
            }

            await FoxTelegram.Disconnect();

            await FoxLog.StopLoggingThreadAsync();
        }
    }
}
