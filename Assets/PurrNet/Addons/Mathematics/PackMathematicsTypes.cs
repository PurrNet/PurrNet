using PurrNet.Modules;
using Unity.Mathematics;

namespace PurrNet.Packing
{
    public static class PackMathematicsTypes
    {
        [UsedByIL]
        public static void Write(this BitPacker packer, float2 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref float2 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
        }

        [UsedByIL]
        private static bool WriteFloat2(BitPacker packer, float2 oldvalue, float2 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadFloat2(BitPacker packer, float2 oldvalue, ref float2 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, float3 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref float3 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
        }

        [UsedByIL]
        private static bool WriteFloat3(BitPacker packer, float3 oldvalue, float3 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadFloat3(BitPacker packer, float3 oldvalue, ref float3 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<float>.Read(packer, oldvalue.z, ref value.z);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, float4 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
            packer.Write(value.w);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref float4 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
            packer.Read(ref value.w);
        }

        [UsedByIL]
        private static bool WriteFloat4(BitPacker packer, float4 oldvalue, float4 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            delta.Write(oldvalue.w, newvalue.w);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadFloat4(BitPacker packer, float4 oldvalue, ref float4 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<float>.Read(packer, oldvalue.z, ref value.z);
                DeltaPacker<float>.Read(packer, oldvalue.w, ref value.w);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, quaternion value)
        {
            packer.Write(value.value);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref quaternion value)
        {
            packer.Read(ref value.value);
        }

        [UsedByIL]
        private static bool WriteQuaternion(BitPacker packer, quaternion oldvalue, quaternion newvalue)
        {
            return WriteFloat4(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        private static void ReadQuaternion(BitPacker packer, quaternion oldvalue, ref quaternion value)
        {
            ReadFloat4(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, double2 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref double2 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
        }

        [UsedByIL]
        private static bool WriteDouble2(BitPacker packer, double2 oldvalue, double2 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadDouble2(BitPacker packer, double2 oldvalue, ref double2 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<double>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<double>.Read(packer, oldvalue.y, ref value.y);
            }
            else value = oldvalue;
        }

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

        [UsedByIL]
        public static void Write(this BitPacker packer, double4 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
            packer.Write(value.w);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref double4 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
            packer.Read(ref value.w);
        }

        [UsedByIL]
        private static bool WriteDouble4(BitPacker packer, double4 oldvalue, double4 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            delta.Write(oldvalue.w, newvalue.w);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadDouble4(BitPacker packer, double4 oldvalue, ref double4 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<double>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<double>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<double>.Read(packer, oldvalue.z, ref value.z);
                DeltaPacker<double>.Read(packer, oldvalue.w, ref value.w);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, int2 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref int2 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
        }

        [UsedByIL]
        private static bool WriteInt2(BitPacker packer, int2 oldvalue, int2 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadInt2(BitPacker packer, int2 oldvalue, ref int2 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<int>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<int>.Read(packer, oldvalue.y, ref value.y);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, int3 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref int3 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
        }

        [UsedByIL]
        private static bool WriteInt3(BitPacker packer, int3 oldvalue, int3 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadInt3(BitPacker packer, int3 oldvalue, ref int3 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<int>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<int>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<int>.Read(packer, oldvalue.z, ref value.z);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, int4 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
            packer.Write(value.w);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref int4 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
            packer.Read(ref value.w);
        }

        [UsedByIL]
        private static bool WriteInt4(BitPacker packer, int4 oldvalue, int4 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            delta.Write(oldvalue.w, newvalue.w);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadInt4(BitPacker packer, int4 oldvalue, ref int4 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<int>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<int>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<int>.Read(packer, oldvalue.z, ref value.z);
                DeltaPacker<int>.Read(packer, oldvalue.w, ref value.w);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, uint2 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref uint2 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
        }

        [UsedByIL]
        private static bool WriteUInt2(BitPacker packer, uint2 oldvalue, uint2 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadUInt2(BitPacker packer, uint2 oldvalue, ref uint2 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<uint>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<uint>.Read(packer, oldvalue.y, ref value.y);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, uint3 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref uint3 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
        }

        [UsedByIL]
        private static bool WriteUInt3(BitPacker packer, uint3 oldvalue, uint3 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadUInt3(BitPacker packer, uint3 oldvalue, ref uint3 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<uint>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<uint>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<uint>.Read(packer, oldvalue.z, ref value.z);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, uint4 value)
        {
            packer.Write(value.x);
            packer.Write(value.y);
            packer.Write(value.z);
            packer.Write(value.w);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref uint4 value)
        {
            packer.Read(ref value.x);
            packer.Read(ref value.y);
            packer.Read(ref value.z);
            packer.Read(ref value.w);
        }

        [UsedByIL]
        private static bool WriteUInt4(BitPacker packer, uint4 oldvalue, uint4 newvalue)
        {
            var delta = new DeltaWritingScope(packer);
            delta.Write(oldvalue.x, newvalue.x);
            delta.Write(oldvalue.y, newvalue.y);
            delta.Write(oldvalue.z, newvalue.z);
            delta.Write(oldvalue.w, newvalue.w);
            return delta.Complete();
        }

        [UsedByIL]
        private static void ReadUInt4(BitPacker packer, uint4 oldvalue, ref uint4 value)
        {
            if (packer.ReadBit())
            {
                DeltaPacker<uint>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<uint>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<uint>.Read(packer, oldvalue.z, ref value.z);
                DeltaPacker<uint>.Read(packer, oldvalue.w, ref value.w);
            }
            else value = oldvalue;
        }
    }
}
