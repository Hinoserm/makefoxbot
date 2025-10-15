//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;
//using TL;

//namespace makefoxsrv.telegram
//{
//    internal static class FoxTelegramPeer
//    {
//        private static readonly FoxCache<FoxTelegramPeerBase> _cache = new FoxCache<FoxTelegramPeerBase>(TimeSpan.FromHours(12));
//        private static readonly ConcurrentDictionary<long, SemaphoreSlim> _cacheLocks = new();

//        public static async Task<FoxTelegramPeerBase?> GetAsync(FoxTelegramBot bot, long peerId)
//        {
//            var sem = _cacheLocks.GetOrAdd(peerId, _ => new SemaphoreSlim(1, 1));
//            await sem.WaitAsync();

//            try
//            {
//                var cached = _cache.Get(peerId);
//                if (cached is not null)
//                    return cached;

//                var (accessHash, peerType) = await FoxTelegramPeerBase.LoadAccessHashAsync(bot, peerId);
//                if (peerType is null)
//                    return null;

//                FoxTelegramPeerBase? peer = peerType switch
//                {
//                    "USER" => await FoxTelegramUser.LoadAsync(bot, peerId, accessHash),
//                    "CHAT" => await FoxTelegramChat.LoadAsync(bot, peerId, accessHash),
//                    _ => null
//                };

//                if (peer is not null)
//                    _cache.Put(peerId, peer);

//                return peer;
//            }
//            finally
//            {
//                sem.Release();
//                if (sem.CurrentCount == 1)
//                    _cacheLocks.TryRemove(peerId, out _);
//            }
//        }

//        public static async Task<FoxTelegramPeerBase> GetOrCreateAsync(FoxTelegramBot bot, TL.User user)
//        {
//            var sem = _cacheLocks.GetOrAdd(user.ID, _ => new SemaphoreSlim(1, 1));
//            await sem.WaitAsync();

//            try
//            {
//                var cached = _cache.Get(user.ID);
//                if (cached is not null)
//                {
//                    var usr = (FoxTelegramUser)cached;
//                    bool changed = false;

//                    if (usr.AccessHash != user.access_hash)
//                    {
//                        usr.UpdateAccessHash(user.access_hash);
//                        changed = true;
//                    }

//                    if (usr.Username != user.username)
//                    {
//                        usr.UpdateUsername(user.username);
//                        changed = true;
//                    }

//                    if (changed)
//                        await usr.SaveAsync();

//                    return usr;
//                }

//                var peer = await GetAsync(bot, user.ID);
//                if (peer is not null)
//                    return peer;

//                var created = new FoxTelegramUser(bot, user);
//                await created.SaveAsync();
//                _cache.Put(user.ID, created);
//                return created;
//            }
//            finally
//            {
//                sem.Release();
//                if (sem.CurrentCount == 1)
//                    _cacheLocks.TryRemove(user.ID, out _);
//            }
//        }

//        public static async Task<FoxTelegramPeerBase> GetOrCreateAsync(FoxTelegramBot bot, TL.ChatBase chat)
//        {
//            var sem = _cacheLocks.GetOrAdd(chat.ID, _ => new SemaphoreSlim(1, 1));
//            await sem.WaitAsync();

//            try
//            {
//                long? newAccessHash = (chat is Channel ch && ch.access_hash != 0) ? ch.access_hash : null;

//                var cached = _cache.Get(chat.ID);
//                if (cached is not null)
//                {
//                    var chp = (FoxTelegramChat)cached;
//                    bool changed = false;

//                    if (chp.AccessHash != newAccessHash)
//                    {
//                        chp.UpdateAccessHash(newAccessHash);
//                        changed = true;
//                    }

//                    if (chp.Username != chat.MainUsername)
//                    {
//                        chp.UpdateUsername(chat.MainUsername);
//                        changed = true;
//                    }

//                    if (changed)
//                        await chp.SaveAsync();

//                    return chp;
//                }

//                var peer = await GetAsync(bot, chat.ID);
//                if (peer is not null)
//                    return peer;

//                var created = new FoxTelegramChat(bot, chat);
//                await created.SaveAsync();
//                _cache.Put(chat.ID, created);
//                return created;
//            }
//            finally
//            {
//                sem.Release();
//                if (sem.CurrentCount == 1)
//                    _cacheLocks.TryRemove(chat.ID, out _);
//            }
//        }
//    }
//}
