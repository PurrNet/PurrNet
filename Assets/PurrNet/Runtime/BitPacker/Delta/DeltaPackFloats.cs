using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class DeltaPackFloats
    {
        [UsedByIL]
        private static void WriteHalf(BitPacker packer, Half value)
        {
            Packer<ushort>.Write(packer, value.rawValue);
        }

        [UsedByIL]
        private static void ReadHalf(BitPacker packer, ref Half value)
        {
            ushort rawValue = default;
            Packer<ushort>.Read(packer, ref rawValue);
            value = Half.FromRawValue(rawValue);
        }

        [UsedByIL]
        private static void WriteCompressedFloat(BitPacker packer, CompressedFloat value)
        {
            Packer<PackedInt>.Write(packer, value.rounded);
        }

        [UsedByIL]
        private static void ReadCompressedFloat(BitPacker packer, ref CompressedFloat value)
        {
            PackedInt val = default;
            Packer<PackedInt>.Read(packer, ref val);
            value = new CompressedFloat(val.value);
        }

        [UsedByIL]
        private static bool WriteHalf(BitPacker packer, Half oldvalue, Half newvalue)
        {
            return DeltaPacker<ushort>.Write(packer, oldvalue.rawValue, newvalue.rawValue);
        }

        [UsedByIL]
        private static void ReadHalf(BitPacker packer, Half oldvalue, ref Half value)
        {
            ushort newValue = default;
            DeltaPacker<ushort>.Read(packer, oldvalue.rawValue, ref newValue);
            value = Half.FromRawValue(newValue);
        }

        [UsedByIL]
        private static unsafe bool WriteDouble(BitPacker packer, double oldvalue, double newvalue)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            bool hasChanged = oldvalue != newvalue;

            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                ulong oldBits = *(ulong*)&oldvalue;
                ulong newBits = *(ulong*)&newvalue;
                long diff = (long)(newBits - oldBits);
                Packer<PackedLong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        private static unsafe void ReadDouble(BitPacker packer, double oldvalue, ref double value)
        {
            if (packer.ReadBit())
            {
                PackedLong packed = default;
                Packer<PackedLong>.Read(packer, ref packed);
                ulong oldBits = *(ulong*)&oldvalue;
                ulong newBits = (ulong)((long)oldBits + packed.value);
                value = *(double*)&newBits;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        private static bool WriteSingle(BitPacker packer, CompressedFloat oldvalue, CompressedFloat newvalue)
        {
            int delta = newvalue.rounded - oldvalue.rounded;

            if (delta == 0)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            PackingIntegers.Write(packer, (PackedInt)delta);
            return true;
        }

        [UsedByIL]
        private static void ReadSingle(BitPacker packer, CompressedFloat oldvalue, ref CompressedFloat value)
        {
            if (packer.ReadBit())
            {
                PackedInt packed = default;
                PackingIntegers.Read(packer, ref packed);
                value = new CompressedFloat(oldvalue.rounded + packed.value);
            }
            else value = oldvalue;
        }
    }
}
