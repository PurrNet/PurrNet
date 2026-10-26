using PurrNet.Modules;
using UnityEngine;

namespace PurrNet.Packing
{
    public static class PackingIntegers
    {
        public static byte ZigzagEncode(sbyte i) => (byte)(((uint)i >> 7) ^ ((uint)i << 1));

        public static sbyte ZigzagDecode(byte i) => (sbyte)((i >> 1) ^ -(i & 1));

        public static ushort ZigzagEncode(short i) => (ushort)(((ulong)i >> 15) ^ ((ulong)i << 1));

        public static short ZigzagDecode(ushort i) => (short)((i >> 1) ^ -(i & 1));

        public static uint ZigzagEncode(int i) => (uint)(((ulong)i >> 31) ^ ((ulong)i << 1));

        public static ulong ZigzagEncode(long i) => (ulong)((i >> 63) ^ (i << 1));

        public static int ZigzagDecode(uint i) => (int)(((long)i >> 1) ^ -((long)i & 1));

        public static long ZigzagDecode(ulong i) => ((long)(i >> 1) & 0x7FFFFFFFFFFFFFFFL) ^ ((long)(i << 63) >> 63);

        public static int CountLeadingZeroBits(ulong value)
        {
            if (value == 0) return 64; // Special case for zero

            int count = 0;
            if ((value & 0xFFFFFFFF00000000) == 0)
            {
                count += 32;
                value <<= 32;
            }

            if ((value & 0xFFFF000000000000) == 0)
            {
                count += 16;
                value <<= 16;
            }

            if ((value & 0xFF00000000000000) == 0)
            {
                count += 8;
                value <<= 8;
            }

            if ((value & 0xF000000000000000) == 0)
            {
                count += 4;
                value <<= 4;
            }

            if ((value & 0xC000000000000000) == 0)
            {
                count += 2;
                value <<= 2;
            }

            if ((value & 0x8000000000000000) == 0)
            {
                count += 1;
            }

            return count;
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedUInt value)
        {
            Write(packer, new PackedULong(value.value));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedUInt value)
        {
            PackedULong packed = default;
            Read(packer, ref packed);
            value = new PackedUInt((uint)packed.value);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedInt value)
        {
            var packed = new PackedUInt(ZigzagEncode(value.value));
            Write(packer, packed);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedInt value)
        {
            PackedUInt packed = default;
            Read(packer, ref packed);
            value = new PackedInt(ZigzagDecode(packed.value));
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedUShort value)
        {
            var packed = new PackedULong(value.value);
            Write(packer, packed);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedUShort value)
        {
            PackedULong packed = default;
            Read(packer, ref packed);
            value = new PackedUShort((ushort)packed.value);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedShort value)
        {
            Write(packer, new PackedULong(ZigzagEncode(value.value)));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedShort value)
        {
            PackedULong packed = default;
            Read(packer, ref packed);
            value = new PackedShort((short)ZigzagDecode(packed.value));
        }

        private const int SEGMENTS = 1 << 5;
        const int TOTAL_BITS = 64;
        const int CHUNK = TOTAL_BITS / SEGMENTS;

        public static void WritePrefixed(BitPacker packer, long value, byte maxBitCount)
        {
            ulong packedValue = ZigzagEncode(value);
            WritePrefixed(packer, packedValue, maxBitCount);
        }

        public static void ReadPrefixed(BitPacker packer, ref long value, byte maxBitCount)
        {
            ulong packedValue = 0;
            ReadPrefixed(packer, ref packedValue, maxBitCount);
            value = ZigzagDecode(packedValue);
        }

        public static void WritePrefixed(BitPacker packer, ulong value, byte maxBitCount)
        {
            byte bitCount = (byte)(TOTAL_BITS - CountLeadingZeroBits(value));
            byte prefixBits = (byte)Mathf.CeilToInt(Mathf.Log(maxBitCount, 2));

            packer.WriteBits(bitCount, prefixBits);

            if (bitCount == 0)
                return;

            packer.WriteBits(value, bitCount);
        }

        public static void ReadPrefixed(BitPacker packer, ref ulong value, byte maxBitCount)
        {
            byte prefixBits = (byte)Mathf.CeilToInt(Mathf.Log(maxBitCount, 2));

            if (prefixBits == 0)
            {
                value = 0;
                return;
            }

            byte bitCount = (byte)packer.ReadBits(prefixBits);
            value = packer.ReadBits(bitCount);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedULong value)
        {
            ulong uvalue = value.value;
            if (uvalue < 0x80UL)
            {
                packer.WriteBits(uvalue, 8);
                return;
            }

            // Assemble up to eight wire bytes before touching the backing buffer.
            // A 64-bit value needs at most two further bytes after this block.
            do
            {
                ulong encoded = 0;
                byte bits = 0;
                do
                {
                    ulong chunk = uvalue & 0x7FUL;
                    uvalue >>= 7;
                    if (uvalue != 0) chunk |= 0x80UL;
                    encoded |= chunk << bits;
                    bits += 8;
                } while (uvalue != 0 && bits < 64);

                packer.WriteBits(encoded, bits);
            } while (uvalue != 0);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedULong value)
        {
            ulong result = 0;
            int shift = 0;

            while (shift < 64)
            {
                ulong chunk = packer.ReadBits(8);
                bool cont = (chunk & 0x80UL) != 0;
                result |= (chunk & 0x7FUL) << shift;
                shift += 7;

                if (!cont) break;
            }

            value.value = result;
        }

        [UsedByIL]
        public static void Write(BitPacker packer, Size value)
        {
            int trailingZeroes = CountLeadingZeroBits(value.value);
            int emptyChunks = trailingZeroes / CHUNK;
            int segmentCount = SEGMENTS - emptyChunks;
            int pointer = 0;

            const uint mask = uint.MaxValue >> (32 - CHUNK);
            do
            {
                uint isolated = (value.value >> pointer) & mask;
                packer.WriteBits(isolated, CHUNK);
                pointer += CHUNK;

                --segmentCount;
                packer.WriteBits(segmentCount <= 0 ? 0u : 1u, 1);
            } while (segmentCount > 0 && pointer < 32);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref Size value)
        {
            uint result = 0;
            int pointer = 0;
            bool continueReading;

            do
            {
                uint chunk = (uint)packer.ReadBits(CHUNK);
                result |= chunk << pointer;
                pointer += CHUNK;

                continueReading = packer.ReadBits(1) == 1;
            } while (continueReading && pointer < 32);

            value.value = result;
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedLong value)
        {
            Write(packer, new PackedULong(ZigzagEncode(value.value)));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedLong value)
        {
            PackedULong packed = default;
            Read(packer, ref packed);
            value = new PackedLong(ZigzagDecode(packed.value));
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedByte value)
        {
            Write(packer, new PackedULong(value.value));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedByte value)
        {
            PackedULong packed = default;
            Read(packer, ref packed);
            value = new PackedByte((byte)packed.value);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedSByte value)
        {
            Write(packer, new PackedByte(ZigzagEncode(value.value)));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedSByte value)
        {
            PackedByte packed = default;
            Read(packer, ref packed);
            value = new PackedSByte(ZigzagDecode(packed.value));
        }
    }
}
