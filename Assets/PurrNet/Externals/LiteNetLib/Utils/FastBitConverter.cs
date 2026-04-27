using System;
using System.Runtime.CompilerServices;

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
            Unsafe.WriteUnaligned(ref bytes[startIndex], value);
        }

        private static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();
    }
}
