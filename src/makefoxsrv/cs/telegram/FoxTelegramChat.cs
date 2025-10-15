//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;
//using MySqlConnector;
//using TL;

//namespace makefoxsrv.telegram
//{
//    internal class FoxTelegramChat : FoxTelegramPeerBase
//    {
//        private static readonly FoxCache<FoxTelegramChat> _cache = new FoxCache<FoxTelegramChat>(TimeSpan.FromHours(12));
//        private static readonly ConcurrentDictionary<long, SemaphoreSlim> _cacheLocks = new();

//        [DbColumn("id")]
//        public long Id { get; private set; }

//        [DbColumn("access_hash")]
//        public long? AccessHashValue { get; private set; }

//        [DbColumn("active")]
//        public bool? Active { get; private set; }

//        [DbColumn("type")]
//        public string Type { get; private set; } = "UNKNOWN";

//        [DbColumn("username")]
//        public string? Username { get; private set; }

//        [DbColumn("title")]
//        public string? Title { get; private set; }

//        [DbColumn("description")]
//        public string? Description { get; private set; }

//        [DbColumn("level")]
//        public int? Level { get; private set; }

//        [DbColumn("flags")]
//        public long? Flags { get; private set; }

//        [DbColumn("flags2")]
//        public long? Flags2 { get; private set; }

//        [DbColumn("admin_flags")]
//        public long? AdminFlags { get; private set; }

//        [DbColumn("participants")]
//        public int? Participants { get; private set; }

//        [DbColumn("photo_id")]
//        public long? PhotoId { get; private set; }

//        [DbColumn("photo")]
//        public byte[]? Photo { get; private set; }

//        [DbColumn("date_added")]
//        public DateTime? DateAdded { get; private set; }

//        [DbColumn("date_updated")]
//        public DateTime DateUpdated { get; private set; }

//        [DbColumn("last_full_update")]
//        public DateTime? LastFullUpdate { get; private set; }

//        public FoxTelegramChat()
//        {
//        }

//        public FoxTelegramChat(FoxTelegramBot bot, long peerId, long? accessHash = null)
//            : base(bot, peerId, accessHash)
//        {
//        }

//        public override InputPeer GetInputPeer()
//        {
//            //if (Type == "CHANNEL" || Type == "SUPERGROUP" || Type == "GIGAGROUP")
//            //{
//            //    return new InputPeerChannel
//            //    {
//            //        channel_id = PeerId,
//            //        access_hash = AccessHash ?? 0
//            //    };
//            //}

//            //return new InputPeerChat
//            //{
//            //    chat_id = PeerId
//            //};
//        }

//        protected override async Task SavePeerDetailsAsync()
//        {
//            await FoxDB.SaveObjectAsync(this, "telegram_chats");
//            Cache();
//        }

//        public static async Task<FoxTelegramChat?> LoadAsync(FoxTelegramBot bot, long peerId, long? accessHash = null)
//        {
//            var cached = _cache.Get(peerId);
//            if (cached is not null)
//                return cached;

//            var sem = _cacheLocks.GetOrAdd(peerId, _ => new SemaphoreSlim(1, 1));
//            await sem.WaitAsync();

//            try
//            {
//                cached = _cache.Get(peerId);
//                if (cached is not null)
//                    return cached;

//                var obj = await FoxDB.LoadObjectAsync<FoxTelegramChat>(
//                    "telegram_chats",
//                    "id = @id",
//                    new Dictionary<string, object?> { { "id", peerId } });

//                if (obj is null)
//                {
//                    obj = new FoxTelegramChat(bot, peerId, accessHash);
//                }

//                obj.Bot = bot;
//                obj.AccessHash = accessHash;
//                obj.BotId = (ulong)bot.Id;
//                obj.Cache();

//                _cache.Put(peerId, obj);
//                return obj;
//            }
//            finally
//            {
//                sem.Release();
//                if (sem.CurrentCount == 1)
//                    _cacheLocks.TryRemove(peerId, out _);
//            }
//        }

//        // Example of a chat-specific method
//        public async Task SetTopicTitleAsync(string title)
//        {
//            //if (Bot is null || Bot.Client is null)
//            //    throw new InvalidOperationException("Bot is not connected.");

//            //if (Type != "SUPERGROUP" && Type != "GIGAGROUP")
//            //    throw new InvalidOperationException("This operation is only valid for supergroups or gigagroups.");

//            //await Bot.Client.Channels_EditTitle(GetInputPeer() as InputPeerChannel, title);
//        }
//    }
//}
