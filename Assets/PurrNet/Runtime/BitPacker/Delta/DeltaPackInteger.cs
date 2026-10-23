using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class DeltaPackInteger
    {
        static readonly byte[] buckets8 = { 2, 4, 6, 8 };
        static readonly byte[] buckets16 = { 4, 8, 12, 16 };
        static readonly byte[] buckets64 = { 4, 8, 24, 64 };

        static void WriteChangedDiff(BitPacker packer, ulong zigzag, byte[] buckets)
        {
            int bits = 64 - PackingIntegers.CountLeadingZeroBits(zigzag);
            int selector = 0;
            while (bits > buckets[selector]) selector++;
            ulong prefix = 1UL | ((ulong)selector << 1);
            byte payloadBits = buckets[selector];
            if (payloadBits == 64)
            {
                packer.WriteBits((zigzag << 3) | prefix, 64);
                packer.WriteBits(zigzag >> 61, 3);
            }
            else
                packer.WriteBits((zigzag << 3) | prefix, (byte)(payloadBits + 3));
        }

        // The 32-bit widths 4, 8, 16 and 32 as constants: every 32-bit field of every delta-packed struct
        // comes through here, and the table lookup and its search cost more than the write.
        // The packer is not the first parameter, or codegen would take this for the ulong serializer.
        static void WriteChangedDiff32(ulong zigzag, BitPacker packer)
        {
            int bits = 64 - PackingIntegers.CountLeadingZeroBits(zigzag);
            ulong selector;
            byte payloadBits;
            if (bits <= 4) { selector = 0; payloadBits = 4; }
            else if (bits <= 8) { selector = 1; payloadBits = 8; }
            else if (bits <= 16) { selector = 2; payloadBits = 16; }
            else { selector = 3; payloadBits = 32; }
            packer.WriteBits((zigzag << 3) | 1UL | (selector << 1), (byte)(payloadBits + 3));
        }

        static int Bucket32(int selector) => selector switch { 0 => 4, 1 => 8, 2 => 16, _ => 32 };

        static bool ReadChangedDiff32(out ulong diff, BitPacker packer)
        {
            const int widestBits = 3 + 32;
            if (packer.HasReadableBits(widestBits))
            {
                ulong word = packer.PeekBitsWithoutChecks(widestBits);
                if ((word & 1) == 0)
                {
                    packer.SkipBitsWithoutChecks(1);
                    diff = 0;
                    return false;
                }

                int payloadBits = Bucket32((int)(word >> 1) & 3);
                diff = (word >> 3) & ((1UL << payloadBits) - 1);
                packer.SkipBitsWithoutChecks(3 + payloadBits);
                return true;
            }

            if (!packer.ReadBit())
            {
                diff = 0;
                return false;
            }

            diff = packer.ReadBits((byte)Bucket32((int)packer.ReadBits(2)));
            return true;
        }

        static ulong ReadDiff(BitPacker packer, byte[] buckets)
        {
            int selector = (int)packer.ReadBits(2);
            return packer.ReadBits(buckets[selector]);
        }

        // Reads the changed flag, bucket selector and payload with one buffer access when the
        // widest encoding fits in a word; otherwise falls back to reading them one by one.
        static bool ReadChangedDiff(byte[] buckets, BitPacker packer, out ulong diff)
        {
            int widestBits = 3 + buckets[3];
            if (widestBits <= 64 && packer.HasReadableBits(widestBits))
            {
                ulong word = packer.PeekBitsWithoutChecks((byte)widestBits);
                if ((word & 1) == 0)
                {
                    packer.SkipBitsWithoutChecks(1);
                    diff = 0;
                    return false;
                }

                int payloadBits = buckets[(int)(word >> 1) & 3];
                diff = (word >> 3) & ((1UL << payloadBits) - 1);
                packer.SkipBitsWithoutChecks(3 + payloadBits);
                return true;
            }

            if (!packer.ReadBit())
            {
                diff = 0;
                return false;
            }

            diff = ReadDiff(packer, buckets);
            return true;
        }

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
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff(packer, PackingIntegers.ZigzagEncode((sbyte)(newvalue - oldvalue)), buckets8);
            return true;
        }

        [UsedByIL]
        public static void ReadInt8(BitPacker packer, sbyte oldvalue, ref sbyte value)
        {
            if (ReadChangedDiff(buckets8, packer, out ulong zigzag))
            {
                sbyte diff = PackingIntegers.ZigzagDecode((byte)zigzag);
                value = (sbyte)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt8(BitPacker packer, byte oldvalue, byte newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff(packer, PackingIntegers.ZigzagEncode((sbyte)(newvalue - oldvalue)), buckets8);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt8(BitPacker packer, byte oldvalue, ref byte value)
        {
            if (ReadChangedDiff(buckets8, packer, out ulong zigzag))
            {
                sbyte diff = PackingIntegers.ZigzagDecode((byte)zigzag);
                value = (byte)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt16(BitPacker packer, short oldvalue, short newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff(packer, PackingIntegers.ZigzagEncode((short)(newvalue - oldvalue)), buckets16);
            return true;
        }

        [UsedByIL]
        public static void ReadInt16(BitPacker packer, short oldvalue, ref short value)
        {
            if (ReadChangedDiff(buckets16, packer, out ulong zigzag))
            {
                short diff = PackingIntegers.ZigzagDecode((ushort)zigzag);
                value = (short)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, ushort oldvalue, ushort newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff(packer, PackingIntegers.ZigzagEncode((short)(newvalue - oldvalue)), buckets16);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, ushort oldvalue, ref ushort value)
        {
            if (ReadChangedDiff(buckets16, packer, out ulong zigzag))
            {
                short diff = PackingIntegers.ZigzagDecode((ushort)zigzag);
                value = (ushort)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt32(BitPacker packer, int oldvalue, int newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff32(PackingIntegers.ZigzagEncode(newvalue - oldvalue), packer);
            return true;
        }

        [UsedByIL]
        public static void ReadInt32(BitPacker packer, int oldvalue, ref int value)
        {
            if (ReadChangedDiff32(out ulong zigzag, packer))
            {
                int diff = PackingIntegers.ZigzagDecode((uint)zigzag);
                value = oldvalue + diff;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt32(BitPacker packer, uint oldvalue, uint newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff32(PackingIntegers.ZigzagEncode((int)(newvalue - oldvalue)), packer);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt32(BitPacker packer, uint oldvalue, ref uint value)
        {
            if (ReadChangedDiff32(out ulong zigzag, packer))
            {
                int diff = PackingIntegers.ZigzagDecode((uint)zigzag);
                value = (uint)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt64(BitPacker packer, long oldvalue, long newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff(packer, PackingIntegers.ZigzagEncode(newvalue - oldvalue), buckets64);
            return true;
        }

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, long oldvalue, ref long value)
        {
            if (packer.ReadBit())
            {
                long diff = PackingIntegers.ZigzagDecode(ReadDiff(packer, buckets64));
                value = oldvalue + diff;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, ulong oldvalue, ulong newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            WriteChangedDiff(packer, PackingIntegers.ZigzagEncode((long)(newvalue - oldvalue)), buckets64);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, ulong oldvalue, ref ulong value)
        {
            if (packer.ReadBit())
            {
                long diff = PackingIntegers.ZigzagDecode(ReadDiff(packer, buckets64));
                value = (ulong)((long)oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt32(BitPacker packer, PackedUInt oldvalue, PackedUInt newvalue)
        {
            return WriteUInt32(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadUInt32(BitPacker packer, PackedUInt oldvalue, ref PackedUInt value)
        {
            ReadUInt32(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteInt64(BitPacker packer, PackedLong oldvalue, PackedLong newvalue)
        {
            return WriteInt64(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, PackedLong oldvalue, ref PackedLong value)
        {
            ReadInt64(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteIndex(BitPacker packer, Size oldvalue, Size newvalue)
        {
            return WriteUInt32(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadIndex(BitPacker packer, Size oldvalue, ref Size value)
        {
            ReadUInt32(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, PackedULong oldvalue, PackedULong newvalue)
        {
            return WriteUInt64(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, PackedULong oldvalue, ref PackedULong value)
        {
            ReadUInt64(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, PackedUShort oldvalue, PackedUShort newvalue)
        {
            return WriteUInt16(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, PackedUShort oldvalue, ref PackedUShort value)
        {
            ReadUInt16(packer, oldvalue.value, ref value.value);
        }
    }
}
