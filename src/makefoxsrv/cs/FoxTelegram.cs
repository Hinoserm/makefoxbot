using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            get => _Client ?? throw new InvalidOperationException("Client is null");
        }

        public TL.User User {
            get => _User ?? throw new InvalidOperationException("User is null");
        }

        public TL.ChatBase? Chat { get => _Chat; }
        public TL.InputPeer? Peer { get => _Peer; }

        private TL.User? _User;
        private TL.ChatBase? _Chat;
        private TL.InputPeer? _Peer;

        private long _UserId;
        private long? _ChatId;        

        private static readonly Dictionary<long, User> Users = [];
        private static readonly Dictionary<long, ChatBase> Chats = [];

        private static WTelegram.Client? _Client;

        public FoxTelegram(User user, ChatBase? chat)
        {
            _User = user;
            _UserId = user.ID;
            _Chat = chat;
            if (chat is not null)
            {
                _Peer = chat;
                _ChatId = chat.ID;
            }
            else
                _Peer = user;
        }

        public FoxTelegram(long userId, long userAccessHash, long? chatId = null, long? chatAccessHash = null)
        {
            _UserId = userId;
            _ChatId = chatId;

            Users.TryGetValue(userId, out this._User);
            if (chatId is not null)
                Chats.TryGetValue((long)chatId, out this._Chat);

            if (chatId is not null && chatAccessHash is not null)
            {
                _Peer = new InputPeerChannel(chatId.Value, chatAccessHash.Value);
            }
            else if (chatId is not null)
            {
                _Peer = new InputPeerChat(chatId.Value);
            }
            else
            {
                _Peer = new InputPeerUser(userId, userAccessHash);
            }
        }

        public static async Task Connect(int appID, string apiHash, string botToken, string? sessionFile = null)
        {
            WTelegram.Helpers.Log = (i, s) =>
            {
                FoxLog.WriteLine(s, LogLevel.LOG_DEBUG);
            };

            _Client = new WTelegram.Client(appID, apiHash, sessionFile);

            _Client.OnUpdate += (update) => HandleUpdateAsync(update);

            await _Client.LoginBotIfNeeded(botToken);

            FoxLog.WriteLine($"We are logged-in as {_Client.User} (id {_Client.User.id})");
        }

        private Peer? InputToPeer(InputPeer peer) => peer switch
        {
            InputPeerSelf => new PeerUser { user_id = _UserId },
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
            if (_Client is null)
                throw new InvalidOperationException("Client is null");

            long random_id = Helpers.RandomLong();

            var updates = await _Client.Messages_SendMessage(
                        peer: _Peer,
                        random_id: random_id,
                        message: text,
                        reply_to: replyToMessageId == 0 ? null : new InputReplyToMessage { reply_to_msg_id = replyToMessageId },
                        reply_markup: replyInlineMarkup,
                        entities: entities,
                        no_webpage: disableWebPagePreview
                    );

            if (updates is UpdateShortSentMessage sent)
                return new Message
                {
                    flags = (Message.Flags)sent.flags | (replyToMessageId == 0 ? 0 : Message.Flags.has_reply_to) | (_Peer is InputPeerSelf ? 0 : Message.Flags.has_from_id),
                    id = sent.id,
                    date = sent.date,
                    message = text,
                    entities = sent.entities,
                    media = sent.media,
                    ttl_period = sent.ttl_period,
                    reply_markup = replyInlineMarkup,
                    reply_to = replyToMessageId == 0 ? null : new MessageReplyHeader { reply_to_msg_id = replyToMessageId, flags = MessageReplyHeader.Flags.has_reply_to_msg_id },
                    from_id = _Peer is InputPeerSelf ? null : new PeerUser { user_id = _UserId },
                    peer_id = InputToPeer(_Peer)
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

        public async Task EditMessageAsync(int id, string? text = null, ReplyInlineMarkup ? replyInlineMarkup = null)
        {
            if (_Client is null)
                throw new InvalidOperationException("Client is null");

            await _Client.Messages_EditMessage(
                peer: _Peer,
                message: text,
                id: id,
                reply_markup: replyInlineMarkup
            );
        }

        public async Task DeleteMessage(int id)
        {
            if (_Client is null)
                throw new InvalidOperationException("Client is null");

            await _Client.DeleteMessages(
                peer: _Peer,
                id: [ id ]
            );
        }

        public async Task SendCallbackAnswer(long queryID, int cacheTime, string? message = null)
        {
            if (_Client is null)
                throw new InvalidOperationException("Client is null");

            await _Client.Messages_SetBotCallbackAnswer(queryID, cacheTime, message);
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
                var entities = _Client.HtmlToEntities(ref msg);

                await t.SendMessageAsync(
                            text: msg,
                            replyToMessageId: ms.id,
                            entities: entities,
                            disableWebPagePreview: true
                        );

                //await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
            }
        }

        private static async Task HandleMessageAsync(FoxTelegram t, Message msg)
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

        private static async Task HandleUpdateAsync(UpdatesBase updates)
        {
            updates.CollectUsersChats(FoxTelegram.Users, FoxTelegram.Chats);

            //Run these in the background.
            _= Task.Run(async () => {
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
                                            _ = FoxImage.SaveImageFromTelegram(t, m, photo);
                                        }
                                        else
                                        {
                                            _ = HandleMessageAsync(t, m);
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

                                t = new FoxTelegram(user, chat);

                                FoxLog.WriteLine($"Callback: {user} in {chat}> {System.Text.Encoding.ASCII.GetString(ucbk.data)}");

                                _ = FoxCallbacks.Handle(t, ucbk, System.Text.Encoding.ASCII.GetString(ucbk.data));

                                break;
                            case UpdateBotPrecheckoutQuery upck:
                                await _Client.Messages_SetBotPrecheckoutResults(upck.query_id, null, true);
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

        private static async Task UpdateTelegramUser(User user, bool ForceUpdate = false)
        {
            try
            {
                if (user is null)
                    throw new Exception("User is null");

                if (Client is null)
                    throw new Exception("Client is null");

                using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var transaction = await SQL.BeginTransactionAsync())
                    {
                        DateTime? dateAdded = null;
                        Byte[]? photoBytes = null;
                        long? photoID = 0;
                        var shouldUpdate = ForceUpdate;

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = "SELECT date_updated,date_added,photo_id FROM telegram_users WHERE id = @id FOR UPDATE";
                            cmd.Parameters.AddWithValue("id", user.ID);

                            using var reader = await cmd.ExecuteReaderAsync();

                            if (reader.HasRows)
                            {
                                await reader.ReadAsync();

                                dateAdded = reader["date_added"] as DateTime?;
                                photoID = reader["photo_id"] as long?;

                                DateTime dateUpdated = reader.GetDateTime("date_updated");

                                if (dateAdded is null || dateUpdated < DateTime.Now.AddHours(-1))
                                    shouldUpdate = true; //If at least an hour has passed, force the update.
                            }
                            else
                                shouldUpdate = true; //Always update if it's missing.

                            if (dateAdded is null)
                                dateAdded = DateTime.Now;
                        }

                        if (shouldUpdate)
                        {
                            UserFull? fullUser = null;

                            try
                            {
                                fullUser = (await Client.Users_GetFullUser(user)).full_user;

                                MemoryStream memoryStream = new MemoryStream();

                                Photo? photo = (Photo?)fullUser.profile_photo;

                                if (photo is not null && photo.ID != photoID)
                                {
                                    photoID = photo.ID;

                                    //await FoxTelegram.Client.DownloadFileAsync(photo, memoryStream, photo.LargestPhotoSize);
                                    //photoBytes = memoryStream.ToArray();
                                }
                            }
                            catch (Exception ex)
                            {
                                FoxLog.WriteLine("Error getting full user: " + ex.Message);
                            }

                            using (var cmd = new MySqlCommand())
                            {
                                cmd.Connection = SQL;
                                cmd.Transaction = transaction;
                                cmd.CommandText = @"
                                    INSERT INTO telegram_users
                                    (id, access_hash, active, type, username, firstname, lastname, bio, flags, flags2, date_added, date_updated, photo_id, photo)
                                    VALUES 
                                    (@id, @access_hash, @active, @type, @username, @firstname, @lastname, @bio, @flags, @flags2, @date_added, @date_updated, @photo_id, @photo)
                                    ON DUPLICATE KEY UPDATE 
                                        access_hash = COALESCE(@access_hash, access_hash),
                                        active = COALESCE(@active, active),
                                        type = COALESCE(@type, type),
                                        username = COALESCE(@username, username),
                                        firstname = COALESCE(@firstname, firstname),
                                        lastname = COALESCE(@lastname, lastname),
                                        bio = COALESCE(@bio, bio),
                                        flags = COALESCE(@flags, flags),
                                        flags2 = COALESCE(@flags2, flags2),
                                        date_added = COALESCE(@date_added, date_added),
                                        date_updated = COALESCE(@date_updated, date_updated),
                                        photo_id = COALESCE(@photo_id, photo_id),
                                        photo = COALESCE(@photo, photo);
                                ";

                                cmd.Parameters.AddWithValue("id", user.ID);
                                cmd.Parameters.AddWithValue("access_hash", user.access_hash);
                                cmd.Parameters.AddWithValue("active", user.IsActive);
                                cmd.Parameters.AddWithValue("type", user.IsBot ? "BOT" : "USER");
                                cmd.Parameters.AddWithValue("username", user.MainUsername);
                                cmd.Parameters.AddWithValue("firstname", user.first_name);
                                cmd.Parameters.AddWithValue("lastname", user.last_name);
                                cmd.Parameters.AddWithValue("bio", fullUser?.about);

                                cmd.Parameters.AddWithValue("flags", fullUser?.flags);
                                cmd.Parameters.AddWithValue("flags2", fullUser?.flags2);
                                cmd.Parameters.AddWithValue("date_added", dateAdded);
                                cmd.Parameters.AddWithValue("date_updated", DateTime.Now);

                                cmd.Parameters.AddWithValue("photo_id", photoID);
                                cmd.Parameters.AddWithValue("photo", photoBytes);

                                await cmd.ExecuteNonQueryAsync();
                            }
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

        private static async Task UpdateTelegramChat(ChatBase chat, bool ForceUpdate = false)
        {
            try
            {
                if (chat is null)
                    throw new Exception("Chat is null");

                if (Client is null)
                    throw new Exception("Client is null");

                using (var SQL = new MySqlConnection(FoxMain.MySqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var transaction = await SQL.BeginTransactionAsync())
                    {
                        DateTime? dateAdded = null;
                        Byte[]? chatPhoto = null;
                        long? photoID = 0;
                        var shouldUpdate = true;

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = "SELECT date_updated,date_added,photo_id FROM telegram_chats WHERE id = @id FOR UPDATE";
                            cmd.Parameters.AddWithValue("id", chat.ID);

                            using var reader = await cmd.ExecuteReaderAsync();

                            if (reader.HasRows)
                            {
                                await reader.ReadAsync();

                                dateAdded = reader["date_added"] as DateTime?;
                                photoID = reader["photo_id"] as long?;

                                DateTime dateUpdated = reader.GetDateTime("date_updated");

                                if (dateAdded is null || dateUpdated < DateTime.Now.AddHours(-1))
                                    shouldUpdate = true; //If at least an hour has passed, force the update.
                            }
                            else
                                shouldUpdate = true; //Always update if it's missing.

                            if (dateAdded is null)
                                dateAdded = DateTime.Now;
                        }

                        if (shouldUpdate)
                        {
                            long? adminFlags = null;
                            var groupType = "GROUP";
                            TL.Channel? group = null;
                            ChannelFull? fullChannel = null;
                            ChatFullBase? fullChat = null;

                            try
                            {
                                fullChat = (await Client.GetFullChat(chat)).full_chat;
                                MemoryStream memoryStream = new MemoryStream();

                                Photo? photo = (Photo?)fullChat?.ChatPhoto;

                                if (photo is not null && photo.ID != photoID)
                                {
                                    photoID = photo.ID;

                                    await FoxTelegram.Client.DownloadFileAsync(photo, memoryStream, photo.LargestPhotoSize);
                                    chatPhoto = memoryStream.ToArray();
                                }

                                fullChannel = fullChat as ChannelFull;
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

                                        break;
                                    case Chat c:

                                        groupType = "UNKNOWN";
                                        break;
                                }
                            }

                            using (var cmd = new MySqlCommand())
                            {
                                cmd.Connection = SQL;
                                cmd.Transaction = transaction;
                                cmd.CommandText = @"
                                    INSERT INTO telegram_chats 
                                    (id, access_hash, active, username, title, type, level, flags, flags2, description, admin_flags, participants, photo_id, photo, date_added, date_updated)
                                    VALUES 
                                    (@id, @access_hash, @active, @username, @title, @type, @level, @flags, @flags2, @description, @admin_flags, @participants, @photo_id, @photo, @date_updated, @date_updated)
                                    ON DUPLICATE KEY UPDATE 
                                        access_hash = VALUE(access_hash),
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
                                        date_added = COALESCE(@date_added, date_added),
                                        date_updated = COALESCE(@date_updated, date_updated)
                                ";

                                cmd.Parameters.AddWithValue("id", chat.ID);
                                cmd.Parameters.AddWithValue("access_hash", group?.access_hash);
                                cmd.Parameters.AddWithValue("active", chat.IsActive);
                                cmd.Parameters.AddWithValue("username", chat.MainUsername);
                                cmd.Parameters.AddWithValue("title", chat.Title);
                                cmd.Parameters.AddWithValue("level", group?.level);
                                cmd.Parameters.AddWithValue("type", groupType);
                                cmd.Parameters.AddWithValue("flags", group?.flags);
                                cmd.Parameters.AddWithValue("flags2", group?.flags2);
                                cmd.Parameters.AddWithValue("admin_flags", adminFlags);
                                cmd.Parameters.AddWithValue("date_added", dateAdded);
                                cmd.Parameters.AddWithValue("date_updated", DateTime.Now);

                                cmd.Parameters.AddWithValue("description", fullChat?.About);
                                cmd.Parameters.AddWithValue("participants", fullChat?.ParticipantsCount ?? group?.participants_count);
                                cmd.Parameters.AddWithValue("slowmode_next_date", fullChannel?.slowmode_next_send_date);

                                cmd.Parameters.AddWithValue("photo_id", photoID);
                                cmd.Parameters.AddWithValue("photo", chatPhoto);

                                await cmd.ExecuteNonQueryAsync();
                            }

                            if (group is not null && (!group.IsChannel || group.flags.HasFlag(Channel.Flags.has_admin_rights)))
                            {
                                try
                                {
                                    var channelParticipants = new ChannelParticipantsAdmins();
                                    var groupAdmins = await Client.Channels_GetParticipants(group, channelParticipants);

                                    groupAdmins.CollectUsersChats(FoxTelegram.Users, FoxTelegram.Chats);

                                    await UpdateTelegramUsers(groupAdmins.users);

                                    foreach (var p in groupAdmins.participants)
                                    {
                                        var admin = p as ChannelParticipantAdmin;

                                        groupAdmins.users.TryGetValue(p.UserId, out User? user);

                                        using (var cmd = new MySqlCommand())
                                        {
                                            cmd.Connection = SQL;
                                            cmd.Transaction = transaction;
                                            cmd.CommandText = "REPLACE INTO telegram_chat_admins (chatid, userid, rank, flags, date_updated) VALUES (@chatid, @userid, @rank, @flags, @now)";
                                            cmd.Parameters.AddWithValue("chatid", chat.ID);
                                            cmd.Parameters.AddWithValue("userid", p.UserId);
                                            cmd.Parameters.AddWithValue("flags", admin?.admin_rights.flags);
                                            cmd.Parameters.AddWithValue("rank", admin?.rank);
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
    }
}