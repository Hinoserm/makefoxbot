using System;
using System.Collections.Concurrent;
using System.Threading;

namespace makefoxsrv
{
    public class FoxCache<T> where T : class
    {
        private class CacheEntry
        {
            public T? StrongRef { get; private set; }
            public WeakReference<T> WeakRef { get; }
            private readonly TimeSpan _ttl;
            private readonly bool _sliding;
            private DateTime _expiry;

            public CacheEntry(T value, TimeSpan ttl, bool sliding)
            {
                StrongRef = value;
                WeakRef = new WeakReference<T>(value);
                _ttl = ttl;
                _sliding = sliding;
                _expiry = DateTime.Now.Add(ttl);
            }

            public T? GetValue()
            {
                if (DateTime.Now <= _expiry)
                {
                    // accessed while still alive → refresh sliding window
                    if (_sliding)
                        _expiry = DateTime.Now.Add(_ttl);
                    return StrongRef;
                }

                // expired → drop strong ref, fall back to weak
                StrongRef = null;
                return WeakRef.TryGetTarget(out var v) ? v : null;
            }

            public void Refresh()
            {
                if (_sliding && StrongRef != null)
                    _expiry = DateTime.Now.Add(_ttl);
            }

            public bool IsDead()
            {
                if (DateTime.Now <= _expiry)
                    return false; // still alive in sliding window

                // expired → release strong reference
                StrongRef = null;
                return !WeakRef.TryGetTarget(out _);
            }
        }

        private readonly ConcurrentDictionary<ulong, CacheEntry> _cache = new();
        private readonly TimeSpan _ttl;
        private readonly bool _sliding;
        private readonly Timer _cleanupTimer;

        public FoxCache(TimeSpan? ttl = null, bool sliding = true)
        {
            _ttl = ttl ?? TimeSpan.FromHours(1);
            _sliding = sliding;
            _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        public T? Get(ulong id)
        {
            if (_cache.TryGetValue(id, out var entry))
            {
                var value = entry.GetValue();
                if (value is not null)
                    return value;

                _cache.TryRemove(id, out _); // dead, drop it
            }
            return null;
        }

        public void Put(ulong id, T value)
        {
            _cache[id] = new CacheEntry(value, _ttl, _sliding);
        }

        public IEnumerable<T> Values
        {
            get
            {
                foreach (var kv in _cache)
                {
                    var value = kv.Value.GetValue();
                    if (value is not null)
                        yield return value;
                }
            }
        }

        public IEnumerable<(ulong Key, T Value)> Entries
        {
            get
            {
                foreach (var kv in _cache)
                {
                    var value = kv.Value.GetValue();
                    if (value is not null)
                        yield return (kv.Key, value);
                }
            }
        }

        private void Cleanup()
        {
            foreach (var kv in _cache.ToArray())
            {
                if (kv.Value.IsDead())
                    _cache.TryRemove(kv.Key, out _);
            }
        }

        public int Count => _cache.Count;
    }
}
