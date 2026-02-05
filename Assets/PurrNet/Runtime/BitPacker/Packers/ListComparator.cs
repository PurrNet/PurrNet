using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct ListComparator<T> : IEqualityComparer<List<T>>
    {
        public bool Equals(List<T> x, List<T> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Count != y.Count) return false;

            int count = x.Count;
            var elementEquality = PurrEquality<T>.Default;

            for (int i = 0; i < count; i++)
            {
                if (!elementEquality.Equals(x[i], y[i]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(List<T> obj)
        {
            return obj.GetHashCode();
        }
    }
}
