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

        public LimitedMemoryQueue(int itemLimit)
        {
            maxItemCount = itemLimit;
        }

        public new void Enqueue(T item)
        {
            while (Count >= maxItemCount)
            {
                Dequeue(); // Remove oldest items to make space
            }
            base.Enqueue(item);
        }

        // New method to retrieve items matching a specific condition
        public IEnumerable<T> FindAll(Func<T, bool> match)
        {
            return this.Where(match).ToList();
        }
    }

}
