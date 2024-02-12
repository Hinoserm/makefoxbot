using Autofocus;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

using Config.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

public interface IMySettings
{
    [Option(Alias = "Telegram.BOT_TOKEN")]
    string TelegramBotToken { get; }

    [Option(Alias = "Telegram.API_URL")]
    string TelegramApiUrl { get; }

    [Option(Alias = "MySQL.USERNAME")]
    string MySQLUsername { get; }
    [Option(Alias = "MySQL.PASSWORD")]
    string MySQLPassword { get; }
    [Option(Alias = "MySQL.SERVER")]
    string MySQLServer { get; }
    [Option(Alias = "MySQL.DATABASE")]
    string MySQLDatabase { get; }
}

public static class TimeSpanExtensions
{
    public static string ToPrettyFormat(this TimeSpan span)
    {

        if (span == TimeSpan.Zero) return "0 minutes";

        var sb = new StringBuilder();
        if (span.Days > 0)
            sb.AppendFormat("{0} day{1} ", span.Days, span.Days > 1 ? "s" : String.Empty);
        if (span.Hours > 0)
            sb.AppendFormat("{0} hour{1} ", span.Hours, span.Hours > 1 ? "s" : String.Empty);
        if (span.Minutes > 0)
            sb.AppendFormat("{0} minute{1} ", span.Minutes, span.Minutes > 1 ? "s" : String.Empty);
        if (span.Seconds > 0)
            sb.AppendFormat("{0} second{1} ", span.Seconds, span.Seconds > 1 ? "s" : String.Empty);

        return sb.ToString().Trim();

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

        public static string MySqlConnectionString = $"Server={settings.MySQLServer};User ID={settings.MySQLUsername};Password={settings.MySQLPassword};Database={settings.MySQLDatabase};charset=utf8mb4;keepalive=60;minpoolsize=2";
        //public static MySqlConnection? SQL;

        static string sha1hash(byte[] input)
        {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(input));
        }

        public static User me;

        static async Task updateTelegramUsers(User? user)
        {
            if (user is null)
                return;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "REPLACE INTO telegram_users (id, username, firstname, lastname, is_premium, date_updated) VALUES (@id, @username, @firstname, @lastname, @is_premium, CURRENT_TIMESTAMP())";
                    cmd.Parameters.AddWithValue("id", user.Id);
                    cmd.Parameters.AddWithValue("username", user.Username);
                    cmd.Parameters.AddWithValue("firstname", user.FirstName);
                    cmd.Parameters.AddWithValue("lastname", user.LastName);
                    cmd.Parameters.AddWithValue("is_premium", user.IsPremium);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        static async Task updateTelegramChats(Chat? chat)
        {
            if (chat is null)
                return;

            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand())
                {
                    cmd.Connection = SQL;
                    cmd.CommandText = "REPLACE INTO telegram_chats (id, username, firstname, lastname, title, description, bio, type, date_updated) VALUES (@id, @username, @firstname, @lastname, @title, @description, @bio, @type, CURRENT_TIMESTAMP())";
                    cmd.Parameters.AddWithValue("id", chat.Id);
                    cmd.Parameters.AddWithValue("username", chat.Username);
                    cmd.Parameters.AddWithValue("firstname", chat.FirstName);
                    cmd.Parameters.AddWithValue("lastname", chat.LastName);
                    cmd.Parameters.AddWithValue("title", chat.Title);
                    cmd.Parameters.AddWithValue("description", chat.Description);
                    cmd.Parameters.AddWithValue("bio", chat.Bio);
                    cmd.Parameters.AddWithValue("type", chat.Type.ToString().ToUpper());

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        static async Task RunHandlerThread(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (botClient is null )
                throw new ArgumentNullException(nameof(botClient));

            if (update is null)
                throw new ArgumentNullException(nameof(update));
            
            if (update.Message is not null)
            {
                try
                {
                    var message = update.Message;

                    if (message is null || message.From is null)
                        throw new ArgumentNullException();

                    if (message.From.Id != message.Chat.Id)
                        _ = updateTelegramChats(message.Chat); //Skip the 'await' since we don't care about the results.
                    _ = updateTelegramUsers(message.From);     //Change this later if this function starts caring about these tables.

                    if (message.Type == MessageType.Photo)
                    {

                        try
                        {

                            Console.WriteLine($"Got a photo from {message.From.Username} ({message.From.Id})!");

                            var user = await FoxUser.GetByTelegramUser(message.From);

                            if (user is not null)
                            {

                                await user.UpdateTimestamps();

                                var photo = message.Photo.Last();

                                if (photo is null)
                                    throw new Exception("Unexpected null photo object received from Telegram");

                                var img = await FoxImage.LoadFromTelegramUniqueId(user.UID, photo.FileUniqueId, message.Chat.Id);

                                if (img is not null)
                                {
                                    Console.WriteLine("Image found by Telegram unique ID.  ID: " + img.ID);
                                    if (message.Chat.Id != img.TelegramChatID)
                                    {
                                        var newimg = await FoxImage.Create(user.UID, img.Image, FoxImage.ImageType.INPUT, img.Filename, img.TelegramFileID, img.TelegramUniqueID, message.Chat.Id, message.MessageId);

                                        img = newimg;
                                    }
                                }
                                else
                                {
                                    if (botClient.LocalBotServer)
                                    {
                                        var file = await botClient.GetFileAsync(photo.FileId);

                                        img = await FoxImage.Create(user.UID, System.IO.File.ReadAllBytes(file.FilePath), FoxImage.ImageType.INPUT, file.FilePath, file.FileId, file.FileUniqueId, message.Chat.Id, message.MessageId);

                                        System.IO.File.Delete(file.FilePath);
                                    }
                                    else
                                    {
                                        using var imgStream = new MemoryStream();

                                        var file = await botClient.GetInfoAndDownloadFileAsync(photo.FileId, imgStream);

                                        img = await FoxImage.Create(user.UID, imgStream.ToArray(), FoxImage.ImageType.INPUT, file.FilePath, file.FileId, file.FileUniqueId, message.Chat.Id, message.MessageId);

                                    }

                                    Console.WriteLine("Image saved.  ID: " + img.ID);
                                }

                                if (message.From.Id == message.Chat.Id) //Only save & notify outside of groups.
                                {
                                    var settings = await FoxUserSettings.GetTelegramSettings(user, message.From, message.Chat);

                                    settings.selected_image = img.ID;

                                    await settings.Save();

                                    Message waitMsg = await botClient.SendTextMessageAsync(
                                        chatId: message.Chat.Id,
                                        text: "✅ Image saved and selected as input for /img2img",
                                        replyToMessageId: update.Message.MessageId,
                                        cancellationToken: cancellationToken
                                    );
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error with input image: " + ex.Message);
                        }
                    }



                    // Only process text messages
                    if (message.Text is { } messageText)
                    {
                        var chatId = message.Chat.Id;

                        await FoxCommandHandler.HandleCommand(botClient, message, cancellationToken);

                        try
                        {
                            using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
                            {
                                await SQL.OpenAsync();

                                using (var cmd = new MySqlCommand())
                                {
                                    cmd.Connection = SQL;
                                    cmd.CommandText = "INSERT INTO telegram_log (user_id, chat_id, message_text, date_added) VALUES (@tele_id, @tele_chatid, @message, CURRENT_TIMESTAMP())";
                                    cmd.Parameters.AddWithValue("tele_id", message.From.Id);
                                    cmd.Parameters.AddWithValue("tele_chatid", message.Chat.Id);
                                    cmd.Parameters.AddWithValue("message", messageText);

                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("telegram_log error: " + ex.Message);
                        }

                        Console.WriteLine($"Received a '{messageText}' message in chat {chatId} from {message.From.Username}.");
                    }
                }
                catch (Exception ex)
                {
                    Message waitMsg = await botClient.SendTextMessageAsync(
                        chatId: update.Message.Chat.Id,
                        text: "❌ Error! \"" + ex.Message + "\"",
                        replyToMessageId: update.Message.MessageId,
                        cancellationToken: cancellationToken
                    );
                    Console.WriteLine("Error processing: " + ex.Message);
                }
            } else if (update.CallbackQuery is not null) {
                try
                {
                    var user = await FoxUser.GetByTelegramUser(update.CallbackQuery.From);

                    Console.WriteLine("Callback! " + update.CallbackQuery.Data);

                    if (user is not null)
                        await FoxCommandHandler.HandleCallback(botClient, update, cancellationToken, user);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error processing callback: " + ex.Message);
                }
            }
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Only process Message updates: https://core.telegram.org/bots/api#message
            if (update.Message is null && update.CallbackQuery is null)
            {
                Console.WriteLine("Unexpected type of update received from Telegram: " + update.Type);
                return;
            }
            //Console.WriteLine("Update Type: " + update.Type);

            _ = Task.Run(() => RunHandlerThread(botClient, update, cancellationToken), cancellationToken);
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        static void LoadSettings(string filename = "settings.ini")
        {
            if (settings is null)
                throw new Exception($"Settings file {filename} could not be loaded.");

            if (string.IsNullOrEmpty(settings.TelegramBotToken))
                throw new Exception("Missing setting Telegram.bot_token is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLUsername))
                throw new Exception("Missing setting MySQL.username is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLPassword))
                throw new Exception("Missing setting MySQL.password is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLServer))
                throw new Exception("Missing setting MySQL.server is not optional.");

            if (string.IsNullOrEmpty(settings.MySQLDatabase))
                throw new Exception("Missing setting MySQL.database is not optional.");

        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            string currentDirectory = Directory.GetCurrentDirectory();
            Console.WriteLine($"Current Working Directory: {currentDirectory}");

            Console.Write("Loading configuration... ");
            try {
                LoadSettings();
            } catch (Exception ex) {
                Console.WriteLine("error: " + ex.Message);

                return;
            }
            Console.WriteLine("done.");

            MySqlConnection sql;


            Console.Write("Connecting to database... ");
            try
            {

                sql = new MySqlConnection(FoxMain.MySqlConnectionString);
                await sql.OpenAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("error: " + ex.Message);
                return;
            }
            Console.WriteLine("done.");

            //var botClient = new TelegramBotClient("6970653264:AAFG_Ohd04pVLKXJHzPCzsXH3OPPTxs8TqA");

            if (settings.TelegramBotToken is null)
                throw new Exception("BOT_TOKEN setting not set.");

            var teleOptions = new TelegramBotClientOptions(
                token: settings.TelegramBotToken,
                baseUrl: settings.TelegramApiUrl //Might be null, and that's okay.
            );

            var botClient = new TelegramBotClient(teleOptions);

            using CancellationTokenSource cts = new();

            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = Array.Empty<UpdateType>() // receive all update types except ChatMember related updates
            };

            FoxMain.me = await botClient.GetMeAsync();


            //Start workers BEFORE processing input from telegram.
            await FoxWorker.StartWorkers(botClient);

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            await botClient.SetMyCommandsAsync(FoxCommandHandler.GenerateTelegramBotCommands());

            //_ = Task.Run(() => RunWorkerThread(botClient, "http://10.0.2.30:7860/"));
            //_ = Task.Run(() => RunWorkerThread(botClient, "http://10.0.2.30:7861/"));
            //_ = Task.Run(() => RunWorkerThread(botClient, "http://10.0.2.2:7860/"));

            


            using (var cmd = new MySqlCommand($"UPDATE queue SET status = 'PENDING' WHERE status = 'PROCESSING'", sql))
            {
                long stuck_count = await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"Unstuck {stuck_count} queue items.");
            }

            Console.WriteLine($"Start listening for @{me.Username}");
            Console.WriteLine($"Bot ID: {me.Id}");

            _ = FoxQueue.NotifyUserPositions(botClient, cts);


            Console.ReadLine();

            //await botClient.LogOutAsync();

            // Send cancellation request to stop bot
            cts.Cancel();


        }
    }
}
