using PurrNet.Modules;
using UnityEngine;

namespace PurrNet.Packing
{
    public static class DeltaPackVectors
    {
        [UsedByIL]
        private static bool WriteVector2(BitPacker packer, Vector2 oldvalue, Vector2 newvalue)
        {
            int flagPos = packer.AdvanceBits(1);
            bool wasChanged;

            wasChanged = DeltaPacker<float>.Write(packer, oldvalue.x, newvalue.x);
            wasChanged = DeltaPacker<float>.Write(packer, oldvalue.y, newvalue.y) || wasChanged;

            packer.WriteAt(flagPos, wasChanged);

            if (!wasChanged)
                packer.SetBitPosition(flagPos + 1);

            return wasChanged;
        }

        [UsedByIL]
        private static void ReadVector2(BitPacker packer, Vector2 oldvalue, ref Vector2 value)
        {
            bool hasChanged = default;
            Packer<bool>.Read(packer, ref hasChanged);

            if (hasChanged)
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        private static bool WriteVector3(BitPacker packer, Vector3 oldvalue, Vector3 newvalue)
        {
            int flagPos = packer.AdvanceBits(1);
            bool hasChanged;

            hasChanged = DeltaPacker<float>.Write(packer, oldvalue.x, newvalue.x);
            hasChanged = DeltaPacker<float>.Write(packer, oldvalue.y, newvalue.y) || hasChanged;
            hasChanged = DeltaPacker<float>.Write(packer, oldvalue.z, newvalue.z) || hasChanged;

            packer.WriteAt(flagPos, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(flagPos + 1);
            return hasChanged;
        }

        [UsedByIL]
        private static void ReadVector3(BitPacker packer, Vector3 oldvalue, ref Vector3 value)
        {
            bool hasChanged = default;
            Packer<bool>.Read(packer, ref hasChanged);

            if (hasChanged)
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<float>.Read(packer, oldvalue.z, ref value.z);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        private static bool WriteVector3(BitPacker packer, Vector3Int oldvalue, Vector3Int newvalue)
        {
            int flagPos = packer.AdvanceBits(1);
            bool hasChanged;

            hasChanged = DeltaPacker<int>.Write(packer, oldvalue.x, newvalue.x);
            hasChanged = DeltaPacker<int>.Write(packer, oldvalue.y, newvalue.y) || hasChanged;
            hasChanged = DeltaPacker<int>.Write(packer, oldvalue.z, newvalue.z) || hasChanged;

            packer.WriteAt(flagPos, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(flagPos + 1);
            return hasChanged;
        }

        [UsedByIL]
        private static void ReadVector3(BitPacker packer, Vector3Int oldvalue, ref Vector3Int value)
        {
            bool hasChanged = default;
            Packer<bool>.Read(packer, ref hasChanged);

            if (hasChanged)
            {
                value.x = DeltaPacker<int>.Read(packer, oldvalue.x);
                value.y = DeltaPacker<int>.Read(packer, oldvalue.y);
                value.z = DeltaPacker<int>.Read(packer, oldvalue.z);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        private static bool WriteVector4(BitPacker packer, Vector4 oldvalue, Vector4 newvalue)
        {
            int flagPos = packer.AdvanceBits(1);
            bool isEqual;

            isEqual = DeltaPacker<float>.Write(packer, oldvalue.x, newvalue.x);
            isEqual = DeltaPacker<float>.Write(packer, oldvalue.y, newvalue.y) || isEqual;
            isEqual = DeltaPacker<float>.Write(packer, oldvalue.z, newvalue.z) || isEqual;
            isEqual = DeltaPacker<float>.Write(packer, oldvalue.w, newvalue.w) || isEqual;

            packer.WriteAt(flagPos, isEqual);
            if (!isEqual)
                packer.SetBitPosition(flagPos + 1);
            return isEqual;
        }

        [UsedByIL]
        private static void ReadVector4(BitPacker packer, Vector4 oldvalue, ref Vector4 value)
        {
            bool hasChanged = default;
            Packer<bool>.Read(packer, ref hasChanged);

            if (hasChanged)
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<float>.Read(packer, oldvalue.z, ref value.z);
                DeltaPacker<float>.Read(packer, oldvalue.w, ref value.w);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        private static bool WriteQuaternion(BitPacker packer, Quaternion oldvalue, Quaternion newvalue)
        {
            int flagPos = packer.AdvanceBits(1);
            bool isEqual;

            isEqual = DeltaPacker<float>.Write(packer, oldvalue.x, newvalue.x);
            isEqual = DeltaPacker<float>.Write(packer, oldvalue.y, newvalue.y) || isEqual;
            isEqual = DeltaPacker<float>.Write(packer, oldvalue.z, newvalue.z) || isEqual;
            isEqual = DeltaPacker<float>.Write(packer, oldvalue.w, newvalue.w) || isEqual;

            packer.WriteAt(flagPos, isEqual);
            if (!isEqual)
                packer.SetBitPosition(flagPos + 1);
            return isEqual;
        }

        [UsedByIL]
        private static void ReadQuaternion(BitPacker packer, Quaternion oldvalue, ref Quaternion value)
        {
            bool hasChanged = default;
            Packer<bool>.Read(packer, ref hasChanged);

            if (hasChanged)
            {
                DeltaPacker<float>.Read(packer, oldvalue.x, ref value.x);
                DeltaPacker<float>.Read(packer, oldvalue.y, ref value.y);
                DeltaPacker<float>.Read(packer, oldvalue.z, ref value.z);
                DeltaPacker<float>.Read(packer, oldvalue.w, ref value.w);
            }
            else value = oldvalue;
        }
    }
}
