using System;
using JetBrains.Annotations;
using PurrNet.Utils;

namespace PurrNet.Packing
{
    public static class PackDeltaObj
    {
        [UsedImplicitly]
        public static bool WriteDeltaObject(BitPacker packer, object oldvalue, object newvalue)
        {
            return WriteDeltaObject(packer, oldvalue, newvalue, oldvalue?.GetType(), newvalue?.GetType());
        }

        internal static bool WriteDeltaObject(BitPacker packer, object oldvalue, object newvalue,
            Type oldType, Type newType)
        {
            int flagPos = packer.AdvanceBits(1);

            bool oldHasValue = oldvalue != null;
            bool newHasValue = newvalue != null;

            bool hasChanged = DeltaPacker<bool>.Write(packer, oldHasValue, newHasValue);

            if (newHasValue)
            {
                var oldHash = Hasher.GetStableHashU32(oldType);
                var newHash = Hasher.GetStableHashU32(newType);

                var oldValueSanitized = oldType != newType ? null : oldvalue;

                hasChanged = DeltaPacker<uint>.Write(packer, oldHash, newHash) || hasChanged;

                bool valueChanged = newType == typeof(object)
                    ? DeltaPacker<object>.WriteUnpacked(packer, oldValueSanitized, newvalue)
                    : DeltaPacker.WriteAsExactType(packer, newType, oldValueSanitized, newvalue);
                hasChanged = valueChanged || hasChanged;
            }

            packer.WriteAt(flagPos, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(flagPos + 1);

            return hasChanged;
        }

        [UsedImplicitly]
        public static void ReadDeltaObject(BitPacker packer, object oldvalue, ref object value)
        {
            ReadDeltaObject(packer, oldvalue, oldvalue?.GetType(), ref value);
        }

        internal static void ReadDeltaObject(BitPacker packer, object oldvalue, Type oldType, ref object value)
        {
            if (!packer.ReadBit())
            {
                ClearReplacedValue(oldvalue, ref value);
                value = oldvalue == null ? null : Packer.Copy(oldvalue);
                return;
            }

            bool hasValue = default;
            DeltaPacker<bool>.Read(packer, oldvalue != null, ref hasValue);

            if (hasValue)
            {
                uint oldHash = Hasher.GetStableHashU32(oldType);
                uint newHash = default;
                DeltaPacker<uint>.Read(packer, oldHash, ref newHash);

                var newType = Hasher.ResolveType(newHash);
                var oldValueSanitized = oldType != newType ? null : oldvalue;

                if (oldType != newType || (value != null && value.GetType() != newType))
                    ClearReplacedValue(oldvalue, ref value);

                if (newType == typeof(object))
                    DeltaPacker<object>.ReadUnpacked(packer, oldValueSanitized, ref value);
                else
                    DeltaPacker.ReadAsExactType(packer, newType, oldValueSanitized, ref value);
            }
            else
            {
                ClearReplacedValue(oldvalue, ref value);
            }
        }

        static void ClearReplacedValue(object oldvalue, ref object value)
        {
            DeltaPacker.DisposeReplaced(oldvalue, ref value);
            value = null;
        }
    }
}
