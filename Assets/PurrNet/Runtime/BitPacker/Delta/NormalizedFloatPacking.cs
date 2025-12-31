using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class NormalizedFloatPacking
    {
        [UsedByIL]
        private static void WriteHalf(BitPacker packer, NormalizedFloat value)
        {
            PackingIntegers.WritePrefixed(packer, value.value, NormalizedFloat.BIT_RESOLUTION);
        }

        [UsedByIL]
        private static void ReadHalf(BitPacker packer, ref NormalizedFloat value)
        {
            PackingIntegers.ReadPrefixed(packer, ref value.value, NormalizedFloat.BIT_RESOLUTION);
        }

        [UsedByIL]
        private static bool WriteAngle(BitPacker packer, NormalizedFloat oldvalue, NormalizedFloat newvalue)
        {
            var delta = newvalue.value - oldvalue.value;

            if (delta == 0)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            //Packer<PackedLong>.Write(packer, delta);
            PackingIntegers.WritePrefixed(packer, delta, NormalizedFloat.BIT_RESOLUTION);
            return true;
        }

        [UsedByIL]
        private static void ReadAngle(BitPacker packer, NormalizedFloat oldvalue, ref NormalizedFloat value)
        {
            bool hasChanged = packer.ReadBit();

            if (!hasChanged)
            {
                value = oldvalue;
                return;
            }

            /*PackedLong delta = default;
            Packer<PackedLong>.Read(packer, ref delta);*/
            long delta = default;
            PackingIntegers.ReadPrefixed(packer, ref delta, NormalizedFloat.BIT_RESOLUTION);
            value.value = oldvalue.value + delta;
        }
    }
}
