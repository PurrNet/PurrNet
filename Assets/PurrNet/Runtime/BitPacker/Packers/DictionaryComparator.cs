using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct DictionaryComparator<K, V> : IEqualityComparer<Dictionary<K, V>>
    {
        public bool Equals(Dictionary<K, V> x, Dictionary<K, V> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Count != y.Count) return false;

            var matched = new bool[y.Count];

            foreach (var xCurrent in x)
            {
                int index = 0;
                bool found = false;
                foreach (var yCurrent in y)
                {
                    if (!matched[index] && PurrEquality<K>.Equals(xCurrent.Key, yCurrent.Key) &&
                        PurrEquality<V>.Equals(xCurrent.Value, yCurrent.Value))
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

        public int GetHashCode(Dictionary<K, V> obj)
        {
            return obj.GetHashCode();
        }
    }
}
