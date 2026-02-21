using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace PurrNet.Packing
{
    /// <summary>Value equality for <see cref="NativeArray{T}"/>. Uses raw memory comparison when both are created and same length (no per-element loop over T).</summary>
    internal readonly unsafe struct NativeArrayComparator<T> : IEqualityComparer<NativeArray<T>> where T : unmanaged
    {
        public bool Equals(NativeArray<T> x, NativeArray<T> y)
        {
            bool xCreated = x.IsCreated;
            bool yCreated = y.IsCreated;
            if (!xCreated && !yCreated) return true;
            if (!xCreated || !yCreated) return false;
            int len = x.Length;
            if (len != y.Length) return false;
            if (len == 0) return true;

            return MemEqual(ref x, ref y, len);
        }

        static bool MemEqual(ref NativeArray<T> a, ref NativeArray<T> b, int length)
        {
            if (a.Length < length || b.Length < length) return false;

            int byteCount = length * Unsafe.SizeOf<T>();

            var ptrA = a.GetUnsafeReadOnlyPtr();
            var ptrB = b.GetUnsafeReadOnlyPtr();

            return UnsafeUtility.MemCmp(ptrA, ptrB, byteCount) == 0;
        }

        public int GetHashCode(NativeArray<T> array)
        {
            if (!array.IsCreated || array.Length == 0)
                return 0;

            int length = array.Length;
            int byteLength = length * UnsafeUtility.SizeOf<T>();
            var ptr = array.GetUnsafeReadOnlyPtr();
            var hash = xxHash3.Hash64(ptr, byteLength, seed: 0);
            return (int)math.hash(hash);
        }
    }

    /// <summary>Value equality for <see cref="NativeList{T}"/>. Uses raw memory comparison when both are created and same length (no per-element loop over T).</summary>
    internal readonly unsafe struct NativeListComparator<T> : IEqualityComparer<NativeList<T>> where T : unmanaged
    {
        public bool Equals(NativeList<T> x, NativeList<T> y)
        {
            bool xCreated = x.IsCreated;
            bool yCreated = y.IsCreated;
            if (!xCreated && !yCreated) return true;
            if (!xCreated || !yCreated) return false;
            int len = x.Length;
            if (len != y.Length) return false;
            if (len == 0) return true;

            return MemEqual(x, y, len);
        }

        static bool MemEqual(NativeList<T> a, NativeList<T> b, int length)
        {
            if (a.Length < length || b.Length < length) return false;

            int byteCount = length * Unsafe.SizeOf<T>();

            void* ptrA = a.GetUnsafeReadOnlyPtr();
            void* ptrB = b.GetUnsafeReadOnlyPtr();

            return UnsafeUtility.MemCmp(ptrA, ptrB, byteCount) == 0;
        }

        public int GetHashCode(NativeList<T> list)
        {
            if (!list.IsCreated || list.Length == 0)
                return 0;

            int length = list.Length;
            int byteLength = length * UnsafeUtility.SizeOf<T>();
            void* ptr = list.GetUnsafeReadOnlyPtr();

            var hash = xxHash3.Hash64(ptr, byteLength, seed: 0);
            return (int)math.hash(hash);
        }
    }
}
