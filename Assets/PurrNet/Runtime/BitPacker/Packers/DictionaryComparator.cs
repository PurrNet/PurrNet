using System.Collections.Generic;
using PurrNet.Pooling;

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

            using var xKeys = DisposableList<K>.Create(x.Keys);
            using var yKeys = DisposableList<K>.Create(y.Keys);
            using var xValues = DisposableList<V>.Create(x.Values);
            using var yValues = DisposableList<V>.Create(y.Values);

            if (!xKeys.Equals(yKeys))
                return false;
            return xValues.Equals(yValues);
        }

        public int GetHashCode(Dictionary<K, V> obj)
        {
            return obj.GetHashCode();
        }
    }
}