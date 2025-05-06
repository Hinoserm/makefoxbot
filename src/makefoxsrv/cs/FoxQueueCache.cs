using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace makefoxsrv
{
    static class FoxQueueCache
    {
        private static readonly Dictionary<ulong, CacheEntry> _cache = new();
        private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

        private static readonly TimeSpan _imageLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan _strongLifetime = TimeSpan.FromHours(24);
        private static readonly int _maxSize = 20000;
        private static readonly object _lock = new();

        private class CacheEntry
        {
            public WeakReference<FoxQueue> WeakRef { get; }
            public FoxQueue? StrongRef { get; private set; }
            public DateTime LastAccess { get; private set; }
            public DateTime AddedAt { get; }

            public CacheEntry(FoxQueue item)
            {
                StrongRef = item;
                WeakRef = new WeakReference<FoxQueue>(item);
                LastAccess = DateTime.Now;
                AddedAt = DateTime.Now;
            }

            public void Touch() => LastAccess = DateTime.Now;

            public void MaybeDropStrongRef(DateTime now)
            {
                if (StrongRef != null && now - AddedAt > _strongLifetime)
                    StrongRef = null;
            }

            public FoxQueue? GetTarget()
            {
                return StrongRef ?? (WeakRef.TryGetTarget(out var obj) ? obj : null);
            }
        }

        public static FoxQueue? Get(ulong id)
        {
            if (_cache.TryGetValue(id, out var entry))
            {
                var now = DateTime.Now;
                entry.MaybeDropStrongRef(now);
                entry.Touch();
                return entry.GetTarget();
            }

            return null;
        }

        public static void Add(FoxQueue item)
        {
            lock (_lock)
            {
                _cache[item.ID] = new CacheEntry(item);
            }
        }

        public static async Task Lock(ulong id)
        {
            var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            // Debug
            //FoxLog.WriteLine($"[LOCK] {id} acquired by {Environment.CurrentManagedThreadId}");
        }

        public static void Unlock(ulong id)
        {
            if (_locks.TryGetValue(id, out var gate))
            {
                gate.Release();

                // Debug
                //FoxLog.WriteLine($"[UNLOCK] {id} released by {Environment.CurrentManagedThreadId}");
            }
            else
            {
                FoxLog.WriteLine($"[UNLOCK] {id} not found in locks");
            }
        }

        public static void Touch(ulong id)
        {
            if (_cache.TryGetValue(id, out var entry))
                entry.Touch();
        }

        public static IEnumerable<FoxQueue> Where(Func<FoxQueue, bool> predicate)
        {
            foreach (var entry in _cache.Values)
            {
                var now = DateTime.Now;
                entry.MaybeDropStrongRef(now);

                var obj = entry.GetTarget();
                if (obj != null && predicate(obj))
                {
                    entry.Touch();
                    yield return obj;
                }
            }
        }

        public static IEnumerable<FoxQueue> Values => _cache.Values
            .Select(entry => {
                entry.MaybeDropStrongRef(DateTime.Now);
                return entry.GetTarget();
            })
            .Where(obj => obj != null)!;

        public static List<FoxQueue> FindAll(Func<FoxQueue, bool> predicate) =>
            Where(predicate).ToList();

        public static FoxQueue? FindFirstOrDefault(Func<FoxQueue, bool> predicate)
        {
            foreach (var entry in _cache.Values)
            {
                entry.MaybeDropStrongRef(DateTime.Now);

                var obj = entry.GetTarget();
                if (obj != null && predicate(obj))
                    return obj;
            }

            return default;
        }

        public static int Count() =>
            _cache.Count();

        private static bool CanEvict(CacheEntry entry)
        {
            var obj = entry.GetTarget();

            if (obj != null)
            {
                // Never evict active queues
                if (obj.status != FoxQueue.QueueStatus.CANCELLED &&
                    obj.status != FoxQueue.QueueStatus.FINISHED)
                    return false;

                // Allow unload image if needed
                // if (obj.Image != null && DateTime.Now - entry.LastAccess > _imageLifetime)
                //     obj.UnloadImage();

                return false; // still alive, but finished/cancelled — don't evict yet
            }

            // Object is gone — but if we had a strong ref, we might still know the status
            if (entry.StrongRef != null)
            {
                var status = entry.StrongRef.status;
                if (status != FoxQueue.QueueStatus.CANCELLED &&
                    status != FoxQueue.QueueStatus.FINISHED)
                    return false;
            }

            return true;
        }

        // Crazy aggressive for testing purposes
        [Cron(seconds: 1)]
        public static void Cleanup()
        {
            var now = DateTime.Now;
            var keysToRemove = new List<ulong>();

            foreach (var (id, entry) in _cache.ToList())
            {
                entry.MaybeDropStrongRef(now);

                var obj = entry.GetTarget();

                if (obj != null)
                {
                    // if (obj.Image != null && now - entry.LastAccess > _imageLifetime)
                    //     obj.UnloadImage();
                }

                if (CanEvict(entry))
                    keysToRemove.Add(id);

                if (_cache.Count - keysToRemove.Count <= _maxSize)
                    break;
            }

            foreach (var id in keysToRemove)
                RemoveEntry(id);
        }

        private static void RemoveEntry(ulong id)
        {
            _cache.Remove(id);
            _locks.TryRemove(id, out _);
        }
    }
}
