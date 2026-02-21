using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct ArrayComparator<T> : IEqualityComparer<T[]>
    {
        static readonly IEqualityComparer<T> eq = PurrEquality<T>.Default;

        public bool Equals(T[] x, T[] y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Length != y.Length) return false;

            int count = x.Length;

            for (int i = 0; i < count; i++)
            {
                if (!eq.Equals(x[i], y[i]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(T[] obj)
        {
            return obj.GetHashCode();
        }
    }
}
