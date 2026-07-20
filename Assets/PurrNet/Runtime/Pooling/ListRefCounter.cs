using System.Collections.Generic;

namespace PurrNet.Pooling
{
    internal sealed class ListRefCounter
    {
        public int refs;
    }

    internal static class ListRefCounterPool
    {
        private static readonly object _lock = new object();
        private static Stack<ListRefCounter> _pool;

        private static Stack<ListRefCounter> pool => _pool ??= new Stack<ListRefCounter>();

        public static ListRefCounter Rent()
        {
            lock (_lock)
            {
                var counters = pool;
                var counter = counters.Count > 0 ? counters.Pop() : new ListRefCounter();
                counter.refs = 1;
                return counter;
            }
        }

        public static void Return(ListRefCounter counter)
        {
            lock (_lock)
            {
                pool.Push(counter);
            }
        }
    }
}
