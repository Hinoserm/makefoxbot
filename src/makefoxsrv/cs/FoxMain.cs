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


public interface IMySettings
{
    [Option(Alias = "Telegram.BOT_TOKEN")]
    string TelegramBotToken { get; }

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

        public static string MySqlConnectionString = $"Server={settings.MySQLServer};User ID={settings.MySQLUsername};Password={settings.MySQLPassword};Database={settings.MySQLDatabase};charset=utf8mb4;keepalive=60;minpoolsize=5;default command timeout=180";
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
                    cmd.CommandText = "REPLACE INTO telegram_users (id, access_hash, username, firstname, lastname, date_updated) VALUES (@id, @access_hash, @username, @firstname, @lastname, @now)";
                    cmd.Parameters.AddWithValue("id", user.ID);
                    cmd.Parameters.AddWithValue("access_hash", user.access_hash);
                    cmd.Parameters.AddWithValue("username", user.username);
                    cmd.Parameters.AddWithValue("firstname", user.first_name);
                    cmd.Parameters.AddWithValue("lastname", user.last_name);
                    //cmd.Parameters.AddWithValue("is_premium", user.IsPremium);
                    cmd.Parameters.AddWithValue("now", DateTime.Now);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        static async Task updateTelegramChats(ChatBase? chat)
        {
            try {
                if (chat is null)
                    return;

                using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
                {
                    await SQL.OpenAsync();


                    using (var cmd = new MySqlCommand())
                    {
                        long? adminFlags = null;

                        TL.Channel group = (TL.Channel)chat;
                        var admin = group.admin_rights;
                        if (admin is not null)
                            adminFlags = ((long)admin.flags);

                        var groupType = "GROUP";

                        if (chat.IsChannel)
                        {
                            groupType = "CHANNEL";
                        }
                        else if (chat.IsGroup)
                        {
                            groupType = "GROUP";
                            if (group.flags.HasFlag(TL.Channel.Flags.megagroup))
                            {
                                groupType = "SUPERGROUP";
                            }
                            else if (group.flags.HasFlag(TL.Channel.Flags.gigagroup))
                            {
                                groupType = "GIGAGROUP";
                            }
                        }

                        cmd.Connection = SQL;
                        cmd.CommandText = "REPLACE INTO telegram_chats (id, access_hash, active, username, title, type, admin_flags, participants, date_updated) VALUES (@id, @access_hash, @active, @username, @title, @type, @admin_flags, @participants, @now)";
                        cmd.Parameters.AddWithValue("id", chat.ID);
                        cmd.Parameters.AddWithValue("access_hash", group.access_hash);
                        cmd.Parameters.AddWithValue("active", group.IsActive);
                        cmd.Parameters.AddWithValue("username", group.MainUsername);
                        //cmd.Parameters.AddWithValue("firstname", chat.f);
                        //cmd.Parameters.AddWithValue("lastname", chat.LastName);
                        cmd.Parameters.AddWithValue("title", chat.Title);
                        //cmd.Parameters.AddWithValue("description", chat.Description);
                        //cmd.Parameters.AddWithValue("bio", chat.Bio);
                        cmd.Parameters.AddWithValue("type", groupType);
                        cmd.Parameters.AddWithValue("admin_flags", adminFlags);
                        cmd.Parameters.AddWithValue("participants", group.participants_count);
                        cmd.Parameters.AddWithValue("now", DateTime.Now);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("updateTelegramChats error: " + ex.Message);
            }
    }

        static async Task HandlePayment(FoxTelegram t, MessageService ms, MessageActionPaymentSentMe payment)
        {
            var user = await FoxUser.GetByTelegramUser(t.User, true);

            if (user is not null)
            {
                
                string payload = System.Text.Encoding.ASCII.GetString(payment.payload);
                string[] parts = payload.Split('_');
                if (parts.Length != 3 || parts[0] != "PAY" || !long.TryParse(parts[1], out long recvUID) || !int.TryParse(parts[2], out int days))
                {
                    throw new System.Exception("Malformed payment request!  Contact /support");
                }

                var recvUser = await FoxUser.GetByUID(recvUID);

                if (recvUser is null)
                    throw new System.Exception("Unknown UID in payment request!  Contact /support");

                await recvUser.recordPayment((int)payment.total_amount, payment.currency, days, payload, payment.charge.id, payment.charge.provider_charge_id);
                FoxLog.WriteLine($"Payment recorded for user {recvUID} by {user.UID}: ({payment.total_amount}, {payment.currency}, {days}, {payload}, {payment.charge.id}, {payment.charge.provider_charge_id})");

                var msg = @$"
<b>Thank You for Your Generous Support!</b>

We are deeply grateful for your donation, which is vital for our platform's sustainability and growth.

Your contribution has granted you <b>{(days == -1 ? "lifetime" : $"{days} days of")} premium access</b>, enhancing your experience with increased limits and features.

We are committed to using your donation to further develop and maintain the service, supporting our mission to provide a creative and expansive platform for our users. Thank you for being an integral part of our journey and for empowering us to continue offering a high-quality service.
";
                var entities = t.botClient.HtmlToEntities(ref msg);

                await t.SendMessageAsync(
                            text: msg,
                            replyToMessageId: ms.id,
                            entities: entities,
                            disableWebPagePreview: true
                        );

                //await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
            }

                //    }
                //}
                //catch (Exception ex)
                //{
                //    Message waitMsg = await botClient.SendTextMessageAsync(
                //        chatId: update.Message.Chat.Id,
                //        text: "❌ Error! \"" + ex.Message + "\"",
                //        replyToMessageId: update.Message.MessageId,
                //        cancellationToken: cancellationToken
                //    );
                //    FoxLog.WriteLine("Error processing: " + ex.Message);
                //}
        }

        static async Task HandleMessageAsync(FoxTelegram t, Message msg)
        {
            // Only process text messages

            FoxLog.WriteLine($"{msg.from_id} in {msg.peer_id}> {msg.message}");

            if (msg.message is not null)
            {
                _ = FoxCommandHandler.HandleCommand(t, msg);

                try
                {
                    using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
                    {
                        await SQL.OpenAsync();

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.CommandText = "INSERT INTO telegram_log (user_id, chat_id, message_id, message_text, date_added) VALUES (@tele_id, @tele_chatid, @message_id, @message, @now)";
                            cmd.Parameters.AddWithValue("tele_id", t.User.ID);
                            cmd.Parameters.AddWithValue("tele_chatid", t.Chat is not null ? t.Chat.ID : null);
                            cmd.Parameters.AddWithValue("message_id", msg.ID);
                            cmd.Parameters.AddWithValue("message", msg.message);
                            cmd.Parameters.AddWithValue("now", DateTime.Now);

                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine("telegram_log error: " + ex.Message);
                }
            }
        }

        static async Task HandleUpdateAsync(WTelegram.Client botClient, UpdatesBase updates, CancellationTokenSource cts)
        {
            updates.CollectUsersChats(FoxTelegram.Users, FoxTelegram.Chats);

            foreach (KeyValuePair<long, User> item in updates.Users)
            {
                await updateTelegramUsers(item.Value);
            }

            foreach (KeyValuePair<long, ChatBase> item in updates.Chats)
            {
                await updateTelegramChats(item.Value);
            }

            foreach (var update in updates.UpdateList)
            {
                try
                {
                    User? user = null;
                    ChatBase? chat = null;
                    InputPeer? peer = null;
                    FoxTelegram? t = null;

                    //FoxLog.WriteLine("Update type from Telegram: " + update.GetType().Name);

                    switch (update)
                    {
                        case UpdateNewMessage unm:
                            switch (unm.message)
                            {
                                case Message m:
                                    updates.Users.TryGetValue(m.from_id ?? m.peer_id, out user);
                                    updates.Chats.TryGetValue(m.peer_id, out chat);

                                    if (user is null)
                                        throw new Exception("Invalid telegram user");

                                    t = new FoxTelegram(botClient, user, chat);

                                    if (m.media is MessageMediaPhoto { photo: Photo photo })
                                    {
                                        _= FoxImage.SaveImageFromTelegram(t, m, photo);
                                    }
                                    else
                                    {
                                        _= HandleMessageAsync(t, m);
                                    }

                                    break;
                                case MessageService ms:
                                    switch (ms.action)
                                    {
                                        case MessageActionPaymentSentMe payment:

                                            updates.Users.TryGetValue(ms.from_id ?? ms.peer_id, out user);
                                            updates.Chats.TryGetValue(ms.peer_id, out chat);

                                            if (user is null)
                                                throw new Exception("Invalid telegram user");

                                            t = new FoxTelegram(botClient, user, chat);

                                            await HandlePayment(t, ms, payment);
                                            break;
                                        default:
                                            FoxLog.WriteLine("Unexpected service message type: " + ms.action.GetType().Name);
                                            break;
                                    }
                                    break;
                                default:
                                    FoxLog.WriteLine("Unexpected message type: " + unm.GetType().Name);
                                    break;
                            }

                            break;
                        case UpdateDeleteChannelMessages udcm:
                            FoxLog.WriteLine("Deleted chat messages " + udcm.messages.Length);
                            break;
                        case UpdateDeleteMessages udm:
                            FoxLog.WriteLine("Deleted messages " + udm.messages.Length);
                            break;

                        case UpdateBotCallbackQuery ucbk:
                            updates.Users.TryGetValue(ucbk.user_id, out user);

                            if (user is null)
                                throw new Exception("Invalid telegram user");

                            var p = updates.UserOrChat(ucbk.peer);

                            if (p is ChatBase c)
                                chat = c;

                            t = new FoxTelegram(botClient, user, chat);
                            
                            FoxLog.WriteLine($"Callback: {user} in {chat}> {System.Text.Encoding.ASCII.GetString(ucbk.data)}");

                            _= FoxCallbacks.Handle(t, ucbk, System.Text.Encoding.ASCII.GetString(ucbk.data));

                            break;
                        case UpdateBotPrecheckoutQuery upck:
                            await botClient.Messages_SetBotPrecheckoutResults(upck.query_id, null, true);
                            break;
                        default:
                            FoxLog.WriteLine("Unexpected update type from Telegram: " + update.GetType().Name);
                            break; // there are much more update types than the above example cases
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine("Error in HandleUpdateAsync: " + ex.Message);
                }
            }
        }

        //static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        //{
        //    var ErrorMessage = exception switch
        //    {
        //        ApiRequestException apiRequestException
        //            => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
        //        _ => exception.ToString()
        //    };
        //
        //    FoxLog.WriteLine("Error: " + ErrorMessage);
        //    return Task.CompletedTask;
        //}

        static void LoadSettings(string filename = "conf/settings.ini")
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

        public static string GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
            return attribute?.InformationalVersion; // This will include the version and Git revision
        }

        public static WTelegram.Client? botClient;

        static async Task Main(string[] args)
        {
            using CancellationTokenSource cts = new();

            try
            {
                FoxUI.Start(cts);
            }
            catch
            {
                throw;
            }

            FoxLog.WriteLine($"Hello, World!  Version {GetVersion()}");

            string currentDirectory = Directory.GetCurrentDirectory();
            FoxLog.WriteLine($"Current Working Directory: {currentDirectory}");

            FoxLog.Write("Loading configuration... ");
            try {
                LoadSettings();
            } catch (Exception ex) {
                FoxLog.WriteLine("error: " + ex.Message);

                return;
            }
            FoxLog.WriteLine("done.");

            MySqlConnection sql;

            FoxLog.Write("Connecting to database... ");
            try
            {

                sql = new MySqlConnection(FoxMain.MySqlConnectionString);
                await sql.OpenAsync();
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("error: " + ex.Message);
                return;
            }
            FoxLog.WriteLine("done.");

            await FoxSettings.LoadSettingsAsync();

            if (settings.TelegramApiId is null)
                throw new Exception("API_ID setting not set.");
            if (settings.TelegramApiHash is null)
                throw new Exception("API_HASH setting not set.");
            if (settings.TelegramBotToken is null)
                throw new Exception("BOT_TOKEN setting not set.");

            WTelegram.Helpers.Log = (i, s) => {
                FoxLog.WriteLine(s, LogLevel.LOG_DEBUG);
            };

            botClient = new WTelegram.Client(settings.TelegramApiId ?? 0, settings.TelegramApiHash, "../conf/telegram.session");

            botClient.OnUpdate += (e) => HandleUpdateAsync(botClient, e, cts);

            //Load workers BEFORE processing input from telegram.
            await FoxWorker.LoadWorkers(botClient);

            await botClient.LoginBotIfNeeded(settings.TelegramBotToken); 

            FoxLog.WriteLine($"We are logged-in as {botClient.User} (id {botClient.User.id})");

            await FoxCommandHandler.SetBotCommands(botClient);

            //await botClient.SetMyCommandsAsync(FoxCommandHandler.GenerateTelegramBotCommands());

            using (var cmd = new MySqlCommand($"UPDATE queue SET status = 'PENDING' WHERE status = 'PROCESSING'", sql))
            {
                long stuck_count = await cmd.ExecuteNonQueryAsync();
                FoxLog.WriteLine($"Unstuck {stuck_count} queue items.");
            }

            await FoxWorker.StartWorkers();

            //_ = FoxQueue.NotifyUserPositions(botClient, cts);

            //Console.ReadLine();

            await FoxUI.Run(cts);

            //await botClient.LogOutAsync();

            // Send cancellation request to stop bot
            cts.Cancel();


        }
    }
}
