using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct DictionaryComparator<K, V> : IEqualityComparer<Dictionary<K, V>>
    {
        private enum EnumerationResult : byte
        {
            Equal,
            Ambiguous,
            Different
        }

        public bool Equals(Dictionary<K, V> x, Dictionary<K, V> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Count != y.Count) return false;

            bool canUseDefaultLookup = DefaultComparerLookup<K>.CanUse() &&
                                       ReferenceEquals(x.Comparer, EqualityComparer<K>.Default) &&
                                       ReferenceEquals(y.Comparer, EqualityComparer<K>.Default);
            switch (CompareEnumerations(x, y, canUseDefaultLookup))
            {
                case EnumerationResult.Equal:
                    return true;
                case EnumerationResult.Different:
                    return false;
            }

            if (canUseDefaultLookup)
            {
                foreach (var pair in x)
                {
                    if (!y.TryGetValue(pair.Key, out var yValue) ||
                        !PurrEquality<V>.Equals(pair.Value, yValue))
                        return false;
                }

                return true;
            }

            return MultisetEquality.MatchedEquals<PairSource, KeyValuePair<K, V>, Dictionary<K, V>.Enumerator>(
                new PairSource(x), new PairSource(y), y.Count);
        }

        private readonly struct PairSource : IMultisetSource<KeyValuePair<K, V>, Dictionary<K, V>.Enumerator>
        {
            private readonly Dictionary<K, V> _value;

            public PairSource(Dictionary<K, V> value)
            {
                _value = value;
            }

            public Dictionary<K, V>.Enumerator GetEnumerator() => _value.GetEnumerator();

            public bool ElementEquals(in KeyValuePair<K, V> a, in KeyValuePair<K, V> b)
            {
                return PurrEquality<K>.Equals(a.Key, b.Key) &&
                       PurrEquality<V>.Equals(a.Value, b.Value);
            }
        }

        private static EnumerationResult CompareEnumerations(
            Dictionary<K, V> x,
            Dictionary<K, V> y,
            bool keysAreUniqueUnderPurrEquality)
        {
            using var xEnumerator = x.GetEnumerator();
            using var yEnumerator = y.GetEnumerator();

            while (xEnumerator.MoveNext())
            {
                if (!yEnumerator.MoveNext())
                    return EnumerationResult.Different;

                var xCurrent = xEnumerator.Current;
                var yCurrent = yEnumerator.Current;
                if (!PurrEquality<K>.Equals(xCurrent.Key, yCurrent.Key))
                    return EnumerationResult.Ambiguous;

                if (!PurrEquality<V>.Equals(xCurrent.Value, yCurrent.Value))
                {
                    return keysAreUniqueUnderPurrEquality
                        ? EnumerationResult.Different
                        : EnumerationResult.Ambiguous;
                }
            }

            return yEnumerator.MoveNext() ? EnumerationResult.Different : EnumerationResult.Equal;
        }

        public int GetHashCode(Dictionary<K, V> obj)
        {
            return obj.GetHashCode();
        }
    }
}
