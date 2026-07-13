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

            var matched = new bool[y.Count];

            foreach (var xValue in x)
            {
                int index = 0;
                bool found = false;
                foreach (var yValue in y)
                {
                    if (!matched[index] && PurrEquality<T>.Equals(xValue, yValue))
                    {
                        matched[index] = true;
                        found = true;
                        break;
                    }
                    index++;
                }

                if (!found)
                    return false;
            }

            return true;
        }

        public int GetHashCode(HashSet<T> obj)
        {
            return obj.GetHashCode();
        }
    }
}
