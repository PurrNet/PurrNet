using System.Runtime.CompilerServices;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class NativePacker<T>
    {
        public static unsafe delegate*<BitPacker, T, void> WriteFunc;
        public static unsafe delegate*<BitPacker, ref T, void> ReadFunc;

        static bool _hasWriter, _hasReader;

        static unsafe NativePacker()
        {
            WriteFunc = &Packer.FallbackWriter;
            ReadFunc = &Packer.FallbackReader;
        }

        public static bool HasPacker()
        {
            return _hasWriter && _hasReader;
        }

        public static unsafe void RegisterWriter(WriteFunc<T> write)
        {
            var handle = write.Method.MethodHandle;
            var ptr = (delegate*<BitPacker, T, void>)handle.GetFunctionPointer();
            RegisterWriterWithPointer(ptr);
        }

        static unsafe void RegisterWriterWithPointer(delegate*<BitPacker, T, void> ptr)
        {
            if (_hasWriter)
                return;

            _hasWriter = true;
            WriteFunc = ptr;
        }

        public static unsafe void RegisterReader(ReadFunc<T> read)
        {
            var handle = read.Method.MethodHandle;
            var ptr = (delegate*<BitPacker, ref T, void>)handle.GetFunctionPointer();
            RegisterReaderWithPointer(ptr);
        }

        static unsafe void RegisterReaderWithPointer(delegate*<BitPacker, ref T, void> ptr)
        {
            if (_hasReader)
                return;

            _hasReader = true;
            ReadFunc = ptr;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Write(BitPacker packer, T value)
        {
            WriteFunc(packer, value);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Read(BitPacker packer, ref T value)
        {
            ReadFunc(packer, ref value);
        }
    }
}
