using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    [UsedByIL]
    public static class PurrEquality
    {
        [UsedByIL]
        public static void Override<D>() where D : IPurrEquatable<D>
        {
            PurrEquality<D>.OverrideDefault(new PurrEqualityComparer<D>());
        }

        private sealed class PurrEqualityComparer<D> : IEqualityComparer<D> where D : IPurrEquatable<D>
        {
            public bool Equals(D x, D y)
            {
                if (x is null) return y is null;
                if (y is null) return false;
                return x.PurrEquals(y);
            }

            public int GetHashCode(D obj) => EqualityComparer<D>.Default.GetHashCode(obj);
        }
    }

    public static class PurrEquality<T>
    {
        public static IEqualityComparer<T> Default;

        static PurrEquality()
        {
            Default = EqualityComparer<T>.Default;
        }

        public static void OverrideDefault(IEqualityComparer<T> comparer)
        {
            Default = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), UsedByIL]
        public static bool Equals(T a, T b) => Default.Equals(a, b);
    }
}
