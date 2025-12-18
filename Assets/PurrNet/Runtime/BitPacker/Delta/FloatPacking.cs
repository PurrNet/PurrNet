using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class FloatPacking
    {
        [UsedByIL]
        public static unsafe void Write(this BitPacker packer, float data)
        {
            ulong bits = *(uint*)&data;
            packer.WriteBits(bits, 32);
        }

        [UsedByIL]
        public static unsafe void Read(this BitPacker packer, ref float data)
        {
            ulong bits = packer.ReadBits(32);
            data = *(float*)&bits;
        }

        [UsedByIL]
        private static unsafe bool WriteSingle(BitPacker packer, float oldvalue, float newvalue)
        {
            uint newbits = *(uint*)&newvalue;
            uint oldbits = *(uint*)&oldvalue;

            if (newbits == oldbits)
            {
                Packer<bool>.Write(packer, false);
                return false;
            }

            Packer<bool>.Write(packer, true);
            Packer<float>.Write(packer, newvalue);
            return true;
        }

        [UsedByIL]
        private static void ReadSingle(BitPacker packer, float oldvalue, ref float value)
        {
            bool hasChanged = default;
            Packer<bool>.Read(packer, ref hasChanged);

            if (!hasChanged)
            {
                value = oldvalue;
                return;
            }

            Packer<float>.Read(packer, ref value);
        }
    }
}
