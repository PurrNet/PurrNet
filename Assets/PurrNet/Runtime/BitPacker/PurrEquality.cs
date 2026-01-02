using System.Collections.Generic;

namespace PurrNet.Packing
{
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

        public static bool Equals(T a, T b) => Default.Equals(a, b);
    }
}
