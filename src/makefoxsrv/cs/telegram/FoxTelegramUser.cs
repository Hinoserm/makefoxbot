//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;
//using MySqlConnector;
//using TL;

//namespace makefoxsrv.telegram
//{
//    internal class FoxTelegramUser : FoxTelegramPeerBase
//    {
//        private static readonly FoxCache<FoxTelegramUser> _cache = new FoxCache<FoxTelegramUser>(TimeSpan.FromHours(12));
//        private static readonly ConcurrentDictionary<long, SemaphoreSlim> _cacheLocks = new();

//        [DbColumn("id")]
//        public long Id { get; private set; }

//        [DbColumn("access_hash")]
//        public long AccessHashValue { get; private set; }

//        [DbColumn("type")]
//        public string Type { get; private set; } = "USER";

//        [DbColumn("active")]
//        public bool? Active { get; private set; }

//        [DbColumn("language")]
//        public string? Language { get; private set; }

//        [DbColumn("username")]
//        public string? Username { get; private set; }

//        [DbColumn("firstname")]
//        public string? FirstName { get; private set; }

//        [DbColumn("lastname")]
//        public string? LastName { get; private set; }

//        [DbColumn("bio")]
//        public string? Bio { get; private set; }

//        [DbColumn("flags")]
//        public int? Flags { get; private set; }

//        [DbColumn("flags2")]
//        public int? Flags2 { get; private set; }

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

//        public FoxTelegramUser()
//        {
//        }

//        public FoxTelegramUser(FoxTelegramBot bot, long peerId, long? accessHash = null)
//            : base(bot, peerId, accessHash)
//        {
//        }

//        //public override InputPeer GetInputPeer()
//        //{
//        //    //return new InputPeerUser
//        //    //{
//        //    //    user_id = PeerId,
//        //    //    access_hash = AccessHash ?? 0
//        //    //};
//        //}

//        //public override async Task SendMessageAsync(string message)
//        //{
//        //    if (Bot is null)
//        //        throw new InvalidOperationException("Bot reference is null.");

//        //    //if (Bot.Client is null)
//        //    //    throw new InvalidOperationException("Bot client is not initialized.");

//        //    //await Bot.Client.SendMessageAsync(GetInputPeer(), message);
//        //}

//        protected override async Task SavePeerDetailsAsync()
//        {
//            await FoxDB.SaveObjectAsync(this, "telegram_users");
//            Cache();
//        }

//        public static async Task<FoxTelegramUser?> LoadAsync(FoxTelegramBot bot, long peerId, long? accessHash = null)
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

//                var obj = await FoxDB.LoadObjectAsync<FoxTelegramUser>(
//                    "telegram_users",
//                    "id = @id",
//                    new Dictionary<string, object?> { { "id", peerId } });

//                if (obj is null)
//                {
//                    // fallback creation path
//                    obj = new FoxTelegramUser(bot, peerId, accessHash);
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
//    }
//}
