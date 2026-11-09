using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class PackedQuaternionPacking
    {
        [UsedByIL]
        private static bool WriteDelta(BitPacker packer, PackedQuaternion oldvalue, PackedQuaternion newvalue)
        {
            if (oldvalue.Equals(newvalue))
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            int k = newvalue.DerivedIndex();
            packer.WriteBit(k >= 0);
            if (k >= 0)
            {
                packer.WriteBits((ulong)k, 2);
                packer.WriteBit(newvalue.Get(k) < 0);
            }

            for (int i = 0; i < 4; i++)
            {
                if (i != k)
                    DeltaPacker<NormalizedFloat>.Write(packer, new NormalizedFloat(oldvalue.Get(i)), new NormalizedFloat(newvalue.Get(i)));
            }

            return true;
        }

        [UsedByIL]
        private static void ReadDelta(BitPacker packer, PackedQuaternion oldvalue, ref PackedQuaternion value)
        {
            value = oldvalue;
            if (!packer.ReadBit())
                return;

            int k = -1;
            bool negative = false;
            if (packer.ReadBit())
            {
                k = (int)packer.ReadBits(2);
                negative = packer.ReadBit();
            }

            for (int i = 0; i < 4; i++)
            {
                if (i == k)
                    continue;
                var component = new NormalizedFloat(oldvalue.Get(i));
                DeltaPacker<NormalizedFloat>.Read(packer, new NormalizedFloat(oldvalue.Get(i)), ref component);
                value.Set(i, component.value);
            }

            if (k >= 0)
            {
                long d = value.DeriveAt(k);
                value.Set(k, negative ? -d : d);
            }
        }
    }
}
