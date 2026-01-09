using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct NullableComparator<T> : IEqualityComparer<T?> where T : struct
    {
        static readonly IEqualityComparer<T> eq = PurrEquality<T>.Default;

        public bool Equals(T? x, T? y)
        {
            if (x.HasValue != y.HasValue)
                return false;

            if (!x.HasValue)
                return true;

            return eq.Equals(x.Value, y.Value);
        }

        public int GetHashCode(T? obj)
        {
            return obj.GetHashCode();
        }
    }
}
