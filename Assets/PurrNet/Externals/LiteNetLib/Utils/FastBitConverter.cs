using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace LiteNetLib.Utils
{
    public static class FastBitConverter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void GetBytes<T>(byte[] bytes, int startIndex, T value) where T : unmanaged
        {
            int size = sizeof(T);
            if (bytes.Length < startIndex + size)
                ThrowIndexOutOfRangeException();

            fixed (byte* ptr = &bytes[startIndex])
            {
                var valueBuffer = stackalloc T[1] { value };
                UnsafeUtility.MemCpy(ptr, valueBuffer, size);
            }
        }

        private static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();
    }
}
