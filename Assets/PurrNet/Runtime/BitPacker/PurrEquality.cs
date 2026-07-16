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

        internal readonly struct MemCmpRange
        {
            public readonly int offset;
            public readonly int size;

            public MemCmpRange(int offset, int size)
            {
                this.offset = offset;
                this.size = size;
            }
        }

        internal static MemCmpRange[] CollectMemCmpRanges(Type type)
        {
            var ranges = new List<MemCmpRange>();
            CollectMemCmpRanges(type, 0, ranges);
            ranges.Sort((a, b) => a.offset.CompareTo(b.offset));

            var merged = new List<MemCmpRange>(ranges.Count);

            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];

                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];

                    if (range.offset <= last.offset + last.size)
                    {
                        int end = Math.Max(last.offset + last.size, range.offset + range.size);
                        merged[merged.Count - 1] = new MemCmpRange(last.offset, end - last.offset);
                        continue;
                    }
                }

                merged.Add(range);
            }

            return merged.ToArray();
        }

        private static void CollectMemCmpRanges(Type type, int baseOffset, List<MemCmpRange> ranges)
        {
            if (type.IsPrimitive || type.IsEnum || type.IsPointer)
            {
                ranges.Add(new MemCmpRange(baseOffset, SizeOfLeaf(type)));
                return;
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];

                if (field.IsDefined(typeof(DontPackAttribute), false) ||
                    field.FieldType.IsDefined(typeof(DontPackAttribute), false))
                    continue;

                int offset = baseOffset + UnsafeUtility.GetFieldOffset(field);

                // fixed buffers expose a single element field; the wire carries the whole buffer
                if (field.IsDefined(typeof(FixedBufferAttribute), false))
                    ranges.Add(new MemCmpRange(offset, SizeOfLeaf(field.FieldType)));
                else
                    CollectMemCmpRanges(field.FieldType, offset, ranges);
            }
        }

        private static readonly MethodInfo _sizeOfOpenGeneric = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf));

        private static int SizeOfLeaf(Type type)
        {
            if (type.IsPointer)
                return IntPtr.Size;
            return (int)_sizeOfOpenGeneric.MakeGenericMethod(type).Invoke(null, null);
        }
    }

    public static class PurrEquality<T>
    {
        public static IEqualityComparer<T> Default;

        internal static readonly bool memCmpComparable;
        internal static readonly PurrEquality.MemCmpRange[] packedMemCmpRanges;

        private static readonly IEqualityComparer<T> _builtInDefault;

        static PurrEquality()
        {
            Default = EqualityComparer<T>.Default;
            _builtInDefault = Default;

            bool unmanaged = !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            memCmpComparable = unmanaged && PurrEquality.IsMemCmpComparable(typeof(T), new HashSet<Type>());

            if (unmanaged && !memCmpComparable)
            {
                // a failing type initializer would poison PurrEquality<T> forever; fall back to Default instead
                try
                {
                    packedMemCmpRanges = PurrEquality.CollectMemCmpRanges(typeof(T));
                }
                catch
                {
                    packedMemCmpRanges = null;
                }
            }
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool PackedMemEquals(ref T a, ref T b)
        {
            byte* ptrA = (byte*)Unsafe.AsPointer(ref a);
            byte* ptrB = (byte*)Unsafe.AsPointer(ref b);
            var ranges = packedMemCmpRanges;

            for (int i = 0; i < ranges.Length; i++)
            {
                var range = ranges[i];

                if (UnsafeUtility.MemCmp(ptrA + range.offset, ptrB + range.offset, range.size) != 0)
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), UsedByIL]
        public static bool Equals(T a, T b) => EqualsRef(ref a, ref b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EqualsRef(ref T a, ref T b)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                if (memCmpComparable)
                    return MemEquals(ref a, ref b);

                // a registered override (IPurrEquatable, even one registered late) must still win
                if (packedMemCmpRanges != null && ReferenceEquals(Default, _builtInDefault))
                    return PackedMemEquals(ref a, ref b);
            }

            return Default.Equals(a, b);
        }
    }
}
