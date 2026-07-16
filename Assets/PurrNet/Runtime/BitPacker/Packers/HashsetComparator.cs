using System.Collections.Generic;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PurrNet.Packing
{
    internal readonly struct HashsetComparator<T> : IEqualityComparer<HashSet<T>>
    {
        private static readonly bool CanUseDefaultComparerLookup = IsExactDefaultComparerElement();

        public bool Equals(HashSet<T> x, HashSet<T> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Count != y.Count) return false;

            if (EnumerationsMatch(x, y))
                return true;

            if (CanUseDefaultLookup() &&
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

            int count = y.Count;
            var matched = ArrayPool<bool>.Shared.Rent(count);
            Array.Clear(matched, 0, count);

            try
            {
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
            finally
            {
                ArrayPool<bool>.Shared.Return(matched);
            }
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

        private static bool IsExactDefaultComparerElement()
        {
            var type = typeof(T);
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
                   (RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
                    ReferenceEquals(PurrEquality<T>.Default, EqualityComparer<T>.Default));
        }

        public int GetHashCode(HashSet<T> obj)
        {
            return obj.GetHashCode();
        }
    }
}
