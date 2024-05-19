using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Transactions;
using TL;
using TL.Methods;
using WTelegram;

namespace makefoxsrv
{
    internal class FoxTelegram
    {

        public static WTelegram.Client? Client
        {
            get => _client ?? throw new InvalidOperationException("Client is null");
        }

        public TL.User User {
            get => _user ?? throw new InvalidOperationException("User is null");
        }        

        public TL.ChatBase? Chat { get => _chat; }
        public TL.InputPeer? Peer { get => _peer; }

        private TL.User? _user;
        private TL.ChatBase? _chat;
        private TL.InputPeer? _peer;

        private long _userId;
        private long? _chatId;

        public static bool IsConnected => _client is not null && !_client.Disconnected;

        private static int appID;
        private static string apiHash;
        private static string botToken;
        private static string? sessionFile = null;

        private static readonly Dictionary<long, User> Users = [];
        private static readonly Dictionary<long, ChatBase> Chats = [];

        private static WTelegram.Client? _client;

        public FoxTelegram(User user, ChatBase? chat)
        {
            _user = user;
            _userId = user.ID;
            _chat = chat;
            if (chat is not null)
            {
                _peer = chat;
                _chatId = chat.ID;
            }
            else
                _peer = user;
        }

        private static async Task Client_OnOther(IObject arg)
        {
            switch (arg)
            {

                case ReactorError err:
                // typically: network connection was totally lost
                    FoxLog.WriteLine($"Fatal reactor error: {err.Exception.Message}", LogLevel.ERROR);
                    while (true)
                    {
                        FoxLog.WriteLine("Trying to reconnect to Telegram in 2 seconds...", LogLevel.ERROR);

                        await Task.Delay(2000);
                        try
                        {
                            _client?.Dispose();
                            _client = null;
                            await Connect(appID, apiHash, botToken, sessionFile);
                            break;
                        }
                        catch (Exception ex)
                        {
                            FoxLog.WriteLine($"Connection still failing: {ex.Message}", LogLevel.ERROR);
                        }
                    }
                    break;
                case TL.Pong:
                    break; //Don't care about these.
                default:
                    FoxLog.WriteLine("TelegramClient_OnOther: " + arg.GetType().Name, LogLevel.DEBUG);
                    break;
            }                
        }

        public static async Task Connect(int appID, string apiHash, string botToken, string? sessionFile = null)
        {
            FoxTelegram.appID = appID;
            FoxTelegram.apiHash = apiHash;
            FoxTelegram.botToken = botToken;
            FoxTelegram.sessionFile = sessionFile;

            WTelegram.Helpers.Log = (i, s) =>
            {
                //FoxLog.WriteLine(s, LogLevel.LOG_DEBUG);
                //Console.WriteLine(s);
            };

            if (_client is null)
            {
                _client = new WTelegram.Client(appID, apiHash, sessionFile);
                _client.OnOther += Client_OnOther;
                _client.OnUpdate += HandleUpdateAsync;
            }
            else
                _client.Reset(false, true);

            _client.MaxAutoReconnects = 1000;
            _client.FloodRetryThreshold = 0;

            await _client.LoginBotIfNeeded(botToken);

            FoxLog.WriteLine($"We are logged-in as {_client.User} (id {_client.User.ID})");
        }

        private Peer? InputToPeer(InputPeer peer) => peer switch
        {
            InputPeerSelf => new PeerUser { user_id = _userId },
            InputPeerUser ipu => new PeerUser { user_id = ipu.user_id },
            InputPeerChat ipc => new PeerChat { chat_id = ipc.chat_id },
            InputPeerChannel ipch => new PeerChannel { channel_id = ipch.channel_id },
            InputPeerUserFromMessage ipufm => new PeerUser { user_id = ipufm.user_id },
            InputPeerChannelFromMessage ipcfm => new PeerChannel { channel_id = ipcfm.channel_id },
            _ => null,
        };

        public async Task<Message> SendMessageAsync(string? text = null, int replyToMessageId = 0, ReplyInlineMarkup? replyInlineMarkup = null, MessageEntity[]? entities = null,
            bool disableWebPagePreview = true, InputMedia? media = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            long random_id = Helpers.RandomLong();

            UpdatesBase? updates;


            if (media is not null)
            {
                updates = await _client.Messages_SendMedia(
                    peer: _peer,
                    random_id: random_id,
                    message: text,
                    reply_to: replyToMessageId == 0 ? null : new InputReplyToMessage { reply_to_msg_id = replyToMessageId },
                    reply_markup: replyInlineMarkup,
                    entities: entities,
                    media: media
                );
            }
            else
            {
                updates = await _client.Messages_SendMessage(
                            peer: _peer,
                            random_id: random_id,
                            message: text,
                            reply_to: replyToMessageId == 0 ? null : new InputReplyToMessage { reply_to_msg_id = replyToMessageId },
                            reply_markup: replyInlineMarkup,
                            entities: entities,
                            no_webpage: disableWebPagePreview
                        );
            }

            if (updates is UpdateShortSentMessage sent)
                return new Message
                {
                    flags = (Message.Flags)sent.flags | (replyToMessageId == 0 ? 0 : Message.Flags.has_reply_to) | (_peer is InputPeerSelf ? 0 : Message.Flags.has_from_id),
                    id = sent.id,
                    date = sent.date,
                    message = text,
                    entities = sent.entities,
                    media = sent.media,
                    ttl_period = sent.ttl_period,
                    reply_markup = replyInlineMarkup,
                    reply_to = replyToMessageId == 0 ? null : new MessageReplyHeader { reply_to_msg_id = replyToMessageId, flags = MessageReplyHeader.Flags.has_reply_to_msg_id },
                    from_id = _peer is InputPeerSelf ? null : new PeerUser { user_id = _userId },
                    peer_id = InputToPeer(_peer)
                };
            int msgId = -1;
            foreach (var update in updates.UpdateList)
            {
                switch (update)
                {
                    case UpdateMessageID updMsgId when updMsgId.random_id == random_id: msgId = updMsgId.id; break;
                    case UpdateNewMessage { message: Message message } when message.id == msgId: return message;
                    case UpdateNewScheduledMessage { message: Message schedMsg } when schedMsg.id == msgId: return schedMsg;
                }
            }
            return null;
        }

        public async Task EditMessageAsync(int id, string? text = null, ReplyInlineMarkup ? replyInlineMarkup = null, MessageEntity[]? entities = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            await _client.Messages_EditMessage(
                peer: _peer,
                message: text,
                id: id,
                reply_markup: replyInlineMarkup,
                entities: entities
            );
        }

        public async Task DeleteMessage(int id)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            await _client.DeleteMessages(
                peer: _peer,
                id: [ id ]
            );
        }

        public static async Task<TL.User?> GetUserFromID(long id)
        {
            long? accessHash = null;

            Users.TryGetValue(id, out User? user);

            if (user is not null)
                return user;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT access_hash FROM telegram_users WHERE id = @id", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            accessHash =  r["access_hash"] != DBNull.Value ? Convert.ToInt64(r["access_hash"]) : null;
                        }
                    }
                }
            }

            if (accessHash is null)
                return null;

            return new() { id = id, access_hash = accessHash.Value };
        }

        public static async Task<TL.ChatBase?> GetChatFromID(long id)
        {
            long? accessHash = null;

            Chats.TryGetValue(id, out ChatBase? chat);

            if (chat is not null)
                return chat;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT access_hash FROM telegram_chats WHERE id = @id", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            accessHash = r["access_hash"] != DBNull.Value ? Convert.ToInt64(r["access_hash"]) : null;
                        }
                        else
                            return null; //Didn't find anything in the database.
                    }
                }
            }

            if (accessHash is not null)
            {
                return new Channel() { id = id, access_hash = accessHash.Value };
            }
            else
                return new Chat() { id = id };
        }

        public async Task SendCallbackAnswer(long queryID, int cacheTime, string? message = null, string? url = null, bool alert = false, bool ignoreErrors = true)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            try {
                await _client.Messages_SetBotCallbackAnswer(queryID, cacheTime, message, url, alert);
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"SendCallbackAnswer error: {ex.Message}\r\n{ex.StackTrace}", LogLevel.ERROR);
                if (!ignoreErrors)
                    throw;
            }
        }
            
        private static async Task HandlePayment(FoxTelegram t, MessageService ms, MessageActionPaymentSentMe payment)
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

                await recvUser.RecordPayment((int)payment.total_amount, payment.currency, days, payload, payment.charge.id, payment.charge.provider_charge_id);
                FoxLog.WriteLine($"Payment recorded for user {recvUID} by {user.UID}: ({payment.total_amount}, {payment.currency}, {days}, {payload}, {payment.charge.id}, {payment.charge.provider_charge_id})");

                var msg = @$"
<b>Thank You for Your Generous Support!</b>

We are deeply grateful for your donation, which is vital for our platform's sustainability and growth.

Your contribution has granted you <b>{(days == -1 ? "lifetime" : $"{days} days of")} premium access</b>, enhancing your experience with increased limits and features.

We are committed to using your donation to further develop and maintain the service, supporting our mission to provide a creative and expansive platform for our users. Thank you for being an integral part of our journey and for empowering us to continue offering a high-quality service.
";
                var entities = _client.HtmlToEntities(ref msg);

                await t.SendMessageAsync(
                            text: msg,
                            replyToMessageId: ms.id,
                            entities: entities,
                            disableWebPagePreview: true
                        );

                //await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
            }
        }

        private static string ReplaceNonPrintableCharacters(string input)
        {
            return input; //Disable this for now.

            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            StringBuilder output = new StringBuilder();
            foreach (char c in input)
            {
                if (c == '\r')
                {
                    output.Append("\\r");
                }
                else if (c == '\n')
                {
                    output.Append("\\n");
                }
                //else if (Encoding.UTF8.GetByteCount(new[] { c }) == 1)
                //{
                //    output.Append(c);
                //}
                //else if (char.IsControl(c))
                //{
                //    output.Append('?');
                //}
                else
                {
                    output.Append(c);
                }
            }

            return output.ToString();
        }

        private static async Task HandleMessageAsync(FoxTelegram t, Message msg)
        {
            // Only process text messages

            FoxLog.WriteLine($"{msg.ID}: Message: {t.User}" + (t.Chat is not null ? $" in {t.Chat}" : "") + $"> {ReplaceNonPrintableCharacters(msg.message)}");

            var message = FoxTelegram.Client.EntitiesToHtml(msg.message, msg.entities);

            if (msg.message is not null)
            {
                try
                {
                    _ = Task.Run(async () =>
                    {
                        using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                        {
                            await SQL.OpenAsync();

                            using (var cmd = new MySqlCommand())
                            {
                                cmd.Connection = SQL;
                                cmd.CommandText = "INSERT INTO telegram_log (user_id, chat_id, message_id, message_text, date_added) VALUES (@tele_id, @tele_chatid, @message_id, @message, @now)";
                                cmd.Parameters.AddWithValue("tele_id", t.User.ID);
                                cmd.Parameters.AddWithValue("tele_chatid", t.Chat is not null ? t.Chat.ID : null);
                                cmd.Parameters.AddWithValue("message_id", msg.ID);
                                cmd.Parameters.AddWithValue("message", message);
                                cmd.Parameters.AddWithValue("now", DateTime.Now);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await FoxWebChat.BroadcastMessageAsync(null, t.User, t.Peer, message);
                    });

                    await FoxCommandHandler.HandleCommand(t, msg);
                    FoxLog.WriteLine($"{msg.ID}: Finished processing input for {t.User.username}.");

                    //await DatabaseHandler.DisplayReceivedTelegramMessage(t.User.ID, message);
                    
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine("telegram_log error: " + ex.Message);
                }
            }
        }

        private static async Task HandleDeleteMessagesAsync(int[] messages)
        {
            try
            {
                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var transaction = await SQL.BeginTransactionAsync())
                    {
                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;

                            cmd.CommandText = "UPDATE telegram_log SET message_deleted = 1 WHERE message_id IN (" + string.Join(",", messages) + ")";

                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;

                            cmd.CommandText = "UPDATE images SET hidden = 1 WHERE telegram_msgid IN (" + string.Join(",", messages) + ")";

                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;

                            cmd.CommandText = "UPDATE queue SET msg_deleted = 1 WHERE msg_id IN (" + string.Join(",", messages) + ")";

                            await cmd.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine("Error in HandleDeleteMessagesAsync: " + ex.Message);
            }
        }

        private static async Task HandleUpdateAsync(UpdatesBase updates)
        {
            updates.CollectUsersChats(FoxTelegram.Users, FoxTelegram.Chats);

            _ = Task.Run(async () =>
            {
                await UpdateTelegramUsers(updates.Users);

                await UpdateTelegramChats(updates.Chats);
            });

            foreach (var update in updates.UpdateList)
            {
                try
                {
                    User? user = null;
                    ChatBase? chat = null;
                    FoxTelegram? t = null;
                    int msg_id = 0;

                    //FoxLog.WriteLine("Update type from Telegram: " + update.GetType().Name);

                    try
                    {
                        switch (update)
                        {
                            case UpdateNewAuthorization una:
                                break;

                            case UpdateNewMessage unm:
                                switch (unm.message)
                                {
                                    case Message m:
                                        updates.Users.TryGetValue(m.from_id ?? m.peer_id, out user);
                                        updates.Chats.TryGetValue(m.peer_id, out chat);

                                        if (user is null)
                                            throw new Exception("Invalid telegram user");

                                        t = new FoxTelegram(user, chat);
                                        msg_id = m.ID;

                                        if (m.media is MessageMediaPhoto { photo: Photo photo })
                                        {
                                            await FoxImage.SaveImageFromTelegram(t, m, photo);
                                        }
                                        else
                                        {
                                            await HandleMessageAsync(t, m);
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

                                                t = new FoxTelegram(user, chat);
                                                msg_id = ms.ID;

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
                                await HandleDeleteMessagesAsync(udcm.messages);
                                break;
                            case UpdateDeleteMessages udm:
                                await HandleDeleteMessagesAsync(udm.messages);
                                break;

                            case UpdateBotCallbackQuery ucbk:
                                updates.Users.TryGetValue(ucbk.user_id, out user);

                                if (user is null)
                                    throw new Exception("Invalid telegram user");

                                var p = updates.UserOrChat(ucbk.peer);

                                if (p is ChatBase c)
                                    chat = c;

                                t = new FoxTelegram(user, chat);

                                FoxLog.WriteLine($"Callback: {user}" + (chat is not null ? $" in {chat}" : "") + $"> {System.Text.Encoding.ASCII.GetString(ucbk.data)}");

                                await FoxCallbacks.Handle(t, ucbk, System.Text.Encoding.ASCII.GetString(ucbk.data));

                                break;
                            case UpdateBotPrecheckoutQuery upck:
                                await _client.Messages_SetBotPrecheckoutResults(upck.query_id, null, true);
                                break;
                            case UpdateReadChannelOutbox urco:
                                //User has read our messages (and we're an admin in the channel)
                                break;
                            default:
                                FoxLog.WriteLine("Unexpected update type from Telegram: " + update.GetType().Name);
                                break; // there are much more update types than the above example cases
                        }
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine("Error in HandleUpdateAsync: " + ex.Message);
                        FoxLog.WriteLine("Backtrace : " + ex.StackTrace);

                        if (t is not null)
                        {
                            try
                            {
                                await t.SendMessageAsync(
                                    text: "❌ Error! \"" + ex.Message + "\"",
                                    replyToMessageId: msg_id
                                );
                            } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine("Error in HandleUpdateAsync: " + ex.Message);
                }
            }
        }

        private static async Task UpdateTelegramUser(User user, bool forceFullUpdate = false)
        {
            try
            {
                if (user is null)
                    throw new Exception("User is null");

                if (Client is null)
                    throw new Exception("Client is null");

                if (Client.User is not null && user.ID == Client.User.ID)
                    return; //We don't really need to store info about ourself.

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var transaction = await SQL.BeginTransactionAsync())
                    {
                        Byte[]? photoBytes = null;
                        long? photoID = 0;
                        var fullUpdate = forceFullUpdate;
                        var now = DateTime.Now;

                        if (FoxSettings.Get<bool>("GetFullUser") || FoxSettings.Get<bool>("GetUserPhoto"))
                        {
                            using (var cmd = new MySqlCommand())
                            {
                                cmd.Connection = SQL;
                                cmd.Transaction = transaction;
                                cmd.CommandText = "SELECT last_full_update,photo_id FROM telegram_users WHERE id = @id";
                                cmd.Parameters.AddWithValue("id", user.ID);

                                using var reader = await cmd.ExecuteReaderAsync();

                                if (reader.HasRows)
                                {
                                    await reader.ReadAsync();

                                    photoID = reader["photo_id"] as long?;

                                    DateTime? dateFullUpdated = reader["last_full_update"] is DBNull ? null : reader.GetDateTime("last_full_update");

                                    if (dateFullUpdated is null || dateFullUpdated < now.AddHours(-1))
                                        fullUpdate = true; //If at least an hour has passed, force the update.
                                }
                                else
                                    fullUpdate = true; //Always update if it's missing.
                            }
                        }

                        UserFull? fullUser = null;

                        //FoxLog.WriteLine($"UpdateTelegramUser: {user}, Forced={ForceUpdate}");

                        try
                        {
                            if (fullUpdate && (FoxSettings.Get<bool>("GetFullUser") || FoxSettings.Get<bool>("GetUserPhoto")))
                            {
                                fullUser = (await Client.Users_GetFullUser(user)).full_user;

                                if (FoxSettings.Get<bool>("GetUserPhoto"))
                                {
                                    MemoryStream memoryStream = new MemoryStream();
                                    PhotoBase photo = fullUser.profile_photo;

                                    switch (photo)
                                    {
                                        case Photo p:
                                            if (photo.ID != photoID)
                                            {
                                                photoID = p.ID;

                                                await FoxTelegram.Client.DownloadFileAsync(p, memoryStream, p.LargestPhotoSize);
                                                photoBytes = memoryStream.ToArray();
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        catch (WTelegram.WTException ex)
                        {
                            //If we can't edit, we probably hit a rate limit with this user.

                            if (ex is RpcException rex)
                            {
                                FoxLog.WriteLine($"Error getting full user={user} x={rex.X} code={rex.Code} > {ex.Message}");
                            }
                            else
                                FoxLog.WriteLine($"Error getting full user={user} {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            FoxLog.WriteLine("Error getting full user={user}: " + ex.Message);
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                INSERT INTO telegram_users
                                (id, access_hash, active, type, language, username, firstname, lastname, bio, flags, flags2, date_added, date_updated, photo_id, photo, last_full_update)
                                VALUES 
                                (@id, @access_hash, @active, @type, @language, @username, @firstname, @lastname, @bio, @flags, @flags2, @date_updated, @date_updated, @photo_id, @photo, @last_full_update)
                                ON DUPLICATE KEY UPDATE 
                                    access_hash = COALESCE(@access_hash, access_hash),
                                    active = COALESCE(@active, active),
                                    type = COALESCE(@type, type),
                                    language = COALESCE(@language, language),
                                    username = COALESCE(@username, username),
                                    firstname = COALESCE(@firstname, firstname),
                                    lastname = COALESCE(@lastname, lastname),
                                    bio = COALESCE(@bio, bio),
                                    flags = COALESCE(@flags, flags),
                                    flags2 = COALESCE(@flags2, flags2),
                                    date_added = COALESCE(date_added, @date_updated),
                                    date_updated = COALESCE(@date_updated, date_updated),
                                    photo_id = COALESCE(@photo_id, photo_id),
                                    photo = COALESCE(@photo, photo),
                                    last_full_update = COALESCE(@last_full_update, last_full_update);
                            ";

                            cmd.Parameters.AddWithValue("id", user.ID);
                            cmd.Parameters.AddWithValue("access_hash", user.access_hash != 0 ? user.access_hash : null);
                            cmd.Parameters.AddWithValue("active", user.IsActive);
                            cmd.Parameters.AddWithValue("type", user.IsBot ? "BOT" : "USER");
                            cmd.Parameters.AddWithValue("language", user.lang_code);
                            cmd.Parameters.AddWithValue("username", user.MainUsername);
                            cmd.Parameters.AddWithValue("firstname", user.first_name);
                            cmd.Parameters.AddWithValue("lastname", user.last_name);
                            cmd.Parameters.AddWithValue("bio", fullUser?.about);
                            cmd.Parameters.AddWithValue("flags", fullUser?.flags);
                            cmd.Parameters.AddWithValue("flags2", fullUser?.flags2);
                            cmd.Parameters.AddWithValue("date_updated", now);
                            cmd.Parameters.AddWithValue("last_full_update", fullUser is not null ? now : null);
                            cmd.Parameters.AddWithValue("photo_id", photoID);
                            cmd.Parameters.AddWithValue("photo", photoBytes);

                            await cmd.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"updateTelegramUser error: chat={user.ID} {ex.Message}\r\n{ex.StackTrace}");
            }
        }

        private static async Task UpdateTelegramChat(ChatBase chat, bool forceFullUpdate = false)
        {
            try
            {
                if (chat is null)
                    throw new Exception("Chat is null");

                if (Client is null)
                    throw new Exception("Client is null");

                if (chat.Title == "Unsupported Chat")
                    return; //I still don't really know what this chat is all about, but skip it for now.

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var transaction = await SQL.BeginTransactionAsync())
                    {
                        Byte[]? chatPhoto = null;
                        long? photoID = 0;
                        var fullUpdate = forceFullUpdate;
                        var now = DateTime.Now;

                        if (FoxSettings.Get<bool>("GetFullChat") || FoxSettings.Get<bool>("GetChatPhoto") || FoxSettings.Get<bool>("GetChatAdmins"))
                        {

                            using (var cmd = new MySqlCommand())
                            {
                                cmd.Connection = SQL;
                                cmd.Transaction = transaction;
                                cmd.CommandText = "SELECT last_full_update,photo_id FROM telegram_chats WHERE id = @id";
                                cmd.Parameters.AddWithValue("id", chat.ID);

                                using var reader = await cmd.ExecuteReaderAsync();

                                if (reader.HasRows)
                                {
                                    await reader.ReadAsync();

                                    photoID = reader["photo_id"] as long?;

                                    DateTime? dateFullUpdated = reader["last_full_update"] is DBNull ? null : reader.GetDateTime("last_full_update");

                                    if (dateFullUpdated is null || dateFullUpdated < now.AddHours(-1))
                                        fullUpdate = true; //If at least an hour has passed, force the update.
                                }
                                else
                                    fullUpdate = true; //Always update if it's missing.
                            }
                        }

                        long? adminFlags = null;
                        var groupType = "GROUP";
                        TL.Channel? group = null;
                        ChannelFull? fullChannel = null;
                        ChatFullBase? fullChat = null;
                        long? flags = null;
                        long? flags2 = null;

                        //FoxLog.WriteLine($"UpdateTelegramChat: {chat}, Forced={ForceUpdate}");

                        try
                        {
                            if (fullUpdate && (FoxSettings.Get<bool>("GetFullChat") || FoxSettings.Get<bool>("GetChatPhoto") || FoxSettings.Get<bool>("GetChatAdmins")))
                            {
                                fullChat = (await Client.GetFullChat(chat)).full_chat;
                                fullChannel = fullChat as ChannelFull;

                                if (FoxSettings.Get<bool>("GetChatPhoto"))
                                {
                                    MemoryStream memoryStream = new MemoryStream();

                                    PhotoBase photo = fullChat.ChatPhoto;

                                    switch (photo)
                                    {
                                        case Photo p:
                                            if (photo.ID != photoID)
                                            {
                                                photoID = p.ID;

                                                await FoxTelegram.Client.DownloadFileAsync(p, memoryStream, p.LargestPhotoSize);
                                                chatPhoto = memoryStream.ToArray();
                                            }

                                            break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message != "CHANNEL_PRIVATE")
                                FoxLog.WriteLine($"Error getting full chat: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
                        }

                        if (chat.IsChannel || chat.IsGroup)
                        {
                            switch (chat)
                            {
                                case Channel g:
                                    group = g;

                                    var admin = group.admin_rights;
                                    if (admin is not null)
                                        adminFlags = ((long)admin.flags);

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

                                    flags = (long)g.flags;
                                    flags2 = (long)g.flags2;

                                    break;
                                case Chat c:
                                    groupType = "GROUP";
                                    flags = (long)c.flags;

                                    if (c.admin_rights is not null)
                                        adminFlags = ((long)c.admin_rights.flags);

                                    break;
                                default:
                                    groupType = "UNKNOWN";

                                    FoxLog.WriteLine($"Unexpected chat type: chat={chat.ID} {chat.GetType().Name}");
                                    break;
                            }
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                INSERT INTO telegram_chats 
                                (id, access_hash, active, username, title, type, level, flags, flags2, description, admin_flags, participants, photo_id, photo, date_added, date_updated, last_full_update)
                                VALUES 
                                (@id, @access_hash, @active, @username, @title, @type, @level, @flags, @flags2, @description, @admin_flags, @participants, @photo_id, @photo, @date_updated, @date_updated, @last_full_update)
                                ON DUPLICATE KEY UPDATE 
                                    access_hash = COALESCE(@access_hash, access_hash),
                                    active = VALUE(active),
                                    username = VALUE(username),
                                    title = VALUE(title),
                                    type = VALUE(type),
                                    level = COALESCE(@level, level),
                                    flags = COALESCE(@flags, flags),
                                    flags2 = COALESCE(@flags2, flags2),
                                    description = COALESCE(@description, description),
                                    admin_flags = COALESCE(@admin_flags, admin_flags),
                                    participants = COALESCE(@participants, participants),
                                    photo_id = COALESCE(@photo_id, photo_id),
                                    photo = COALESCE(@photo, photo),
                                    date_added = COALESCE(date_added, @date_updated),
                                    date_updated = COALESCE(@date_updated, date_updated),
                                    last_full_update = COALESCE(@last_full_update, last_full_update);
                            ";

                            cmd.Parameters.AddWithValue("id", chat.ID);
                            cmd.Parameters.AddWithValue("access_hash", group?.access_hash != 0 ? group?.access_hash : null);
                            cmd.Parameters.AddWithValue("active", chat.IsActive);
                            cmd.Parameters.AddWithValue("username", chat.MainUsername);
                            cmd.Parameters.AddWithValue("title", chat.Title);
                            cmd.Parameters.AddWithValue("level", group?.level);
                            cmd.Parameters.AddWithValue("type", groupType);
                            cmd.Parameters.AddWithValue("flags", flags);
                            cmd.Parameters.AddWithValue("flags2", flags2);
                            cmd.Parameters.AddWithValue("admin_flags", adminFlags);
                            cmd.Parameters.AddWithValue("last_full_update", fullChat is not null ? now : null);
                            cmd.Parameters.AddWithValue("date_updated", now);
                            cmd.Parameters.AddWithValue("description", fullChat?.About);
                            cmd.Parameters.AddWithValue("participants", fullChat?.ParticipantsCount ?? group?.participants_count);
                            //cmd.Parameters.AddWithValue("slowmode_next_date", fullChannel?.slowmode_next_send_date);

                            cmd.Parameters.AddWithValue("photo_id", photoID);
                            cmd.Parameters.AddWithValue("photo", chatPhoto);

                            await cmd.ExecuteNonQueryAsync();
                        }

                        if (FoxSettings.Get<bool>("GetChatAdmins") && group is not null && (!group.IsChannel || group.flags.HasFlag(Channel.Flags.has_admin_rights)))
                        {
                            try
                            {
                                var channelParticipants = new ChannelParticipantsAdmins();
                                var groupAdmins = await Client.Channels_GetParticipants(group, channelParticipants);

                                groupAdmins.CollectUsersChats(FoxTelegram.Users, FoxTelegram.Chats);

                                await UpdateTelegramUsers(groupAdmins.users);

                                using (var cmd = new MySqlCommand())
                                {
                                    cmd.Connection = SQL;
                                    cmd.Transaction = transaction;
                                    cmd.CommandText = @"DELETE FROM telegram_chat_admins WHERE chatid = @chatid";
                                    cmd.Parameters.AddWithValue("chatid", chat.ID);

                                    await cmd.ExecuteNonQueryAsync();
                                }

                                foreach (var p in groupAdmins.participants)
                                {
                                    var adminType = "UNKNOWN";
                                    string? admRank = null;
                                    long? admFlags = null;

                                    groupAdmins.users.TryGetValue(p.UserId, out User? user);

                                    switch (p)
                                    {
                                        case ChannelParticipantAdmin admin:
                                            adminType = "ADMIN";

                                            admRank = admin.rank;
                                            admFlags = (long)admin.admin_rights.flags;

                                            break;
                                        case ChannelParticipantCreator creator:
                                            adminType = "CREATOR";

                                            admRank = creator.rank;
                                            admFlags = (long)creator.admin_rights.flags;

                                            break;
                                        default:
                                            FoxLog.WriteLine($"Unexpected participant type: chat={chat.ID} {p.GetType().Name}");
                                            break;
                                    }

                                    using (var cmd = new MySqlCommand())
                                    {
                                        cmd.Connection = SQL;
                                        cmd.Transaction = transaction;
                                        cmd.CommandText = "REPLACE INTO telegram_chat_admins (chatid, userid, type, rank, flags, date_updated) VALUES (@chatid, @userid, @type, @rank, @flags, @now)";
                                        cmd.Parameters.AddWithValue("chatid", chat.ID);
                                        cmd.Parameters.AddWithValue("userid", p.UserId);
                                        cmd.Parameters.AddWithValue("type", adminType);
                                        cmd.Parameters.AddWithValue("flags", admFlags);
                                        cmd.Parameters.AddWithValue("rank", admRank);
                                        cmd.Parameters.AddWithValue("now", DateTime.Now);

                                        await cmd.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message != "CHANNEL_PRIVATE" /*&& ex.Message != "CHAT_ADMIN_REQUIRED"*/)
                                    FoxLog.WriteLine($"Error getting group admins: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"updateTelegramChat error: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
            }
        }

        private static async Task UpdateTelegramUsers(IDictionary<long, User> users, bool ForceUpdate = false)
        {
            try
            {
                foreach (var user in users.Values)
                {
                    try
                    {
                        await UpdateTelegramUser(user, ForceUpdate);
                    }
                    catch (Exception ex)
                    {
                        FoxLog.WriteLine($"updateTelegramUsers error: chat={user.ID} {ex.Message}\r\n{ex.StackTrace}");
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.WriteLine($"updateTelegramUsers error: {ex.Message}\r\n{ex.StackTrace}");
            }
        }

        private static async Task UpdateTelegramChats(IDictionary<long, ChatBase> chats, bool ForceUpdate = false)
        {
            //try
            //{
            //    using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
            //    {
            //        await SQL.OpenAsync();

            //        using (var cmd = new MySqlCommand())
            //        {
            //            cmd.Connection = SQL;
            //            cmd.CommandText = "SELECT id FROM telegram_chats WHERE (" + string.Join(" OR ", chats.Keys.Select(k => $"id = {k}")) + ") AND date_updated <= @date";
            //            cmd.Parameters.AddWithValue("date", DateTime.Now.AddHours(-1));

            //            using var reader = await cmd.ExecuteReaderAsync();

            //            if (reader.HasRows)
            //            {
            //                while (await reader.ReadAsync())
            //                {
            //                    long chatId = reader.GetInt64(0);
            //                    if (chats.TryGetValue(chatId, out ChatBase? chat))
            //                    {
            //                        FoxLog.WriteLine("UpdateTelegramChats: " + chat);
            //                        await UpdateTelegramChat(chat, ForceUpdate);
            //                    }
            //                }
            //            }
            //        }
            //    }
            //} catch (Exception ex)
            //{
            //    FoxLog.WriteLine($"updateTelegramChats error: {ex.Message}\r\n{ex.StackTrace}");
            //}   

            foreach (var chat in chats.Values)
            {
                try
                {
                    await UpdateTelegramChat(chat, ForceUpdate);
                }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"updateTelegramChats error: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
                }
            }
        }

        internal static async Task Disconnect()
        {
            //await Client.Auth_LogOut();
            if (_client is not null)
            {
                _client.Reset(false, true);
                _client.Dispose();
            }
            _client = null;
        }
    }
}