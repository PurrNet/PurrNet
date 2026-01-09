using System.Runtime.CompilerServices;
using PurrNet.Modules;
#if PURR_DELTA_CHECK
using PurrNet.Logging;
#endif

namespace PurrNet.Packing
{
    public static class DeltaPacker<T>
    {
        public static DeltaWriteFunc<T> WriteFunc;
        public static DeltaReadFunc<T> ReadFunc;

        static bool _hasWriter, _hasReader;

        static DeltaPacker()
        {
            WriteFunc = DeltaPacker.FallbackWriter;
            ReadFunc = DeltaPacker.FallbackReader;
        }

        public static void Register(DeltaWriteFunc<T> write, DeltaReadFunc<T> read)
        {
            RegisterWriter(write);
            RegisterReader(read);
        }

        public static bool HasPacker()
        {
            return _hasWriter && _hasReader;
        }

        public static void RegisterWriter(DeltaWriteFunc<T> a)
        {
            if (_hasWriter)
                return;

            _hasWriter = true;
            DeltaPacker.RegisterWriter(typeof(T), a.Method);
            WriteFunc = a;
        }

        public static void RegisterReader(DeltaReadFunc<T> b)
        {
            if (_hasReader)
                return;

            _hasReader = true;
            DeltaPacker.RegisterReader(typeof(T), b.Method);
            ReadFunc = b;
        }

        [UsedByIL]
        public static bool WriteUnpacked(BitPacker packer, T oldValue, T newValue)
        {
            if (Packer.AreEqual(oldValue, newValue))
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            Packer<T>.WriteFunc(packer, newValue);
            return true;
        }

        [UsedByIL]
        public static void ReadUnpacked(BitPacker packer, T oldValue, ref T value)
        {
            if (!packer.ReadBit())
            {
                value = oldValue;
                return;
            }

            Packer<T>.ReadFunc(packer, ref value);
        }

#if !PURR_DELTA_CHECK
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static bool Write(BitPacker packer, T oldValue, T newValue)
        {
#if PURR_DELTA_CHECK
            Packer<T>.Write(packer, oldValue);
            Packer<T>.Write(packer, newValue);
            int sizePos = packer.AdvanceBits(32);

            int bits = packer.positionInBits;
            var changed = WriteFunc(packer, oldValue, newValue);
            int endPos = packer.positionInBits;

            packer.SetBitPosition(sizePos);
            Packer<int>.Write(packer, endPos - bits);
            packer.SetBitPosition(endPos);
            return changed;
#else
            return WriteFunc(packer, oldValue, newValue);
#endif
        }

#if !PURR_DELTA_CHECK
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static void Read(BitPacker packer, T oldValue, ref T value)
        {
#if PURR_DELTA_CHECK
            var shouldBeOld = Packer<T>.Read(packer);
            var shouldBeNew = Packer<T>.Read(packer);
            var shouldReadCount = Packer<int>.Read(packer);

            int startPos = packer.positionInBits;

            ReadFunc(packer, oldValue, ref value);

            if (!Packer.AreEqual(shouldBeOld, oldValue))
                PurrLogger.LogError($"<{typeof(T)}> old value `{oldValue}` is not equal to the one that was used to write the delta `{shouldBeOld}`.");

            if (!Packer.AreEqual(shouldBeNew, value))
                PurrLogger.LogError($"<{typeof(T)}> New value `{value}` is not equal to the one that was used to write the delta `{shouldBeNew}`.");

            int readCount = packer.positionInBits - startPos;
            if (shouldReadCount != readCount)
            {
                PurrLogger.LogError($"<{typeof(T)}> Delta read count `{readCount}` is not equal to the actual read count `{shouldReadCount}`.");
                packer.SetBitPosition(startPos + shouldReadCount);
            }
#else
            ReadFunc(packer, oldValue, ref value);
#endif
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize(BitPacker packer, T oldValue, ref T value)
        {
            if (packer.isWriting)
                WriteFunc(packer, oldValue, value);
            else ReadFunc(packer, oldValue, ref value);
        }
    }
}
