using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Transactions;
using TL;
using TL.Methods;
using WTelegram;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace makefoxsrv
{
    public class FoxTelegram
    {

        static public TL.User? BotUser { get; private set; } = null;

        public static WTelegram.Client Client
        {
            get => _client ?? throw new InvalidOperationException("Client is null");
        }

        public TL.User User {
            get => _user ?? throw new InvalidOperationException("User is null");
        }        

        public TL.ChatBase? Chat { get => _chat; }
        public TL.InputPeer Peer { get => _peer; }

        private TL.User _user;
        private TL.ChatBase? _chat;
        private TL.InputPeer _peer;

        private long _userId;
        private long? _chatId;

        public static bool IsConnected => _client is not null && !_client.Disconnected;

        private static int appID;
        private static string apiHash = "";
        private static string botToken = "";
        private static string? sessionFile = null;

        private static readonly Dictionary<long, User> Users = [];
        private static readonly Dictionary<long, ChatBase> Chats = [];

        private static WTelegram.Client? _client;

        private static readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void SetReady()
        {
            _readyTcs.TrySetResult();
        }
        
        internal static Task WaitUntilReadyAsync() => _readyTcs.Task;

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
                            FoxLog.LogException(ex, $"Connection still failing: {ex.Message}");
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
                //FoxLog.WriteLine(s, LogLevel.DEBUG);
                //Console.WriteLine(s);
            };

            if (_client is null)
            {
                _client = new WTelegram.Client(appID, apiHash, sessionFile);
                _client.OnOther += Client_OnOther;
                _client.OnUpdates += HandleUpdateAsync;
                //_client.OnOwnUpdates += HandleUpdateAsync;
            }
            else
                _client.Reset(false, true);

            _client.MaxAutoReconnects = 100000;
            _client.FloodRetryThreshold = 0;

            await _client.LoginBotIfNeeded(botToken);

            BotUser = _client.User;

            FoxLog.WriteLine($"We are logged-in as {_client.User} (id {_client.User.ID})");

            await FoxCommandHandler.SetBotCommands(_client);
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

        public async Task PinMessage(int messageId)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            await _client.Messages_UpdatePinnedMessage(
                peer: _peer,
                id: messageId

            );
        }

        public async Task<Message> SendMessageAsync(string? text = null, int replyToMessageId = 0, int replyToTopicId = 0,
            MessageBase? replyToMessage = null, ReplyInlineMarkup? replyInlineMarkup = null, MessageEntity[]? entities = null,
            bool disableWebPagePreview = true, InputMedia? media = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            long random_id = Helpers.RandomLong();

            UpdatesBase? updates;

            InputReplyToMessage? inputReplyToMessage = null;

            if (replyToMessage is not null)
            {
                inputReplyToMessage = new InputReplyToMessage { reply_to_msg_id = replyToMessage.ID, top_msg_id = replyToMessage.ReplyHeader?.TopicID ?? 0 };
            } else if (replyToMessageId != 0)
            {
                inputReplyToMessage = new InputReplyToMessage { reply_to_msg_id = replyToMessageId, top_msg_id = replyToTopicId };
            }

            if (inputReplyToMessage is not null && inputReplyToMessage.top_msg_id != 0)
                inputReplyToMessage.flags |= InputReplyToMessage.Flags.has_top_msg_id;

            if (media is not null)
            {
                if (text is not null && text.Count() > 1024)
                    text = text.Substring(0, 1024);

                updates = await _client.Messages_SendMedia(
                    peer: _peer,
                    random_id: random_id,
                    message: text,
                    reply_to: inputReplyToMessage,
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
                            reply_to: inputReplyToMessage,
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

            throw new Exception("Message not found in updates");
        }

        public async Task<Message?> EditMessageAsync(int id, string? text = null, TL.InputPeer? peer = null, ReplyInlineMarkup ? replyInlineMarkup = null, MessageEntity[]? entities = null, bool disableWebPagePreview = true)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Telegram is disconnected");

            if (peer is null)
                peer = _peer;

            var updates = await _client.Messages_EditMessage(
                peer: peer,
                message: text,
                id: id,
                reply_markup: replyInlineMarkup,
                no_webpage: disableWebPagePreview,
                entities: entities
            );

            foreach (var update in updates.UpdateList)
            {
                switch (update)
                {
                    case UpdateEditMessage { message: Message message }: return message;
                }
            }

            return null;
        }

        public async Task DeleteMessage(int id)
        {
            if (!IsConnected || _client is null)
                throw new InvalidOperationException("Telegram is disconnected");

            await _client.DeleteMessages(
                peer: _peer,
                id: [ id ]
            );
        }

        public static async Task<TL.User?> GetUserFromID(long id)
        {
            long? accessHash = null;
            string? firstName = null;
            string? lastName = null;
            string? userName = null;

            Users.TryGetValue(id, out User? user);

            if (user is not null)
                return user;

            using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await SQL.OpenAsync();

                using (var cmd = new MySqlCommand("SELECT access_hash, username, firstname, lastname FROM telegram_users WHERE id = @id", SQL))
                {
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            accessHash =  r["access_hash"] != DBNull.Value ? Convert.ToInt64(r["access_hash"]) : null;
                            firstName = r["firstname"] != DBNull.Value ? Convert.ToString(r["firstname"]) : null;
                            lastName = r["lastname"] != DBNull.Value ? Convert.ToString(r["lastname"]) : null;
                            userName = r["username"] != DBNull.Value ? Convert.ToString(r["username"]) : null;
                        }
                    }
                }
            }

            if (accessHash is null || accessHash == 0)
            {
                FoxLog.WriteLine($"Telegram user {id} is missing access hash!", LogLevel.ERROR);
                return null;
            }

            return new() { id = id, access_hash = accessHash.Value, first_name = firstName, last_name = lastName, username = userName };
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
                FoxLog.LogException(ex, $"SendCallbackAnswer error: {ex.Message}\r\n{ex.StackTrace}");
                if (!ignoreErrors)
                    throw;
            }
        }
            
        private static async Task HandlePayment(FoxTelegram t, MessageService ms, MessageActionPaymentSentMe payment)
        {
            var user = await FoxUser.GetByTelegramUser(t.User, true);

            FoxContextManager.Current = new FoxContext
            {
                Message = ms,
                Telegram = t,
                User = user
            };

            try
            {
                if (user is null)
                    throw new Exception("Unknown user in payment request!");

                await user.LockAsync();

                string payload = System.Text.Encoding.ASCII.GetString(payment.payload);
                string[] parts = payload.Split('_');
                if (parts.Length != 3 || parts[0] != "PAY" || !ulong.TryParse(parts[1], out ulong recvUID))
                {
                    throw new System.Exception("Malformed payment request!");
                }

                var recvUser = await FoxUser.GetByUID(recvUID);

                if (recvUser is null)
                    throw new Exception("Unknown UID in payment request!");

                var days = 30;

                await recvUser.RecordPayment(PaymentTypes.TELEGRAM, (int)payment.total_amount, payment.currency, days, payload, payment.charge.id, payment.charge.provider_charge_id, ms.id);
                FoxLog.WriteLine($"Payment recorded for user {recvUID} by {user.UID}: ({payment.total_amount}, {payment.currency}, {days}, {payload}, {payment.charge.id}, {payment.charge.provider_charge_id})");

                //await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
            }
            catch (Exception ex)
            {
                try
                {
                    await FoxTelegram.Client.Payments_RefundStarsCharge(t.User, payment.charge.id);
                }
                catch (Exception ex2)
                { 
                    FoxLog.LogException(ex2, "HandlePayment refund error: " + ex2.Message);
                }

                FoxLog.LogException(ex);
                await t.SendMessageAsync($"❌ Error: {ex.Message}\r\nContact @makefoxhelpbot for support.");
            }
            finally
            {
                if (user is not null)
                    user.Unlock();

                FoxContextManager.Clear();
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

            FoxLog.WriteLine($"{msg.ID}: Message: {t.User}" + (t.Chat is not null ? $" in {t.Chat}" : "") + $"> {ReplaceNonPrintableCharacters(msg.message)}", LogLevel.DEBUG);

            await FoxTelegram.WaitUntilReadyAsync();

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

                    await FoxCommandHandlerOld.HandleCommand(t, msg);
                    //FoxLog.WriteLine($"{msg.ID}: Finished processing input for {t.User.username}.");

                    //await DatabaseHandler.DisplayReceivedTelegramMessage(t.User.ID, message);
                    
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex, "HandleMessageAsync error: " + ex.Message);
                }
            }
        }

        private static async Task HandleDeleteMessagesAsync(int[] messages, long? channel_id = null)
        {
            try
            {
                FoxLog.WriteLine("Deleting messages (" + string.Join(",", messages) + ") channel_id = " + channel_id?.ToString() ?? "null");

                if (channel_id is null)
                    await FoxLLMConversation.DeleteConversationTelegramMessagesAsync(messages);

                return; //broken at the moment

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
                FoxLog.LogException(ex, "Error in HandleDeleteMessagesAsync: " + ex.Message);
            }
        }

        private static async Task LogTelegramUpdate(TL.Update update)
        {
            try
            {
                long? tlFromID = null;
                long? tlPeerID = null;
                int? tlMsgID = null;


                switch (update)
                {
                    case UpdateNewMessage unm:
                        tlFromID = unm.message?.From?.ID;
                        tlPeerID = unm.message?.Peer?.ID;
                        tlMsgID = unm.message?.ID;
                        break;
                    case UpdateEditMessage uem:
                        tlFromID = uem.message?.From?.ID;
                        tlPeerID = uem.message?.Peer?.ID;
                        tlMsgID = uem.message?.ID;
                        break;
                    case UpdateBotMessageReaction ubmr:
                        tlFromID = ubmr.actor?.ID;
                        tlPeerID = ubmr.peer?.ID;
                        tlMsgID = ubmr.msg_id;
                        break;
                    case UpdateBotCallbackQuery ubcq:
                        tlFromID = ubcq.user_id;
                        tlPeerID = ubcq.peer.ID;
                        tlMsgID = ubcq.msg_id;
                        break;
                    case UpdateDeleteMessages udm:
                        if (udm.messages.Count() == 1)
                            tlMsgID = udm.messages.First();
                        break;
                    case UpdateUserName uun:
                        tlFromID = uun.user_id;
                        tlPeerID = uun.user_id;
                        break;
                    case UpdateUserEmojiStatus uues:
                        tlFromID = uues.user_id;
                        tlPeerID = uues.user_id;
                        break;
                    case UpdateUser uu:
                        tlFromID = uu.user_id;
                        tlPeerID = uu.user_id;
                        break;
                    case UpdateChannelMessageViews ucmv:
                        tlPeerID = ucmv.channel_id;
                        tlMsgID = ucmv.id;
                        break;
                    case UpdateBotStopped ubs:
                        tlPeerID = ubs.user_id;
                        break;
                    case UpdateReadChannelOutbox urco:
                        tlPeerID = urco.channel_id;
                        break;
                    case UpdateReadChannelDiscussionOutbox urcdo:
                        tlPeerID = urcdo.channel_id;
                        break;
                    case UpdateChannelParticipant ucp:
                        tlPeerID = ucp.channel_id;
                        tlFromID = ucp.user_id;
                        break;
                    case UpdatePinnedMessages upm:
                        tlPeerID = upm.peer.ID;
                        tlMsgID = upm.messages.FirstOrDefault();
                        break;
                    case UpdateReadHistoryInbox urhi:
                        tlPeerID = urhi.peer.ID;
                        break;
                    case UpdateChannel uc:
                        tlPeerID = uc.channel_id;
                        break;
                    case UpdatePinnedForumTopics ucpts:
                        tlPeerID = ucpts.peer.ID;
                        break;
                    case UpdatePinnedForumTopic ucpt:
                        tlPeerID = ucpt.peer.ID;
                        break;
                    case UpdateBotChatInviteRequester ubcir:
                        tlPeerID = ubcir.peer.ID;
                        tlFromID = ubcir.user_id;
                        break;
                }

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "INSERT INTO telegram_update_log (type, from_id, peer_id, message_id, update_json, date) VALUES (@type, @from_id, @peer_id, @msg_id, @json, @now)";
                        cmd.Parameters.AddWithValue("type", update.GetType().Name);
                        cmd.Parameters.AddWithValue("from_id", tlFromID);
                        cmd.Parameters.AddWithValue("peer_id", tlPeerID);
                        cmd.Parameters.AddWithValue("msg_id", tlMsgID);
                        cmd.Parameters.AddWithValue("json", FoxStrings.SerializeToJson(update));
                        cmd.Parameters.AddWithValue("now", DateTime.Now);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);
            }
        }

        private static async Task LogTelegramMessage(TL.MessageBase message)
        {
            try
            {
                string? messageText = null;

                if (message is TL.Message msg)
                    messageText = msg.message;

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();

                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "INSERT INTO telegram_message_log (type, from_id, peer_id, message_id, message_text, message_json, date) VALUES (@type, @from_id, @peer_id, @msg_id, @text, @json, @now)";
                        cmd.Parameters.AddWithValue("type", message.GetType().Name);
                        cmd.Parameters.AddWithValue("from_id", message.From?.ID);
                        cmd.Parameters.AddWithValue("peer_id", message.Peer?.ID);
                        cmd.Parameters.AddWithValue("msg_id", message.ID);
                        cmd.Parameters.AddWithValue("text", messageText);
                        cmd.Parameters.AddWithValue("json", FoxStrings.SerializeToJson(message));
                        cmd.Parameters.AddWithValue("now", DateTime.Now);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);
            }
        }

        private static async Task HandleUpdateUsername(TL.User user, UpdateUserName newUserInfo)
        {
            try
            {
                var fUser = await FoxUser.GetByTelegramUser(user, false);

                // Find the first active username.
                TL.Username? activeUserName = newUserInfo.usernames.FirstOrDefault(u => u.flags.HasFlag(Username.Flags.active));
                if (activeUserName is null)
                    activeUserName = newUserInfo.usernames.FirstOrDefault();

                string? userName = activeUserName?.username ?? null;
                string? firstName = string.IsNullOrEmpty(newUserInfo.first_name) ? null : newUserInfo.first_name;
                string? lastName = string.IsNullOrEmpty(newUserInfo.last_name) ? null : newUserInfo.last_name;

                FoxContextManager.Current = new FoxContext
                {
                    Telegram = new FoxTelegram(user, null),
                    User = fUser
                };

                using (var SQL = new MySqlConnection(FoxMain.sqlConnectionString))
                {
                    await SQL.OpenAsync();
                    using (var cmd = new MySqlCommand())
                    {
                        cmd.Connection = SQL;
                        cmd.CommandText = "UPDATE telegram_users SET username = @username, firstname = @firstname, lastname = @lastname WHERE id = @id";
                        cmd.Parameters.AddWithValue("username", userName);
                        cmd.Parameters.AddWithValue("firstname", newUserInfo.first_name);
                        cmd.Parameters.AddWithValue("lastname", newUserInfo.last_name);
                        cmd.Parameters.AddWithValue("id", newUserInfo.user_id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                if (fUser is not null)
                    await fUser.SetUsername(userName);

                FoxLog.WriteLine(ReplaceNonPrintableCharacters($"Updated username for {user.id} to {userName}"));
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);
            }
            finally
            {
                FoxContextManager.Clear();
            }
        }

        private static async Task HandleUpdateAsync(UpdatesBase updates)
        {
            updates.CollectUsersChats(FoxTelegram.Users, FoxTelegram.Chats);

            await FoxTelegram.WaitUntilReadyAsync();

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
                    MessageBase? replyToMessage = null;

                    try
                    {
                        _= LogTelegramUpdate(update);

                        switch (update)
                        {
                            case UpdateNewAuthorization una:
                                break;

                            case UpdateNewMessage unm:
                                _ = LogTelegramMessage(unm.message);

                                switch (unm.message)
                                {
                                    case Message m:
                                        updates.Users.TryGetValue(m.From?.ID ?? m.Peer.ID, out user);
                                        updates.Chats.TryGetValue(m.Peer.ID, out chat);

                                        //FoxLog.WriteLine(FoxStrings.SerializeToJson(m));
                                        //FoxLog.WriteLine(m.message.Count().ToString());

                                        if (user is null)
                                        {
                                            FoxLog.WriteLine($"Weird message {m.ID}: {m.from_id} {m.peer_id} {m.GetType()}");
                                            continue;
                                        }

                                        t = new FoxTelegram(user, chat);
                                        replyToMessage = m;

                                        if (m.media is MessageMediaPhoto { photo: Photo photo })
                                        {
                                            await FoxImage.SaveImageFromTelegram(t, m, photo);
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

                                                updates.Users.TryGetValue(ms.From?.ID ?? ms.Peer.ID, out user);
                                                updates.Chats.TryGetValue(ms.Peer.ID, out chat);

                                                if (user is null)
                                                    throw new Exception("Invalid telegram user");

                                                t = new FoxTelegram(user, chat);
                                                replyToMessage = ms;

                                                _= HandlePayment(t, ms, payment);
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
                            case UpdateChannelParticipant ucp:
                                if (ucp.user_id == FoxTelegram.BotUser?.ID)
                                {
                                    // Bot was added or removed from a group

                                    if (ucp.prev_participant?.UserId == FoxTelegram.BotUser?.ID)
                                    {
                                        FoxLog.WriteLine($"Bot was removed from group {ucp.channel_id} by {ucp.actor_id}.");
                                    }
                                    else if (ucp.new_participant?.UserId == FoxTelegram.BotUser?.ID)
                                    {
                                        FoxLog.WriteLine($"Bot was added to group {ucp.channel_id} by {ucp.actor_id}.");

                                        updates.Users.TryGetValue(ucp.actor_id, out user);
                                        updates.Chats.TryGetValue(ucp.channel_id, out chat);

                                        //FoxLog.WriteLine(FoxStrings.SerializeToJson(m));
                                        //FoxLog.WriteLine(m.message.Count().ToString());

                                        if (user is not null && chat is not null)
                                        {
                                            t = new FoxTelegram(user, chat);

                                            StringBuilder sb = new();

                                            sb.AppendLine("🦊 Thank you for inviting me to your group!  I can help you make wonderful furry art.");
                                            sb.AppendLine();
                                            sb.AppendLine($"🎨 To get started, type /help@{FoxTelegram.BotUser?.MainUsername} to see what I can do.");
                                            sb.AppendLine();
                                            sb.AppendLine("📚 If you have any questions, feel free to contact @makefoxhelpbot, or check out our group @toomanyfoxes.");

                                            await t.SendMessageAsync(text: sb.ToString());
                                        }
                                    }
                                }
                                break;
                            case UpdateDeleteChannelMessages udcm:
                                await HandleDeleteMessagesAsync(udcm.messages, udcm.channel_id);
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

                                _= FoxCallbacks.Handle(t, ucbk, System.Text.Encoding.ASCII.GetString(ucbk.data));

                                break;
                            case UpdateBotPrecheckoutQuery upck:
                                await _client.Messages_SetBotPrecheckoutResults(upck.query_id, null, true);
                                break;
                            case UpdateReadChannelOutbox urco:
                                //User has read our messages (and we're an admin in the channel)
                                break;
                            case UpdateReadChannelDiscussionOutbox urcdo:
                                break;
                            case UpdateUserName uun:
                                user = new TL.User { id = uun.user_id };

                                _ = HandleUpdateUsername(user, uun);
                                break;
                            case UpdateBotChatInviteRequester ubcir:
                                // A user has requested to join a chat via an invite link

                                if (ubcir.peer.ID == 2048609895 || ubcir.peer.ID == 2184471767)
                                {
                                    // This is @toomanyfoxes and @soupfoxgames (for testing)

                                    updates.Users.TryGetValue(ubcir.user_id, out user);
                                    updates.Chats.TryGetValue(ubcir.peer.ID, out chat);

                                    if (user is null || chat is null)
                                        return;

                                    var targetUser = await FoxUser.GetByTelegramUser(user, false);

                                    if (targetUser is not null)
                                    {
                                        bool isBanned = (targetUser.GetAccessLevel() == AccessLevel.BANNED);

                                        try
                                        {
                                            if (!isBanned)
                                                await targetUser.Telegram.SendMessageAsync($"🦊 Welcome to @{chat.MainUsername}!  You have been automatically approved.");
                                            else
                                                await targetUser.Telegram.SendMessageAsync($"❌ You are banned and cannot join @{chat.MainUsername}.\r\n\r\n💬 Contact @makefoxhelpbot for assistance.");
                                        }
                                        catch (Exception ex)
                                        {
                                            FoxLog.LogException(ex);
                                        }

                                        await _client.Messages_HideChatJoinRequest(chat, user, !isBanned);
                                    }
                                }

                                break;

                            case UpdateUser uu:
                                // Handle user updates here.
                                break;
                            default:
                                FoxLog.WriteLine("Unexpected update type from Telegram: " + update.GetType().Name);
                                break; // there are much more update types than the above example cases
                        }
                    }
                    catch (Exception ex)
                    {
                        FoxLog.LogException(ex);

                        if (t is not null)
                        {
                            try
                            {
                                await t.SendMessageAsync(
                                    text: "❌ Error! \"" + ex.Message + "\"",
                                    replyToMessage: replyToMessage
                                );
                            } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FoxLog.LogException(ex);
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
                                FoxLog.LogException(ex, $"Error getting full user={user} x={rex.X} code={rex.Code} > {ex.Message}");
                            }
                            else
                                FoxLog.LogException(ex, $"Error getting full user={user} {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            FoxLog.LogException(ex, "Error getting full user={user}: " + ex.Message);
                        }

                        using (var cmd = new MySqlCommand())
                        {
                            cmd.Connection = SQL;
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                INSERT INTO telegram_users
                                (id, access_hash, active, type, language, username, firstname, lastname, bio, flags, flags2, date_added, date_updated, photo_id, photo, last_full_update)
                                VALUES 
                                (@id, @access_hash_always, @active, @type, @language, @username, @firstname, @lastname, @bio, @flags, @flags2, @date_updated, @date_updated, @photo_id, @photo, @last_full_update)
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
                            cmd.Parameters.AddWithValue("access_hash_always", user.access_hash);
                            cmd.Parameters.AddWithValue("access_hash", user.flags.HasFlag(User.Flags.min) ? null : user.access_hash);
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
                FoxLog.LogException(ex, $"updateTelegramUser error: chat={user.ID} {ex.Message}\r\n{ex.StackTrace}");
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
                                FoxLog.LogException(ex, $"Error getting full chat: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
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
                                    FoxLog.LogException(ex, $"Error getting group admins: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"updateTelegramChat error: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
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
                        FoxLog.LogException(ex, $"updateTelegramUsers error: chat={user.ID} {ex.Message}\r\n{ex.StackTrace}");
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"updateTelegramUsers error: {ex.Message}\r\n{ex.StackTrace}");
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
                    FoxLog.LogException(ex, $"updateTelegramChats error: chat={chat.ID} {ex.Message}\r\n{ex.StackTrace}");
                }
            }
        }

        public async Task<TL.Message?> GetReplyMessage(Message message)
        {

            if (message is null)
                return null; //Nothing we can do.

            try
            {
                if (message.ReplyTo is not null && message.ReplyTo is MessageReplyHeader mrh)
                {
                    switch (this.Peer)
                    {
                        case InputPeerChannel channel:
                            var rmsg = await FoxTelegram.Client.Channels_GetMessages(channel, new InputMessage[] { mrh.reply_to_msg_id });

                            if (rmsg is not null && rmsg.Messages is not null && rmsg.Messages.First() is not null && rmsg.Messages.First().From is not null)
                                return (Message)rmsg.Messages.First();
                            break;
                        case InputPeerChat chat:
                            var crmsg = await FoxTelegram.Client.Messages_GetMessages(new InputMessage[] { mrh.reply_to_msg_id });

                            if (crmsg is not null && crmsg.Messages is not null && crmsg.Messages.First() is not null && crmsg.Messages.First().From is not null)
                                return (Message)crmsg.Messages.First();
                            break;
                        case InputPeerUser user:
                            var umsg = await FoxTelegram.Client.Messages_GetMessages(new InputMessage[] { mrh.reply_to_msg_id });

                            if (umsg is not null && umsg.Messages is not null)
                                return (Message)umsg.Messages.First();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex);
            }

            return null;
        }

        public static void StopUpdates()
        {
            if (_client is not null)
                _client.OnUpdates -= HandleUpdateAsync;
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