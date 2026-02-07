using System.Collections.Generic;

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
