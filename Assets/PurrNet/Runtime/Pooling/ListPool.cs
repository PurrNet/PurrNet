using System;
using System.Collections.Generic;

namespace PurrNet.Pooling
{
    public class ListPool<T> : GenericPool<List<T>>
    {
#if UNITY_EDITOR
        [ThreadStatic]
#endif
        private static ListPool<T> _instance;

        private const int CapacityClasses = 32;

        private readonly Stack<List<T>>[] _classes = new Stack<List<T>>[CapacityClasses];
        private int _count;
        private int _lastReturnedClass;

        static ListPool() => _instance = new ListPool<T>();

        static List<T> Factory() => new List<T>();

        static void Reset(List<T> list) => list.Clear();

        public ListPool() : base(Factory, Reset)
        {
        }

        public override int count => _count;

        public static int GetCount() => _instance.count;

        static int ClassOf(int capacity)
        {
            int c = 0;
            while (capacity > 1)
            {
                capacity >>= 1;
                c++;
            }
            return c;
        }

        static int FitClass(int minCapacity) => minCapacity <= 1 ? 0 : ClassOf(minCapacity - 1) + 1;

        // Prefers the class of the last returned list, so a rent right after a return reuses it.
        public override List<T> Allocate()
        {
            var recent = _classes[_lastReturnedClass];
            if (recent != null && recent.Count > 0)
            {
                _count--;
                return recent.Pop();
            }

            for (var c = 0; c < CapacityClasses; c++)
            {
                var pooled = _classes[c];
                if (pooled != null && pooled.Count > 0)
                {
                    _count--;
                    return pooled.Pop();
                }
            }

            return Factory();
        }

        /// <summary>
        /// Rents a list with room for at least <paramref name="minCapacity"/> items: the smallest pooled
        /// list that fits, or a new one sized to its class boundary so it returns to the class that
        /// later rents of this size search.
        /// </summary>
        public List<T> Allocate(int minCapacity)
        {
            if (minCapacity <= 0)
                return Allocate();

            int fit = FitClass(minCapacity);
            for (var c = fit; c < CapacityClasses; c++)
            {
                var pooled = _classes[c];
                if (pooled != null && pooled.Count > 0)
                {
                    _count--;
                    return pooled.Pop();
                }
            }

            return new List<T>(fit < CapacityClasses - 1 ? 1 << fit : minCapacity);
        }

        public override void Delete(List<T> list)
        {
            Reset(list);
            int c = ClassOf(list.Capacity);
            (_classes[c] ??= new Stack<List<T>>()).Push(list);
            _lastReturnedClass = c;
            _count++;
        }

        public static List<T> Instantiate()
        {
#if UNITY_EDITOR
            _instance ??= new ListPool<T>();
#endif
            var allocated =  _instance.Allocate();
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(allocated);
#endif
            return allocated;
        }

        /// <summary>
        /// Rents a list with room for at least <paramref name="minCapacity"/> items.
        /// </summary>
        public static List<T> Instantiate(int minCapacity)
        {
#if UNITY_EDITOR
            _instance ??= new ListPool<T>();
#endif
            var allocated = _instance.Allocate(minCapacity);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(allocated);
#endif
            return allocated;
        }

        public static void Destroy(List<T> list)
        {
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UnTrack(list);
#endif
            _instance.Delete(list);
        }
    }
}
