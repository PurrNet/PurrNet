using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PurrNet.Packing
{
    internal static class DefaultComparerLookup<T>
    {
        private static readonly bool _matchesPurrEquality = MatchesPurrEquality();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanUse()
        {
            return _matchesPurrEquality ||
                   (RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
                    ReferenceEquals(PurrEquality<T>.Default, EqualityComparer<T>.Default));
        }

        private static bool MatchesPurrEquality()
        {
            var type = typeof(T);
            if (IsExactDefaultComparerType(type))
                return true;

            return !RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
                   PurrEquality<T>.memCmpComparable &&
                   DefaultEqualityMatchesMemCmp(type, new HashSet<Type>());
        }

        private static bool IsExactDefaultComparerType(Type type)
        {
            if (type.IsEnum || type == typeof(IntPtr) || type == typeof(UIntPtr) || type == typeof(Guid))
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

        // EqualityComparer<T>.Default must agree with memcmp: no float bit-pattern aliasing (+0/-0, NaN)
        // and no user Equals/IEquatable that could diverge from bitwise equality
        private static bool DefaultEqualityMatchesMemCmp(Type type, HashSet<Type> visited)
        {
            if (type == typeof(float) || type == typeof(double))
                return false;

            if (type.IsPrimitive || type.IsEnum || type.IsPointer)
                return true;

            if (!visited.Add(type))
                return true;

            if (OverridesEquality(type))
                return false;

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < fields.Length; i++)
            {
                if (!DefaultEqualityMatchesMemCmp(fields[i].FieldType, visited))
                    return false;
            }

            return true;
        }

        private static bool OverridesEquality(Type type)
        {
            var interfaces = type.GetInterfaces();

            for (int i = 0; i < interfaces.Length; i++)
            {
                var candidate = interfaces[i];
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEquatable<>))
                    return true;
            }

            return type.GetMethod(nameof(Equals),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, new[] { typeof(object) }, null) != null;
        }
    }

    internal interface IMultisetSource<TElement, TEnumerator>
        where TEnumerator : struct, IEnumerator<TElement>
    {
        TEnumerator GetEnumerator();

        bool ElementEquals(in TElement a, in TElement b);
    }

    internal static class MultisetEquality
    {
        public static bool MatchedEquals<TSource, TElement, TEnumerator>(TSource x, TSource y, int yCount)
            where TSource : struct, IMultisetSource<TElement, TEnumerator>
            where TEnumerator : struct, IEnumerator<TElement>
        {
            var matched = ArrayPool<bool>.Shared.Rent(yCount);
            Array.Clear(matched, 0, yCount);

            try
            {
                var xEnumerator = x.GetEnumerator();

                try
                {
                    while (xEnumerator.MoveNext())
                    {
                        var xCurrent = xEnumerator.Current;
                        int index = 0;
                        bool found = false;
                        var yEnumerator = y.GetEnumerator();

                        try
                        {
                            while (yEnumerator.MoveNext())
                            {
                                if (!matched[index] && x.ElementEquals(xCurrent, yEnumerator.Current))
                                {
                                    matched[index] = true;
                                    found = true;
                                    break;
                                }

                                index++;
                            }
                        }
                        finally
                        {
                            yEnumerator.Dispose();
                        }

                        if (!found)
                            return false;
                    }

                    return true;
                }
                finally
                {
                    xEnumerator.Dispose();
                }
            }
            finally
            {
                ArrayPool<bool>.Shared.Return(matched);
            }
        }
    }
}
