using System.Collections.Generic;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PurrNet.Packing
{
    internal readonly struct DictionaryComparator<K, V> : IEqualityComparer<Dictionary<K, V>>
    {
        private static readonly bool CanUseDefaultComparerLookup = IsExactDefaultComparerKey();

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

            bool canUseDefaultLookup = CanUseDefaultLookup() &&
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

            int count = y.Count;
            var matched = ArrayPool<bool>.Shared.Rent(count);
            Array.Clear(matched, 0, count);

            try
            {
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
            finally
            {
                ArrayPool<bool>.Shared.Return(matched);
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

        private static bool IsExactDefaultComparerKey()
        {
            var type = typeof(K);
            if (type.IsEnum || type == typeof(IntPtr) || type == typeof(UIntPtr))
                return true;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }

        private static bool CanUseDefaultLookup()
        {
            return CanUseDefaultComparerLookup ||
                   (RuntimeHelpers.IsReferenceOrContainsReferences<K>() &&
                    ReferenceEquals(PurrEquality<K>.Default, EqualityComparer<K>.Default));
        }

        public int GetHashCode(Dictionary<K, V> obj)
        {
            return obj.GetHashCode();
        }
    }
}
