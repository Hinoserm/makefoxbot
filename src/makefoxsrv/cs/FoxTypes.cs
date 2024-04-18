using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// A collection of useful custom types for the project.

namespace makefoxsrv
{
    public class LimitedMemoryQueue<T> : Queue<T>
    {
        private readonly int maxItemCount;
        private readonly object syncRoot = new object(); // Object to lock on

        public LimitedMemoryQueue(int itemLimit)
        {
            maxItemCount = itemLimit;
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
                    Dequeue(); // Remove oldest items to make space
                }
                base.Enqueue(item);
            }
        }

        public IEnumerable<T> FindAll(Func<T, bool> match)
        {
            lock (syncRoot)
            {
                return this.Where(match).ToList();
            }
        }

        // Ensure other methods that modify or read the queue are also synchronized
    }
}
