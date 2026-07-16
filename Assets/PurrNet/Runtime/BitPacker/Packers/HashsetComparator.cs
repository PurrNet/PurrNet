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

            if (EnumerationsMatch(x, y))
                return true;

            if (DefaultComparerLookup<T>.CanUse() &&
                ReferenceEquals(x.Comparer, EqualityComparer<T>.Default) &&
                ReferenceEquals(y.Comparer, EqualityComparer<T>.Default))
            {
                foreach (var value in x)
                {
                    if (!y.Contains(value))
                        return false;
                }

                return true;
            }

            return MultisetEquality.MatchedEquals<ElementSource, T, HashSet<T>.Enumerator>(
                new ElementSource(x), new ElementSource(y), y.Count);
        }

        private readonly struct ElementSource : IMultisetSource<T, HashSet<T>.Enumerator>
        {
            private readonly HashSet<T> _value;

            public ElementSource(HashSet<T> value)
            {
                _value = value;
            }

            public HashSet<T>.Enumerator GetEnumerator() => _value.GetEnumerator();

            public bool ElementEquals(in T a, in T b) => PurrEquality<T>.Equals(a, b);
        }

        private static bool EnumerationsMatch(HashSet<T> x, HashSet<T> y)
        {
            using var xEnumerator = x.GetEnumerator();
            using var yEnumerator = y.GetEnumerator();

            while (xEnumerator.MoveNext())
            {
                if (!yEnumerator.MoveNext())
                    return false;

                if (!PurrEquality<T>.Equals(xEnumerator.Current, yEnumerator.Current))
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
