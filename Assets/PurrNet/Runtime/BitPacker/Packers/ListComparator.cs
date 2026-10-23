using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PurrNet.Packing
{
    internal readonly struct ListComparator<T> : IEqualityComparer<List<T>>
    {
        public bool Equals(List<T> x, List<T> y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            if (ReferenceEquals(x, y)) return true;
            if (x.Count != y.Count) return false;

            int count = x.Count;

            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() && ListItems.supported)
            {
                return count == 0 || PurrEquality<T>.MemEquals(
                    ref ListItems.Get(x)[0], ref ListItems.Get(y)[0], count);
            }

            for (int i = 0; i < count; i++)
            {
                if (!PurrEquality<T>.Equals(x[i], y[i]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(List<T> obj)
        {
            return obj.GetHashCode();
        }
    }

    /// <summary>
    /// Reads a list's backing array, which List declares as its first field
    /// (Unity's NoAllocHelpers relies on the same layout).
    /// </summary>
    internal static class ListItems
    {
        private sealed class Layout<T>
        {
            public T[] items;
        }

        internal static readonly bool supported = Probe();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T[] Get<T>(List<T> list) => Unsafe.As<Layout<T>>(list).items;

        private static bool Probe()
        {
            try
            {
                var list = new List<int>(4) { 1 };
                var field = typeof(List<int>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
                return field != null && ReferenceEquals(field.GetValue(list), Unsafe.As<Layout<int>>(list).items);
            }
            catch
            {
                return false;
            }
        }
    }
}
