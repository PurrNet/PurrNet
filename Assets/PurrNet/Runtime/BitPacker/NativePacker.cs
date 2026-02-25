using System;
using System.Runtime.CompilerServices;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class NativePacker<T>
    {
        public static unsafe delegate*<BitPacker, T, void> WriteFunc;
        public static unsafe delegate*<BitPacker, ref T, void> ReadFunc;

        static bool _hasWriter, _hasReader;

        static WriteFunc<T> _writeDelegate;
        static ReadFunc<T> _readDelegate;

        static unsafe NativePacker()
        {
            WriteFunc = &Packer.FallbackWriter;
            ReadFunc = &Packer.FallbackReader;
        }

        public static bool HasPacker()
        {
            return _hasWriter && _hasReader;
        }

        static void WriteDelegateFallback(BitPacker packer, T value)
        {
            _writeDelegate(packer, value);
        }

        static void ReadDelegateFallback(BitPacker packer, ref T value)
        {
            _readDelegate(packer, ref value);
        }

        public static unsafe void RegisterWriter(WriteFunc<T> write)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return;

            try
            {
                var ptr = (delegate*<BitPacker, T, void>)write.Method.MethodHandle.GetFunctionPointer();
                RegisterWriterWithPointer(ptr);
            }
            catch (NotSupportedException)
            {
                // IL2CPP doesn't support RuntimeMethodHandle.GetFunctionPointer()
                _writeDelegate = write;
                RegisterWriterWithPointer(&WriteDelegateFallback);
            }
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
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return;

            try
            {
                var ptr = (delegate*<BitPacker, ref T, void>)read.Method.MethodHandle.GetFunctionPointer();
                RegisterReaderWithPointer(ptr);
            }
            catch (NotSupportedException)
            {
                _readDelegate = read;
                RegisterReaderWithPointer(&ReadDelegateFallback);
            }
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
