using PurrNet.Modules;
using UnityEngine;
using System;
using System.Runtime.CompilerServices;

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
            CompactIntegerPacking.WriteShifted7UInt32(packer, value.value);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedUInt value)
        {
            value.value = (uint)CompactIntegerPacking.ReadShifted7UInt32(packer);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedInt value)
        {
            CompactIntegerPacking.WriteShifted7UInt32(packer, ZigzagEncode(value.value));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedInt value)
        {
            value.value = ZigzagDecode((uint)CompactIntegerPacking.ReadShifted7UInt32(packer));
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedUShort value)
        {
            CompactIntegerPacking.WriteShifted7UInt16(packer, value.value);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedUShort value)
        {
            value.value = (ushort)CompactIntegerPacking.ReadShifted7UInt16(packer);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedShort value)
        {
            CompactIntegerPacking.WriteShifted7UInt16(packer, ZigzagEncode(value.value));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedShort value)
        {
            value.value = ZigzagDecode((ushort)CompactIntegerPacking.ReadShifted7UInt16(packer));
        }

        const int TOTAL_BITS = 64;

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
            CompactIntegerPacking.WriteShifted7UInt64(packer, value.value);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedULong value)
        {
            value.value = CompactIntegerPacking.ReadShifted7UInt64(packer);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, Size value)
        {
            CompactIntegerPacking.WriteShifted2Size(packer, value.value);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref Size value)
        {
            value.value = CompactIntegerPacking.ReadShifted2Size(packer);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedLong value)
        {
            CompactIntegerPacking.WriteShifted7UInt64(packer, ZigzagEncode(value.value));
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedLong value)
        {
            value.value = ZigzagDecode(CompactIntegerPacking.ReadShifted7UInt64(packer));
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedByte value)
        {
            packer.WriteBits(value.value, 8);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedByte value)
        {
            value.value = (byte)packer.ReadBits(8);
        }

        [UsedByIL]
        public static void Write(BitPacker packer, PackedSByte value)
        {
            packer.WriteBits(unchecked((byte)value.value), 8);
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref PackedSByte value)
        {
            value.value = unchecked((sbyte)packer.ReadBits(8));
        }
    }

    /// <summary>
    /// Width-aware shifted-tier integer codec. Reclaiming overlong encodings keeps every value at
    /// its previous bit count or less, while reducing worst cases to 8/18/36/72 bits (47 for Size).
    /// The non-static container prevents codegen from treating helpers as serializer registrations.
    /// </summary>
    internal sealed class CompactIntegerPacking
    {
        private const ulong S1 = 128UL;
        private const ulong S2 = 16_512UL;
        private const ulong S3 = 2_113_664UL;
        private const ulong S4 = 270_549_120UL;
        private const ulong S5 = 34_630_287_488UL;
        private const ulong S6 = 4_432_676_798_592UL;
        private const ulong S7 = 567_382_630_219_904UL;
        private const ulong S8 = 72_624_976_668_147_840UL;
        private const ulong S9 = 9_295_997_013_522_923_648UL;

        private static readonly ulong[] SizeOffsets =
        {
            0UL, 4UL, 20UL, 84UL, 340UL, 1_364UL, 5_460UL, 21_844UL,
            87_380UL, 349_524UL, 1_398_100UL, 5_592_404UL, 22_369_620UL,
            89_478_484UL, 357_913_940UL, 1_431_655_764UL
        };

        private CompactIntegerPacking() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteShifted7UInt16(BitPacker packer, ulong value)
        {
            if (value < S1)
            {
                Write7Tokens(packer, value, 1, false);
                return;
            }

            if (value < S2)
            {
                Write7Tokens(packer, value - S1, 2, false);
                return;
            }

            ulong payload = value - S2;
            ulong packed = Pack7Tokens(payload, 2, true) | ((payload >> 14) << 16);
            packer.WriteBits(packed, 18);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadShifted7UInt16(BitPacker packer)
        {
            ulong token = packer.ReadBits(8);
            ulong payload = token & 0x7FUL;
            if ((token & 0x80UL) == 0)
                return payload;

            token = packer.ReadBits(8);
            payload |= (token & 0x7FUL) << 7;
            if ((token & 0x80UL) == 0)
                return S1 + payload;

            payload |= packer.ReadBits(2) << 14;
            if (payload > ushort.MaxValue - S2)
                throw new InvalidOperationException("Packed UInt16 terminal exceeds its declared width.");
            return S2 + payload;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteShifted7UInt32(BitPacker packer, ulong value)
        {
            if (value < S1) { Write7Tokens(packer, value, 1, false); return; }
            if (value < S2) { Write7Tokens(packer, value - S1, 2, false); return; }
            if (value < S3) { Write7Tokens(packer, value - S2, 3, false); return; }
            if (value < S4) { Write7Tokens(packer, value - S3, 4, false); return; }

            ulong payload = value - S4;
            ulong packed = Pack7Tokens(payload, 4, true) | ((payload >> 28) << 32);
            packer.WriteBits(packed, 36);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadShifted7UInt32(BitPacker packer)
        {
            ulong token = packer.ReadBits(8);
            ulong payload = token & 0x7FUL;
            if ((token & 0x80UL) == 0) return payload;

            token = packer.ReadBits(8);
            payload |= (token & 0x7FUL) << 7;
            if ((token & 0x80UL) == 0) return S1 + payload;

            token = packer.ReadBits(8);
            payload |= (token & 0x7FUL) << 14;
            if ((token & 0x80UL) == 0) return S2 + payload;

            token = packer.ReadBits(8);
            payload |= (token & 0x7FUL) << 21;
            if ((token & 0x80UL) == 0) return S3 + payload;

            payload |= packer.ReadBits(4) << 28;
            if (payload > uint.MaxValue - S4)
                throw new InvalidOperationException("Packed UInt32 terminal exceeds its declared width.");
            return S4 + payload;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteShifted7UInt64(BitPacker packer, ulong value)
        {
            if (value < S1) { Write7Tokens(packer, value, 1, false); return; }
            if (value < S2) { Write7Tokens(packer, value - S1, 2, false); return; }
            if (value < S3) { Write7Tokens(packer, value - S2, 3, false); return; }
            if (value < S4) { Write7Tokens(packer, value - S3, 4, false); return; }
            if (value < S5) { Write7Tokens(packer, value - S4, 5, false); return; }
            if (value < S6) { Write7Tokens(packer, value - S5, 6, false); return; }
            if (value < S7) { Write7Tokens(packer, value - S6, 7, false); return; }
            if (value < S8) { Write7Tokens(packer, value - S7, 8, false); return; }
            if (value < S9) { Write7Tokens(packer, value - S8, 9, false); return; }
            Write7Tokens(packer, value - S9, 9, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadShifted7UInt64(BitPacker packer)
        {
            ulong payload = 0;
            ulong offset = 0;
            ulong groupSize = S1;

            for (int tokenIndex = 0; tokenIndex < 9; tokenIndex++)
            {
                ulong token = packer.ReadBits(8);
                payload |= (token & 0x7FUL) << (tokenIndex * 7);
                if ((token & 0x80UL) == 0)
                    return offset + payload;

                offset += groupSize;
                if (tokenIndex < 8)
                    groupSize <<= 7;
            }

            if (payload > ulong.MaxValue - S9)
                throw new InvalidOperationException("Packed UInt64 terminal exceeds its declared width.");
            return S9 + payload;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteShifted2Size(BitPacker packer, uint value)
        {
            int tier;
            if (value < 4)
            {
                tier = 1;
            }
            else
            {
                int bitLength = 64 - PackingIntegers.CountLeadingZeroBits(value);
                int standardTier = (bitLength + 1) >> 1;
                tier = value < SizeOffsets[standardTier - 1] ? standardTier - 1 : standardTier;
            }

            if (tier <= 15)
            {
                ulong payload = value - SizeOffsets[tier - 1];
                packer.WriteBits(Pack2Tokens(payload, tier, false), (byte)(tier * 3));
                return;
            }

            ulong terminalPayload = value - SizeOffsets[15];
            ulong packed = Pack2Tokens(terminalPayload, 15, true) | ((terminalPayload >> 30) << 45);
            packer.WriteBits(packed, 47);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ReadShifted2Size(BitPacker packer)
        {
            ulong payload = 0;
            ulong offset = 0;
            ulong groupSize = 4;

            for (int tokenIndex = 0; tokenIndex < 15; tokenIndex++)
            {
                ulong token = packer.ReadBits(3);
                payload |= (token & 0x3UL) << (tokenIndex * 2);
                if ((token & 0x4UL) == 0)
                    return (uint)(offset + payload);

                offset += groupSize;
                if (tokenIndex < 14)
                    groupSize <<= 2;
            }

            payload |= packer.ReadBits(2) << 30;
            ulong terminalOffset = SizeOffsets[15];
            if (payload > uint.MaxValue - terminalOffset)
                throw new InvalidOperationException("Packed Size terminal exceeds its declared width.");
            return (uint)(terminalOffset + payload);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool WriteDelta8(BitPacker packer, ulong oldValue, ulong newValue)
        {
            oldValue &= byte.MaxValue;
            newValue &= byte.MaxValue;
            bool changed = oldValue != newValue;
            packer.WriteBit(changed);
            if (!changed) return false;
            ulong delta = unchecked(newValue - oldValue) & byte.MaxValue;
            packer.WriteBits(EncodeNonZeroZigzag(delta, 0x80UL, byte.MaxValue), 8);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadDelta8(BitPacker packer, ulong oldValue)
        {
            oldValue &= byte.MaxValue;
            if (!packer.ReadBit()) return oldValue;
            ulong delta = DecodeNonZeroZigzag(packer.ReadBits(8), 0x80UL, byte.MaxValue);
            return unchecked(oldValue + delta) & byte.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool WriteDelta16(BitPacker packer, ulong oldValue, ulong newValue)
        {
            oldValue &= ushort.MaxValue;
            newValue &= ushort.MaxValue;
            bool changed = oldValue != newValue;
            packer.WriteBit(changed);
            if (!changed) return false;
            ulong delta = unchecked(newValue - oldValue) & ushort.MaxValue;
            WriteShifted7UInt16(packer, EncodeNonZeroZigzag(delta, 0x8000UL, ushort.MaxValue));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadDelta16(BitPacker packer, ulong oldValue)
        {
            oldValue &= ushort.MaxValue;
            if (!packer.ReadBit()) return oldValue;
            ulong encoded = ReadShifted7UInt16(packer);
            ulong delta = DecodeNonZeroZigzag(encoded, 0x8000UL, ushort.MaxValue);
            return unchecked(oldValue + delta) & ushort.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool WriteDelta32(BitPacker packer, ulong oldValue, ulong newValue)
        {
            oldValue &= uint.MaxValue;
            newValue &= uint.MaxValue;
            bool changed = oldValue != newValue;
            packer.WriteBit(changed);
            if (!changed) return false;
            ulong delta = unchecked(newValue - oldValue) & uint.MaxValue;
            WriteShifted7UInt32(packer, EncodeNonZeroZigzag(delta, 0x80000000UL, uint.MaxValue));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadDelta32(BitPacker packer, ulong oldValue)
        {
            oldValue &= uint.MaxValue;
            if (!packer.ReadBit()) return oldValue;
            ulong encoded = ReadShifted7UInt32(packer);
            ulong delta = DecodeNonZeroZigzag(encoded, 0x80000000UL, uint.MaxValue);
            return unchecked(oldValue + delta) & uint.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool WriteDelta64(BitPacker packer, ulong oldValue, ulong newValue)
        {
            bool changed = oldValue != newValue;
            packer.WriteBit(changed);
            if (!changed) return false;
            ulong delta = unchecked(newValue - oldValue);
            WriteShifted7UInt64(packer, EncodeNonZeroZigzag(delta, 0x8000000000000000UL, ulong.MaxValue));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ReadDelta64(BitPacker packer, ulong oldValue)
        {
            if (!packer.ReadBit()) return oldValue;
            ulong encoded = ReadShifted7UInt64(packer);
            ulong delta = DecodeNonZeroZigzag(encoded, 0x8000000000000000UL, ulong.MaxValue);
            return unchecked(oldValue + delta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong EncodeNonZeroZigzag(ulong raw, ulong signBit, ulong mask)
        {
            if (raw == signBit)
                return mask - 1UL;

            ulong sign = (raw & signBit) == 0 ? 0UL : 1UL;
            ulong zigzag = ((raw << 1) ^ (0UL - sign)) & mask;
            return sign == 0 ? zigzag - 2UL : zigzag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong DecodeNonZeroZigzag(ulong encoded, ulong signBit, ulong mask)
        {
            if (encoded == mask)
                throw new InvalidOperationException("A compact non-zero delta uses its reserved terminal code.");
            if (encoded == mask - 1UL)
                return signBit;

            ulong zigzag = (encoded & 1UL) == 0 ? encoded + 2UL : encoded;
            return ((zigzag >> 1) ^ (0UL - (zigzag & 1UL))) & mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Pack7Tokens(ulong payload, int tokenCount, bool allContinue)
        {
            ulong packed = 0;
            for (int tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
            {
                ulong continuation = allContinue || tokenIndex + 1 < tokenCount ? 0x80UL : 0UL;
                packed |= ((payload & 0x7FUL) | continuation) << (tokenIndex * 8);
                payload >>= 7;
            }
            return packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write7Tokens(BitPacker packer, ulong payload, int tokenCount, bool allContinue)
        {
            if (tokenCount <= 8)
            {
                packer.WriteBits(Pack7Tokens(payload, tokenCount, allContinue), (byte)(tokenCount * 8));
                return;
            }

            packer.WriteBits(Pack7Tokens(payload, 8, true), 64);
            ulong finalToken = (payload >> 56) & 0x7FUL;
            if (allContinue)
                finalToken |= 0x80UL;
            packer.WriteBits(finalToken, 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Pack2Tokens(ulong payload, int tokenCount, bool allContinue)
        {
            ulong packed = 0;
            for (int tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
            {
                ulong continuation = allContinue || tokenIndex + 1 < tokenCount ? 0x4UL : 0UL;
                packed |= ((payload & 0x3UL) | continuation) << (tokenIndex * 3);
                payload >>= 2;
            }
            return packed;
        }
    }
}
