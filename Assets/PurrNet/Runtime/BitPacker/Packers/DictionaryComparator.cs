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

            var keyEquality = PurrEquality<K>.Default;
            var valueEquality = PurrEquality<V>.Default;

            using var xEnumerator = x.GetEnumerator();
            using var yEnumerator = y.GetEnumerator();

            while (xEnumerator.MoveNext())
            {
                if (!yEnumerator.MoveNext())
                    return false;

                var xCurrent = xEnumerator.Current;
                var yCurrent = yEnumerator.Current;

                if (!keyEquality.Equals(xCurrent.Key, yCurrent.Key))
                    return false;
                if (!valueEquality.Equals(xCurrent.Value, yCurrent.Value))
                    return false;
            }

            return !yEnumerator.MoveNext();
        }

        public int GetHashCode(Dictionary<K, V> obj)
        {
            return obj.GetHashCode();
        }
    }
}
