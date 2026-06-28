using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct HashsetComparator<T> : IEqualityComparer<HashSet<T>>
    {
        public bool Equals(HashSet<T> x, HashSet<T> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Count != y.Count) return false;

            var equality = PurrEquality<T>.Default;
            using var xEnumerator = x.GetEnumerator();
            using var yEnumerator = y.GetEnumerator();

            while (xEnumerator.MoveNext())
            {
                if (!yEnumerator.MoveNext())
                    return false;
                if (!equality.Equals(xEnumerator.Current, yEnumerator.Current))
                    return false;
            }

            return !yEnumerator.MoveNext();
        }

        public int GetHashCode(HashSet<T> obj)
        {
            return obj.GetHashCode();
        }
    }
}
