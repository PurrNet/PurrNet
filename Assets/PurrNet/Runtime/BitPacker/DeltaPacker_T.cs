using System;
using PurrNet.Logging;
using PurrNet.Modules;
using Unity.Profiling;

namespace PurrNet.Packing
{
    public static class DeltaPacker<T>
    {
        static DeltaWriteFunc<T> _write;
        static DeltaReadFunc<T> _read;

        public static int GetNecessaryBitsToWrite(in T oldValue, in T newValue)
        {
            if (_write == null)
            {
                PurrLogger.LogError($"No delta writer for type '{typeof(T)}' is registered.");
                return 0;
            }

            using var packer = BitPackerPool.Get();
            if (_write(packer, oldValue, newValue))
                return packer.positionInBits;
            return 0;
        }

        public static void Register(DeltaWriteFunc<T> write, DeltaReadFunc<T> read)
        {
            RegisterWriter(write);
            RegisterReader(read);
        }

        public static bool HasPacker()
        {
            return _write != null;
        }

        public static void RegisterWriter(DeltaWriteFunc<T> a)
        {
            if (_write != null)
                return;

            DeltaPacker.RegisterWriter(typeof(T), a.Method);
            _write = a;
        }

        public static void RegisterReader(DeltaReadFunc<T> b)
        {
            if (_read != null)
                return;

            DeltaPacker.RegisterReader(typeof(T), b.Method);
            _read = b;
        }

        static readonly ProfilerMarker _WriteUnpackedMarker = new ProfilerMarker($"DeltaPacker<{typeof(T).Name}>.WriteUnpacked");
        static readonly ProfilerMarker _ReadUnpackedMarker = new ProfilerMarker($"DeltaPacker<{typeof(T).Name}>.ReadUnpacked");
        static readonly ProfilerMarker _writeMarker = new ProfilerMarker($"DeltaPacker<{typeof(T).Name}>.Write");
        static readonly ProfilerMarker _readMarker = new ProfilerMarker($"DeltaPacker<{typeof(T).Name}>.Read");

        [UsedByIL]
        public static bool WriteUnpacked(BitPacker packer, T oldValue, T newValue)
        {
            using (_WriteUnpackedMarker.Auto())
            {
                if (Packer.AreEqual(oldValue, newValue))
                {
                    Packer<bool>.Write(packer, false);
                    return false;
                }

                Packer<bool>.Write(packer, true);
                Packer<T>.Write(packer, newValue);
                return true;
            }
        }

        [UsedByIL]
        public static void ReadUnpacked(BitPacker packer, T oldValue, ref T value)
        {
            using (_ReadUnpackedMarker.Auto())
            {
                if (!Packer<bool>.Read(packer))
                {
                    value = oldValue;
                    return;
                }

                Packer<T>.Read(packer, ref value);
            }
        }

        public static bool Write(BitPacker packer, T oldValue, T newValue)
        {
            using (_writeMarker.Auto())
            {
                try
                {
#if PURR_DELTA_CHECK
                    Packer<T>.Write(packer, oldValue);
                    Packer<T>.Write(packer, newValue);
                    int sizePos = packer.AdvanceBits(32);

                    int bits = packer.positionInBits;
                    var changed = _write?.Invoke(packer, oldValue, newValue) ??
                                  DeltaPacker.FallbackWriter(packer, oldValue, newValue);

                    int endPos = packer.positionInBits;

                    packer.SetBitPosition(sizePos);
                    Packer<int>.Write(packer, endPos - bits);
                    packer.SetBitPosition(endPos);
                    return changed;
#else
                    if (_write == null)
                        return DeltaPacker.FallbackWriter(packer, oldValue, newValue);
                    return _write(packer, oldValue, newValue);
#endif
                }
                catch (Exception e)
                {
                    PurrLogger.LogError(
                        $"Failed to delta write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
                    return false;
                }
            }
        }

        public static void Read(BitPacker packer, T oldValue, ref T value)
        {
            using (_readMarker.Auto())
            {
                try
                {
#if PURR_DELTA_CHECK
                var shouldBeOld = Packer<T>.Read(packer);
                var shouldBeNew = Packer<T>.Read(packer);
                var shouldReadCount = Packer<int>.Read(packer);

                int startPos = packer.positionInBits;

                if (_read == null)
                {
                    DeltaPacker.FallbackReader(packer, oldValue, ref value);
                    return;
                }

                _read(packer, oldValue, ref value);

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
                    if (_read == null)
                    {
                        DeltaPacker.FallbackReader(packer, oldValue, ref value);
                        return;
                    }

                    _read(packer, oldValue, ref value);
#endif
                }
                catch (Exception e)
                {
                    PurrLogger.LogError(
                        $"Failed to delta read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
                }
            }
        }

        public static T Read(BitPacker packer, T oldValue)
        {
            T newValue = default;
            Read(packer, oldValue, ref newValue);
            return newValue;
        }

        public static void Serialize(BitPacker packer, T oldValue, ref T value)
        {
            if (packer.isWriting)
                Write(packer, oldValue, value);
            else Read(packer, oldValue, ref value);
        }
    }
}
