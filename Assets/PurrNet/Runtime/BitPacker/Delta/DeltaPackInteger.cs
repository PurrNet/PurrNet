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
        public static bool WriteUInt32(BitPacker packer, PackedUInt oldvalue, PackedUInt newvalue)
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
        public static void ReadUInt32(BitPacker packer, PackedUInt oldvalue, ref PackedUInt value)
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
        public static bool WriteInt64(BitPacker packer, PackedLong oldvalue, PackedLong newvalue)
        {
            return DeltaPacker<long>.Write(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, PackedLong oldvalue, ref PackedLong value)
        {
            DeltaPacker<long>.Read(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteIndex(BitPacker packer, Size oldvalue, Size newvalue)
        {
            bool hasChanged = oldvalue.value != newvalue.value;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                long diff = newvalue.value - oldvalue.value;
                Packer<PackedLong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadIndex(BitPacker packer, Size oldvalue, ref Size value)
        {
            if (packer.ReadBit())
            {
                PackedLong packed = default;
                Packer<PackedLong>.Read(packer, ref packed);
                value.value = (uint)(oldvalue.value + packed.value);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, PackedULong oldvalue, PackedULong newvalue)
        {
            bool hasChanged = oldvalue.value != newvalue.value;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                ulong diff = newvalue.value - oldvalue.value;
                Packer<PackedULong>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, PackedULong oldvalue, ref PackedULong value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedULong packed = default;
                Packer<PackedULong>.Read(packer, ref packed);
                value.value = oldvalue.value + packed.value;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, PackedUShort oldvalue, PackedUShort newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);

            if (hasChanged)
            {
                PackedInt diff = newvalue - oldvalue;
                Packer<PackedInt>.Write(packer, diff);
            }

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, PackedUShort oldvalue, ref PackedUShort value)
        {
            bool hasChanged = packer.ReadBit();

            if (hasChanged)
            {
                PackedInt packed = default;
                Packer<PackedInt>.Read(packer, ref packed);
                value = (PackedUShort)(oldvalue + packed.value);
            }
            else value = oldvalue;
        }
    }
}
