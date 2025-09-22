using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace makefoxsrv
{
    public class FoxCacheOld<T> where T : class
    {
        private readonly Func<T, ulong> _idAccessor;
        private readonly TimeSpan _strongLifetime;
        private readonly int _maxSize;
        private readonly Func<T, bool> _retentionPredicate;
        private readonly Action<T, CacheEntry>? _evictionAction;

        private readonly Dictionary<ulong, CacheEntry> _cache = new();
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();
        private readonly object _sync = new();

        private static readonly object _instanceLock = new();
        private static readonly List<IFoxCache> _globalFoxCaches = new();

        public class CacheEntry
        {
            public WeakReference<T> WeakRef { get; }
            public T? StrongRef { get; private set; }
            public DateTime LastAccess { get; private set; }
            public DateTime AddedAt { get; }

            public CacheEntry(T item)
            {
                StrongRef = item;
                WeakRef = new WeakReference<T>(item);
                LastAccess = DateTime.Now;
                AddedAt = DateTime.Now;
            }

            public void Touch() => LastAccess = DateTime.Now;

            public void MaybeDropStrongRef(TimeSpan strongLifetime, DateTime now)
            {
                if (StrongRef != null && now - AddedAt > strongLifetime)
                    StrongRef = null;
            }

            public T? GetTarget() =>
                StrongRef ?? (WeakRef.TryGetTarget(out var obj) ? obj : null);

            public bool IsEvictable(T? obj, Func<T, bool> retentionPredicate)
            {
                if (obj != null && retentionPredicate(obj)) return false;
                if (obj != null) return false;
                if (StrongRef != null && retentionPredicate(StrongRef)) return false;
                return true;
            }

            public void RestoreStrongRef(T? item) => StrongRef = item;
        }

        public FoxCacheOld(
            Func<T, ulong> idAccessor,
            TimeSpan strongLifetime,
            int maxSize,
            Func<T, bool>? retentionPredicate = null,
            Action<T, CacheEntry>? evictionAction = null)
        {
            _idAccessor = idAccessor;
            _strongLifetime = strongLifetime;
            _maxSize = maxSize;
            _retentionPredicate = retentionPredicate ?? (_ => false);
            _evictionAction = evictionAction;

            lock (_instanceLock)
                _globalFoxCaches.Add(new Wrapper(this));
        }

        public async Task Lock(ulong id)
        {
            var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
        }

        public void Unlock(ulong id)
        {
            if (_locks.TryGetValue(id, out var gate))
                gate.Release();
        }

        public T? Get(ulong id)
        {
            lock (_sync)
            {
                if (_cache.TryGetValue(id, out var entry))
                {
                    var now = DateTime.Now;
                    entry.MaybeDropStrongRef(_strongLifetime, now);
                    entry.Touch();
                    return entry.GetTarget();
                }
                return null;
            }
        }

        public void Add(T item)
        {
            var id = _idAccessor(item);
            lock (_sync)
            {
                _cache[id] = new CacheEntry(item);
            }
        }

        public void Touch(ulong id)
        {
            lock (_sync)
            {
                if (_cache.TryGetValue(id, out var entry))
                    entry.Touch();
            }
        }

        public IEnumerable<T> Where(Func<T, bool> predicate)
        {
            List<CacheEntry> snapshot;
            lock (_sync)
            {
                snapshot = _cache.Values.ToList();
            }

            foreach (var entry in snapshot)
            {
                var now = DateTime.Now;
                entry.MaybeDropStrongRef(_strongLifetime, now);

                var obj = entry.GetTarget();
                if (obj != null && predicate(obj))
                {
                    entry.Touch();
                    yield return obj;
                }
            }
        }

        public IEnumerable<T> Values
        {
            get
            {
                List<CacheEntry> snapshot;
                lock (_sync)
                {
                    snapshot = _cache.Values.ToList();
                }

                foreach (var entry in snapshot)
                {
                    entry.MaybeDropStrongRef(_strongLifetime, DateTime.Now);
                    var obj = entry.GetTarget();
                    if (obj != null)
                        yield return obj;
                }
            }
        }

        public List<T> FindAll(Func<T, bool> predicate) =>
            Where(predicate).ToList();

        public T? FindFirstOrDefault(Func<T, bool> predicate)
        {
            List<CacheEntry> snapshot;
            lock (_sync)
            {
                snapshot = _cache.Values.ToList();
            }

            foreach (var entry in snapshot)
            {
                var now = DateTime.Now;
                entry.MaybeDropStrongRef(_strongLifetime, now);

                var obj = entry.GetTarget();
                if (obj != null && predicate(obj))
                    return obj;
            }
            return default;
        }

        public int Count()
        {
            lock (_sync)
            {
                return _cache.Count;
            }
        }

        public void Cleanup()
        {
            List<KeyValuePair<ulong, CacheEntry>> snapshot;
            lock (_sync)
            {
                snapshot = _cache.ToList();
            }

            var now = DateTime.Now;
            var toRemove = new List<ulong>();

            foreach (var kv in snapshot)
            {
                var id = kv.Key;
                var entry = kv.Value;

                entry.MaybeDropStrongRef(_strongLifetime, now);

                var obj = entry.GetTarget();
                if (obj != null)
                {
                    _evictionAction?.Invoke(obj, entry);
                }

                if (entry.IsEvictable(obj, _retentionPredicate))
                    toRemove.Add(id);

                if (snapshot.Count - toRemove.Count <= _maxSize)
                    break;
            }

            if (toRemove.Count > 0)
            {
                lock (_sync)
                {
                    foreach (var id in toRemove)
                    {
                        _cache.Remove(id);
                        _locks.TryRemove(id, out _);
                    }
                }
            }
        }

        private interface IFoxCache
        {
            void Cleanup();
        }

        private class Wrapper : IFoxCache
        {
            private readonly FoxCacheOld<T> _target;
            public Wrapper(FoxCacheOld<T> target) => _target = target;
            public void Cleanup() => _target.Cleanup();
        }

        // Global cleanup for all FoxCache<T> instances
        internal static void GlobalCleanup()
        {
            List<IFoxCache> snapshot;
            lock (_instanceLock)
                snapshot = _globalFoxCaches.ToList();

            foreach (var cache in snapshot)
            {
                try { cache.Cleanup(); }
                catch (Exception ex)
                {
                    FoxLog.WriteLine($"[FoxCache] Cleanup failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public int Clear(bool honorRetention = true)
        {
            int removed = 0;
            var now = DateTime.Now;
            List<ulong> ids;
            var lockedEntries = new Dictionary<ulong, SemaphoreSlim>();

            // First pass: acquire locks and drop strong refs
            lock (_sync)
                ids = _cache.Keys.ToList();

            foreach (var id in ids)
            {
                if (!_locks.TryGetValue(id, out var gate))
                    continue;

                if (!gate.Wait(0))
                    continue;

                bool found = false;

                try
                {
                    lock (_sync)
                    {
                        if (_cache.TryGetValue(id, out var entry))
                        {
                            entry.RestoreStrongRef(null);
                            found = true;
                        }
                    }
                }
                catch
                {
                    gate.Release();
                    continue;
                }

                if (found)
                    lockedEntries[id] = gate;
                else
                    gate.Release();
            }

            // Trigger GC after nuking strong refs
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Second pass: check WeakRef and decide
            foreach (var (id, gate) in lockedEntries)
            {
                bool keep = false;

                try
                {
                    CacheEntry? entry;

                    lock (_sync)
                    {
                        if (!_cache.TryGetValue(id, out entry) || entry is null)
                            continue;
                    }

                    // if ANY reference exists, we must keep it
                    if (entry.WeakRef.TryGetTarget(out var alive))
                    {
                        entry.RestoreStrongRef(alive);
                        keep = true;
                    }
                    else if (honorRetention && entry.StrongRef != null && _retentionPredicate(entry.StrongRef))
                    {
                        entry.RestoreStrongRef(entry.StrongRef);
                        keep = true;
                    }

                    if (!keep)
                    {
                        lock (_sync)
                            _cache.Remove(id);

                        _locks.TryRemove(id, out _);
                        removed++;
                    }
                }
                finally
                {
                    if (keep)
                        gate.Release();
                    // no release if deleted — gate is invalid now
                }
            }

            return removed;
        }
    }

    // Registered global cleanup for all FoxCache<T> instances
    internal static class FoxCacheJanitor
    {
        [Cron(minutes: 5)]
        public static void Cleanup()
        {
            FoxCacheOld<object>.GlobalCleanup();
        }
    }
}
