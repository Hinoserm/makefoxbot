using System;
using System.Collections.Concurrent;
using System.Threading;

namespace makefoxsrv
{
    /// <summary>
    /// Thread-safe in-memory cache with optional sliding or absolute expiration.
    /// Entries are strongly referenced until expiry, then fall back to weak references
    /// until garbage collected or removed by cleanup.
    /// </summary>
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

            public T? GetValue(bool slide)
            {
                if (DateTime.Now <= _expiry)
                {
                    // accessed while still alive → refresh sliding window
                    if (_sliding && slide)
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


        /// <summary>
        /// Creates a new cache.
        /// </summary>
        /// <param name="ttl">
        /// Time-to-live for each entry. Defaults to one hour if not specified.
        /// </param>
        /// <param name="sliding">
        /// If true, each successful <see cref="Get"/> resets the expiry window (sliding expiration).
        /// If false, entries expire a fixed interval after insertion regardless of access.
        /// </param>
        public FoxCache(TimeSpan? ttl = null, bool sliding = true)
        {
            _ttl = ttl ?? TimeSpan.FromHours(1);
            _sliding = sliding;
            _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// Retrieves an item from the cache and counts as an access. 
        /// If sliding expiration is enabled, this call will refresh the expiry.
        /// Returns <c>null</c> if the item is expired or missing.
        /// </summary>
        public T? Get(ulong id) => GetInternal(id, slide: true);

        /// <summary>
        /// Retrieves an item from the cache and counts as an access. 
        /// If sliding expiration is enabled, this call will refresh the expiry.
        /// Returns <c>null</c> if the item is expired or missing.
        /// </summary>
        public T? Get(long id) => GetInternal((ulong)id, slide: true);

        /// <summary>
        /// Retrieves an item from the cache without extending its lifetime. 
        /// Useful for inspection (e.g., enumeration) where you don't want 
        /// iteration itself to keep entries alive. 
        /// Returns <c>null</c> if the item is expired or missing.
        /// </summary>
        public T? Peek(ulong id) => GetInternal(id, slide: false);

        private T? GetInternal(ulong id, bool slide)
        {
            if (_cache.TryGetValue(id, out var entry))
            {
                var value = entry.GetValue(slide);
                if (value is not null)
                    return value;

                _cache.TryRemove(id, out _);
            }
            return null;
        }


        /// <summary>
        /// Adds or replaces a value in the cache with the given key.
        /// Starts its lifetime window immediately.
        /// </summary>
        /// <param name="id">The cache key.</param>
        /// <param name="value">The value to store.</param>
        public void Put(ulong id, T value)
        {
            _cache[id] = new CacheEntry(value, _ttl, _sliding);
        }

        public void Put(long id, T value) => Put((ulong)id, value);


        /// <summary>
        /// Enumerates the values currently in the cache. 
        /// Expired items are skipped, and enumeration does NOT 
        /// count as access for sliding expiration.
        /// </summary>
        public IEnumerable<T> Values
        {
            get
            {
                foreach (var kv in _cache)
                {
                    var value = kv.Value.GetValue(false);
                    if (value is not null)
                        yield return value;
                }
            }
        }

        /// <summary>
        /// Enumerates the key/value pairs currently in the cache. 
        /// Expired items are skipped, and enumeration does NOT 
        /// count as access for sliding expiration.
        /// </summary>
        public IEnumerable<(ulong Key, T Value)> Entries
        {
            get
            {
                foreach (var kv in _cache)
                {
                    var value = kv.Value.GetValue(false);
                    if (value is not null)
                        yield return (kv.Key, value);
                }
            }
        }

        /// <summary>
        /// Gets the current number of entries stored in the cache. 
        /// This count may include expired entries that have not yet been collected.
        /// </summary>
        public int Count => _cache.Count;

        private void Cleanup()
        {
            foreach (var kv in _cache.ToArray())
            {
                if (kv.Value.IsDead())
                  _cache.TryRemove(kv.Key, out _);
            }
        }


    }
}
