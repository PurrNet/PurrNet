using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class DeltaPackInteger
    {
        [UsedByIL]
        public static bool WriteBool(BitPacker packer, bool oldvalue, bool newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);
            return hasChanged;
        }

        [UsedByIL]
        public static void ReadBool(BitPacker packer, bool oldvalue, ref bool value)
        {
            bool hasChanged = packer.ReadBit();
            value = hasChanged ? !oldvalue : oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt8(BitPacker packer, sbyte oldvalue, sbyte newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                short diff = (short)(newvalue - oldvalue);
                Packer<PackedShort>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadInt8(BitPacker packer, sbyte oldvalue, ref sbyte value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedShort packed = default;
                Packer<PackedShort>.Read(packer, ref packed);
                value = (sbyte)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt8(BitPacker packer, byte oldvalue, byte newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                short diff = (short)(newvalue - oldvalue);
                Packer<PackedShort>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadUInt8(BitPacker packer, byte oldvalue, ref byte value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedShort packed = default;
                Packer<PackedShort>.Read(packer, ref packed);
                value = (byte)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt16(BitPacker packer, short oldvalue, short newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                int diff = newvalue - oldvalue;
                Packer<PackedInt>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadInt16(BitPacker packer, short oldvalue, ref short value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedInt packed = default;
                Packer<PackedInt>.Read(packer, ref packed);
                value = (short)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, ushort oldvalue, ushort newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                int diff = (int)((uint)newvalue - oldvalue);
                Packer<PackedInt>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, ushort oldvalue, ref ushort value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedInt packed = default;
                Packer<PackedInt>.Read(packer, ref packed);
                value = (ushort)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt32(BitPacker packer, uint oldvalue, uint newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                long diff = newvalue - (long)oldvalue;
                Packer<PackedLong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadUInt32(BitPacker packer, uint oldvalue, ref uint value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedLong packed = default;
                Packer<PackedLong>.Read(packer, ref packed);
                value = (uint)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt32(BitPacker packer, int oldvalue, int newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                long diff = newvalue - (long)oldvalue;
                Packer<PackedLong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadInt32(BitPacker packer, int oldvalue, ref int value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedLong packed = default;
                Packer<PackedLong>.Read(packer, ref packed);
                value = (int)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt64(BitPacker packer, long oldvalue, long newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                long diff = newvalue - oldvalue;
                Packer<PackedLong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, long oldvalue, ref long value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedLong packed = default;
                Packer<PackedLong>.Read(packer, ref packed);
                value = oldvalue + packed.value;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, ulong oldvalue, ulong newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                PackedLong diff = (long)newvalue - (long)oldvalue;
                Packer<PackedLong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, ulong oldvalue, ref ulong value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedLong packed = default;
                Packer<PackedLong>.Read(packer, ref packed);
                value = (ulong)((long)oldvalue + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt8(BitPacker packer, PackedByte oldvalue, PackedByte newvalue) =>
            CompactIntegerPacking.WriteDelta8(packer, oldvalue.value, newvalue.value);

        [UsedByIL]
        public static void ReadUInt8(BitPacker packer, PackedByte oldvalue, ref PackedByte value) =>
            value.value = (byte)CompactIntegerPacking.ReadDelta8(packer, oldvalue.value);

        [UsedByIL]
        public static bool WriteInt8(BitPacker packer, PackedSByte oldvalue, PackedSByte newvalue) =>
            CompactIntegerPacking.WriteDelta8(packer, unchecked((byte)oldvalue.value),
                unchecked((byte)newvalue.value));

        [UsedByIL]
        public static void ReadInt8(BitPacker packer, PackedSByte oldvalue, ref PackedSByte value) =>
            value.value = unchecked((sbyte)CompactIntegerPacking.ReadDelta8(
                packer, unchecked((byte)oldvalue.value)));

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, PackedUShort oldvalue, PackedUShort newvalue) =>
            CompactIntegerPacking.WriteDelta16(packer, oldvalue.value, newvalue.value);

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, PackedUShort oldvalue, ref PackedUShort value) =>
            value.value = (ushort)CompactIntegerPacking.ReadDelta16(packer, oldvalue.value);

        [UsedByIL]
        public static bool WriteInt16(BitPacker packer, PackedShort oldvalue, PackedShort newvalue) =>
            CompactIntegerPacking.WriteDelta16(packer, unchecked((ushort)oldvalue.value),
                unchecked((ushort)newvalue.value));

        [UsedByIL]
        public static void ReadInt16(BitPacker packer, PackedShort oldvalue, ref PackedShort value) =>
            value.value = unchecked((short)CompactIntegerPacking.ReadDelta16(
                packer, unchecked((ushort)oldvalue.value)));

        [UsedByIL]
        public static bool WriteUInt32(BitPacker packer, PackedUInt oldvalue, PackedUInt newvalue) =>
            CompactIntegerPacking.WriteDelta32(packer, oldvalue.value, newvalue.value);

        [UsedByIL]
        public static void ReadUInt32(BitPacker packer, PackedUInt oldvalue, ref PackedUInt value) =>
            value.value = (uint)CompactIntegerPacking.ReadDelta32(packer, oldvalue.value);

        [UsedByIL]
        public static bool WriteInt32(BitPacker packer, PackedInt oldvalue, PackedInt newvalue) =>
            CompactIntegerPacking.WriteDelta32(packer, unchecked((uint)oldvalue.value),
                unchecked((uint)newvalue.value));

        [UsedByIL]
        public static void ReadInt32(BitPacker packer, PackedInt oldvalue, ref PackedInt value) =>
            value.value = unchecked((int)CompactIntegerPacking.ReadDelta32(
                packer, unchecked((uint)oldvalue.value)));

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, PackedULong oldvalue, PackedULong newvalue) =>
            CompactIntegerPacking.WriteDelta64(packer, oldvalue.value, newvalue.value);

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, PackedULong oldvalue, ref PackedULong value) =>
            value.value = CompactIntegerPacking.ReadDelta64(packer, oldvalue.value);

        [UsedByIL]
        public static bool WriteInt64(BitPacker packer, PackedLong oldvalue, PackedLong newvalue) =>
            CompactIntegerPacking.WriteDelta64(packer, unchecked((ulong)oldvalue.value),
                unchecked((ulong)newvalue.value));

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, PackedLong oldvalue, ref PackedLong value) =>
            value.value = unchecked((long)CompactIntegerPacking.ReadDelta64(
                packer, unchecked((ulong)oldvalue.value)));

        [UsedByIL]
        public static bool WriteIndex(BitPacker packer, Size oldvalue, Size newvalue) =>
            CompactIntegerPacking.WriteDelta32(packer, oldvalue.value, newvalue.value);

        [UsedByIL]
        public static void ReadIndex(BitPacker packer, Size oldvalue, ref Size value) =>
            value.value = (uint)CompactIntegerPacking.ReadDelta32(packer, oldvalue.value);
    }
}
