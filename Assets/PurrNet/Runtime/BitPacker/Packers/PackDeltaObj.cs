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
            int flagPos = packer.AdvanceBits(1);

            bool oldHasValue = oldvalue != null;
            bool newHasValue = newvalue != null;

            bool hasChanged = DeltaPacker<bool>.Write(packer, oldHasValue, newHasValue);

            if (newHasValue)
            {
                var oldType = oldvalue?.GetType();
                var newType = newvalue.GetType();

                var oldHash = Hasher.GetStableHashU32(oldType);
                var newHash = Hasher.GetStableHashU32(newType);

                var oldValueSanitized = oldType != newType ? null : oldvalue;

                hasChanged = DeltaPacker<uint>.Write(packer, oldHash, newHash) || hasChanged;
                hasChanged = DeltaPacker.Write(packer, newType, oldValueSanitized, newvalue) || hasChanged;
            }

            packer.WriteAt(flagPos, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(flagPos + 1);

            return hasChanged;
        }

        [UsedImplicitly]
        public static void ReadDeltaObject(BitPacker packer, object oldvalue, ref object value)
        {
            if (!packer.ReadBit())
            {
                if (value is IDisposable disposable)
                    disposable.Dispose();
                value = oldvalue == null ? null : Packer.Copy(oldvalue);
                return;
            }

            bool hasValue = default;
            DeltaPacker<bool>.Read(packer, oldvalue != null, ref hasValue);

            if (hasValue)
            {
                var oldType = oldvalue?.GetType();
                uint oldHash = Hasher.GetStableHashU32(oldType);
                uint newHash = default;
                DeltaPacker<uint>.Read(packer, oldHash, ref newHash);

                var newType = Hasher.ResolveType(newHash);
                var oldValueSanitized = oldType != newType ? null : oldvalue;

                DeltaPacker.Read(packer, newType, oldValueSanitized, ref value);
            }
            else
            {
                value = null;
            }
        }
    }
}
