using PurrNet.Modules;
using Unity.Mathematics;

namespace PurrNet.Packing
{
    public static class PackMathematicsTypes
    {
        [UsedByIL]
        public static void Write(this BitPacker packer, double3 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref double3 value)
        {
            double x = default;
            double y = default;
            double z = default;
            packer.Read(ref x);
            packer.Read(ref y);
            packer.Read(ref z);
            value = new double3(x, y, z);
        }

        [UsedByIL]
        private static bool WriteDouble3(BitPacker packer, double3 oldvalue, double3 newvalue)
        {
            int flagPos = packer.AdvanceBits(1);
            bool hasChanged;

            hasChanged = DeltaPacker<double>.Write(packer, oldvalue.x, newvalue.x);
            hasChanged = DeltaPacker<double>.Write(packer, oldvalue.y, newvalue.y) || hasChanged;
            hasChanged = DeltaPacker<double>.Write(packer, oldvalue.z, newvalue.z) || hasChanged;

            packer.WriteAt(flagPos, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(flagPos + 1);
            return hasChanged;
        }

        [UsedByIL]
        private static void ReadDouble3(BitPacker packer, double3 oldvalue, ref double3 value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                DeltaPacker<double>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<double>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<double>.Read(packer, oldvalue.z, ref value.z);
            }
            else value = oldvalue;
        }
    }
}
