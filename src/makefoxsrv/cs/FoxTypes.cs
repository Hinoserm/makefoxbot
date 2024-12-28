using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace makefoxsrv
{

    public class BoundedDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> items = new Dictionary<TKey, TValue>();
        private readonly object syncRoot = new object();
        private readonly int maxItemCount;

        // Custom strategy to determine which keys to remove
        public Func<Dictionary<TKey, TValue>, IEnumerable<TKey>>? RemovalStrategy { get; set; } = null;

        public BoundedDictionary(int maxCount)
        {
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount), "Maximum count must be greater than zero.");

            maxItemCount = maxCount;
        }

        /// <summary>
        /// Finds the first value in the dictionary that matches the given predicate, or returns the default value if no match is found.
        /// </summary>
        /// <param name="match">A predicate function to test each value.</param>
        /// <returns>The first matching value, or the default value of TValue if no match is found.</returns>
        public TValue? FirstOrDefault(Func<TValue?, bool> match)
        {
            if (match == null)
                throw new ArgumentNullException(nameof(match));

            lock (syncRoot)
            {
                return items.Values.FirstOrDefault(match);
            }
        }


        /// <summary>
        /// Finds all values in the dictionary that match the given predicate.
        /// </summary>
        /// <param name="match">A predicate function to test each value.</param>
        /// <returns>A list of matching values.</returns>
        public IEnumerable<TValue> FindAll(Func<TValue, bool> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));

            lock (syncRoot)
            {
                return items.Values.Where(match).ToList();
            }
        }

        /// <summary>
        /// Finds all key-value pairs that match the given predicate.
        /// </summary>
        /// <param name="match">A predicate function to test each item.</param>
        /// <returns>A list of matching key-value pairs.</returns>
        public IEnumerable<KeyValuePair<TKey, TValue>> FindAll(Func<KeyValuePair<TKey, TValue>, bool> match)
        {
            if (match == null)
                throw new ArgumentNullException(nameof(match));

            lock (syncRoot)
            {
                return items.Where(match).ToList();
            }
        }

        public TValue? this[TKey key]
        {
            get
            {
                lock (syncRoot)
                {
                    items.TryGetValue(key, out var value);
                    return value;
                }
            }
        }

        public TValue? Get(TKey key)
        {
            lock (syncRoot)
            {
                items.TryGetValue(key, out var value);
                return value;
            }
        }

        public void Add(TKey key, TValue value)
        {
            lock (syncRoot)
            {
                if (key == null)
                    throw new ArgumentNullException(nameof(key));

                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                // Only apply removal logic if maxItemCount is above 0.
                if (maxItemCount > 0 && !items.ContainsKey(key))
                {
                    // Remove items if necessary
                    RemoveItemsByStrategy();
                }

                items[key] = value;
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (syncRoot)
            {
                return items.TryGetValue(key, out value);
            }
        }

        public bool Remove(TKey key)
        {
            lock (syncRoot)
            {
                return items.Remove(key);
            }
        }

        public IEnumerable<TKey> Keys
        {
            get
            {
                lock (syncRoot)
                {
                    return items.Keys.ToList();
                }
            }
        }

        public IEnumerable<TValue> Values
        {
            get
            {
                lock (syncRoot)
                {
                    return items.Values.ToList();
                }
            }
        }

        public int Count
        {
            get
            {
                lock (syncRoot)
                {
                    return items.Count;
                }
            }
        }

        private bool RemoveItemsByStrategy()
        {
            if (RemovalStrategy == null)
                throw new InvalidOperationException("RemovalStrategy cannot be null.");

            // Get the keys to remove
            var keysToRemove = RemovalStrategy(items).ToList();

            foreach (var key in keysToRemove)
            {
                if (items.Count <= maxItemCount)
                    break; // Stop if we're within the size limit

                items.Remove(key);
            }

            return (keysToRemove.Count > 0);
        }
    }

    public class LimitedMemoryQueue<T> : Queue<T>
    {
        private readonly int maxItemCount;
        private readonly object syncRoot = new object(); // Object to lock on

        // Custom dequeue strategy
        public Func<IEnumerable<T>, T> DequeueStrategy { get; set; }

        public LimitedMemoryQueue(int itemLimit)
        {
            maxItemCount = itemLimit;
            DequeueStrategy = DefaultDequeueStrategy; // Default strategy removes the oldest item
        }

        public new void Enqueue(T item)
        {
            lock (syncRoot)
            {
                // Check for null if T is a reference type
                if (item == null && !typeof(T).IsValueType)
                {
                    throw new ArgumentNullException(nameof(item), "Cannot insert null into the queue");
                }

                while (Count >= maxItemCount)
                {
                    RemoveItemByStrategy();
                }
                base.Enqueue(item);
            }
        }

        private void RemoveItemByStrategy()
        {
            if (DequeueStrategy == null)
                throw new InvalidOperationException("DequeueStrategy cannot be null.");

            // Get the item to dequeue based on the strategy
            var itemToRemove = DequeueStrategy(this);

            // Remove the item explicitly
            var items = this.ToList();
            items.Remove(itemToRemove);

            // Clear and re-enqueue items to apply the updated order
            Clear();
            foreach (var item in items)
            {
                base.Enqueue(item);
            }
        }

        private T DefaultDequeueStrategy(IEnumerable<T> items)
        {
            return items.First(); // Default: dequeue the oldest item
        }

        public IEnumerable<T> FindAll(Func<T, bool> match)
        {
            lock (syncRoot)
            {
                return this.Where(match).ToList();
            }
        }
    }
}

