using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;

namespace PurrNet.Packing
{
    public static class DeltaPacker<T>
    {
        /// <summary>
        /// The delta writer for exactly <typeparamref name="T"/>, without runtime type dispatch.
        /// </summary>
        public static DeltaWriteFunc<T> DirectWrite;

        /// <summary>
        /// The delta reader for exactly <typeparamref name="T"/>, without runtime type dispatch.
        /// </summary>
        public static DeltaReadFunc<T> DirectRead;

        /// <summary>
        /// The inheritance-aware delta writer used by the public API.
        /// </summary>
        public static DeltaWriteFunc<T> WriteFunc;

        /// <summary>
        /// The inheritance-aware delta reader used by the public API.
        /// </summary>
        public static DeltaReadFunc<T> ReadFunc;

        static bool _hasWriter, _hasReader;

        static DeltaPacker()
        {
            DirectWrite = DeltaPacker.FallbackWriter;
            DirectRead = DeltaPacker.FallbackReader;
            WriteFunc = DirectWrite;
            ReadFunc = DirectRead;
        }

        [UsedImplicitly]
        public static void Register(DeltaWriteFunc<T> write, DeltaReadFunc<T> read)
        {
            RegisterWriter(write);
            RegisterReader(read);
            NativeDeltaPacker<T>.Register(write, read);
        }

        public static bool HasPacker()
        {
            return _hasWriter && _hasReader;
        }

        public static void RegisterWriter(DeltaWriteFunc<T> write)
        {
            if (_hasWriter)
                return;

            _hasWriter = true;
            DirectWrite = write;

            bool isStructOrSealed = typeof(T).IsValueType || typeof(T).IsSealed;
            WriteFunc = isStructOrSealed ? DirectWrite : WriteClass;

            DeltaPacker.RegisterWriter(DirectWrite, WriteFunc);
        }

        public static void RegisterReader(DeltaReadFunc<T> read)
        {
            if (_hasReader)
                return;

            _hasReader = true;
            DirectRead = read;

            bool isStructOrSealed = typeof(T).IsValueType || typeof(T).IsSealed;
            ReadFunc = isStructOrSealed ? DirectRead : ReadClass;

            DeltaPacker.RegisterReader(DirectRead, ReadFunc);
        }

        static bool WriteClass(BitPacker packer, T oldValue, T newValue)
        {
            var oldType = GetSerializedType(oldValue);
            var newType = GetSerializedType(newValue);
            bool useDirectPacker = oldType == typeof(T) && newType == typeof(T);

            int changedPosition = packer.AdvanceOneBitAndSet();
            packer.WriteBit(useDirectPacker);

            bool changed = useDirectPacker
                ? DirectWrite(packer, oldValue, newValue)
                : PackDeltaObj.WriteDeltaObject(
                    packer,
                    oldValue,
                    newValue,
                    oldValue == null ? null : oldType,
                    newValue == null ? null : newType);

            if (!changed)
                packer.ResetFlagAtAndMovePosition(changedPosition);

            return changed;
        }

        static void ReadClass(BitPacker packer, T oldValue, ref T value)
        {
            if (!packer.ReadBit())
            {
                DeltaPacker.DisposeReplaced(oldValue, ref value);
                value = Packer.Copy(oldValue);
                return;
            }

            if (packer.ReadBit())
            {
                DirectRead(packer, oldValue, ref value);
                return;
            }

            object boxedValue = value;
            var oldType = oldValue == null ? null : GetSerializedType(oldValue);
            PackDeltaObj.ReadDeltaObject(packer, oldValue, oldType, ref boxedValue);

            switch (boxedValue)
            {
                case null:
                    value = default;
                    break;
                case T cast:
                    value = cast;
                    break;
                default:
                    PurrLogger.LogError($"While delta reading `{typeof(T)}`, we got `{boxedValue.GetType()}`, which is not assignable to the declared type.");
                    value = default;
                    break;
            }
        }

        static Type GetSerializedType(T value)
        {
            if (value == null)
                return typeof(T);

            var runtimeType = value.GetType();
            if (runtimeType == typeof(T) || Hasher.IsRegistered(runtimeType))
                return runtimeType;

            WarnUnregisteredRuntimeType(runtimeType);
            return typeof(T);
        }

        static readonly HashSet<Type> _warnedUnregisteredTypes = new HashSet<Type>();

        static void WarnUnregisteredRuntimeType(Type runtimeType)
        {
            if (_warnedUnregisteredTypes.Add(runtimeType))
                PurrLogger.LogWarning(
                    $"Delta writing `{typeof(T)}`: runtime type `{runtimeType}` isn't a registered network type, so only the `{typeof(T)}` fields will sync. " +
                    "Reference the derived type in a serialized context or mark it with [RegisterNetworkType] if its fields should replicate.");
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadUnpacked(BitPacker packer, T oldValue, ref T value)
        {
            if (!packer.ReadBit())
            {
                DeltaPacker.DisposeReplaced(oldValue, ref value);
                value = Packer.Copy(oldValue);
                return;
            }

            if (!typeof(T).IsValueType && ReferenceEquals(value, oldValue))
                value = default;
            Packer<T>.ReadFunc(packer, ref value);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteUnpackedAsExactType(BitPacker packer, T oldValue, T newValue)
        {
            if (Packer.AreEqual(oldValue, newValue))
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            Packer<T>.DirectWrite(packer, newValue);
            return true;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadUnpackedAsExactType(BitPacker packer, T oldValue, ref T value)
        {
            if (!packer.ReadBit())
            {
                DeltaPacker.DisposeReplaced(oldValue, ref value);
                value = Packer.Copy(oldValue);
                return;
            }

            if (!typeof(T).IsValueType && ReferenceEquals(value, oldValue))
                value = default;
            Packer<T>.DirectRead(packer, ref value);
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
