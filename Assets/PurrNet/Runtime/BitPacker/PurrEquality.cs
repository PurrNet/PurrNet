using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PurrNet.Modules;
using Unity.Collections.LowLevel.Unsafe;

namespace PurrNet.Packing
{
    [UsedByIL]
    public static class PurrEquality
    {
        [UsedByIL]
        public static void Override<D>() where D : IPurrEquatable<D>
        {
            PurrEquality<D>.OverrideDefault(new PurrEqualityComparer<D>());
        }

        private sealed class PurrEqualityComparer<D> : IEqualityComparer<D> where D : IPurrEquatable<D>
        {
            public bool Equals(D x, D y)
            {
                if (x is null) return y is null;
                if (y is null) return false;
                return x.PurrEquals(y);
            }

            public int GetHashCode(D obj) => EqualityComparer<D>.Default.GetHashCode(obj);
        }

        // mirrors GenerateSerializersProcessor.ShouldIgnoreField: [DontPack] fields never hit the wire,
        // so a raw memcmp would compare state the receiver never sees
        internal static bool IsMemCmpComparable(Type type, HashSet<Type> visited)
        {
            if (type.IsPrimitive || type.IsEnum || type.IsPointer)
                return true;

            if (!visited.Add(type))
                return true;

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];

                if (field.IsDefined(typeof(DontPackAttribute), false) ||
                    field.FieldType.IsDefined(typeof(DontPackAttribute), false) ||
                    !IsMemCmpComparable(field.FieldType, visited))
                    return false;
            }

            return true;
        }
    }

    public static class PurrEquality<T>
    {
        public static IEqualityComparer<T> Default;

        internal static readonly bool memCmpComparable;

        static PurrEquality()
        {
            Default = EqualityComparer<T>.Default;
            memCmpComparable = !RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
                               PurrEquality.IsMemCmpComparable(typeof(T), new HashSet<Type>());
        }

        public static void OverrideDefault(IEqualityComparer<T> comparer)
        {
            Default = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool MemEquals(ref T a, ref T b)
        {
            return UnsafeUtility.MemCmp(
                Unsafe.AsPointer(ref a),
                Unsafe.AsPointer(ref b),
                Unsafe.SizeOf<T>()
            ) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), UsedByIL]
        public static bool Equals(T a, T b) => EqualsRef(ref a, ref b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EqualsRef(ref T a, ref T b)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() && memCmpComparable)
                return MemEquals(ref a, ref b);
            return Default.Equals(a, b);
        }
    }
}
