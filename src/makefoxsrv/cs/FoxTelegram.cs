using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TL;
using WTelegram;

namespace makefoxsrv
{
    internal class FoxTelegram
    {
        public WTelegram.Client botClient;
        public TL.User User {
            get => _User ?? throw new InvalidOperationException("User is null");
        }

        public TL.ChatBase? Chat { get => _Chat; }
        public TL.InputPeer Peer { get => _Peer; }

        private TL.User? _User;
        private TL.ChatBase? _Chat;
        private TL.InputPeer? _Peer;

        private long _UserId;
        private long? _ChatId;

        public static readonly Dictionary<long, User> Users = [];
        public static readonly Dictionary<long, ChatBase> Chats = [];

        public FoxTelegram(WTelegram.Client botClient, User user, ChatBase? chat)
        {
            this.botClient = botClient;
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

        public FoxTelegram(WTelegram.Client botClient, long userId, long userAccessHash, long? chatId = null, long? chatAccessHash = null)
        {
            this.botClient = botClient;

            _UserId = userId;
            _ChatId = chatId;

            Users.TryGetValue(userId, out this._User);
            if (chatId is not null)
                Chats.TryGetValue((long)chatId, out this._Chat);

            if (chatId is not null && chatAccessHash is not null)
            {
                _Peer = new InputPeerChannel(chatId.Value, chatAccessHash.Value);
            } else
            {
                _Peer = new InputPeerUser(userId, userAccessHash);
            }
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

        public async Task<Message> SendMessageAsync(string text, int replyToMessageId = 0, ReplyInlineMarkup? replyInlineMarkup = null, MessageEntity[]? entities = null,
            bool disableWebPagePreview = true)
        {
            long random_id = Helpers.RandomLong();

            var updates = await botClient.Messages_SendMessage(
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

        public async Task EditMessageAsync(string text, int id, ReplyInlineMarkup? replyInlineMarkup = null)
        {
            await botClient.Messages_EditMessage(
                peer: _Peer,
                message: text,
                id: id,
                reply_markup: replyInlineMarkup
            );
        }

        public async Task DeleteMessage(int id)
        {
            await botClient.DeleteMessages(
                peer: _Peer,
                id: [ id ]
            );
        }
    }
}
