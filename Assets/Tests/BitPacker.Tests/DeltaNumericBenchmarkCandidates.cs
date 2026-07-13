using System;
using PurrNet.Packing;
using UnityEngine;

/// <summary>
/// Exact, allocation-free candidate codecs used only by the delta codec benchmarks.
/// None of these methods are registered with Packer or DeltaPacker.
/// </summary>
internal sealed class DeltaNumericBenchmarkCandidates
{
    private DeltaNumericBenchmarkCandidates() { }

    private const byte HybridRaw = 0;
    private const byte HybridArithmeticLeb = 1;
    private const byte HybridXorLeb = 2;
    private const byte HybridXorWindow = 3;

    private const byte QuaternionDirectDelta = 0;
    private const byte QuaternionSignFlippedDelta = 1;
    private const byte QuaternionRaw = 2;

    private const uint FloatSignMask = 0x80000000U;

    internal static bool WriteIntAdaptive(BitPacker packer, int oldValue, int newValue)
    {
        bool changed = oldValue != newValue;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int delta = unchecked(newValue - oldValue);
        ulong encodedDelta = PackingIntegers.ZigzagEncode(delta);
        bool writeRaw = LebBitCost(encodedDelta) >= 32;
        packer.WriteBit(writeRaw);

        if (writeRaw)
            packer.WriteBits(unchecked((uint)newValue), 32);
        else
            WriteLeb(packer, encodedDelta);

        return true;
    }

    internal static void ReadIntAdaptive(BitPacker packer, int oldValue, ref int value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        if (packer.ReadBit())
        {
            value = unchecked((int)(uint)packer.ReadBits(32));
            return;
        }

        int delta = PackingIntegers.ZigzagDecode((uint)ReadLeb(packer));
        value = unchecked(oldValue + delta);
    }

    internal static bool WriteLongAdaptive(BitPacker packer, long oldValue, long newValue)
    {
        bool changed = oldValue != newValue;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        long delta = unchecked(newValue - oldValue);
        ulong encodedDelta = PackingIntegers.ZigzagEncode(delta);
        bool writeRaw = LebBitCost(encodedDelta) >= 64;
        packer.WriteBit(writeRaw);

        if (writeRaw)
            packer.WriteBits(unchecked((ulong)newValue), 64);
        else
            WriteLeb(packer, encodedDelta);

        return true;
    }

    internal static void ReadLongAdaptive(BitPacker packer, long oldValue, ref long value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        if (packer.ReadBit())
        {
            value = unchecked((long)packer.ReadBits(64));
            return;
        }

        long delta = PackingIntegers.ZigzagDecode(ReadLeb(packer));
        value = unchecked(oldValue + delta);
    }

    internal static bool WriteFloatXorLeb(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = FloatBits(oldValue);
        uint newBits = FloatBits(newValue);
        uint xor = oldBits ^ newBits;
        bool changed = xor != 0;
        packer.WriteBit(changed);
        if (changed)
            WriteLeb(packer, xor);
        return changed;
    }

    internal static void ReadFloatXorLeb(BitPacker packer, float oldValue, ref float value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        uint bits = FloatBits(oldValue) ^ (uint)ReadLeb(packer);
        value = FloatFromBits(bits);
    }

    internal static bool WriteFloatXorWindow(BitPacker packer, float oldValue, float newValue)
    {
        uint xor = FloatBits(oldValue) ^ FloatBits(newValue);
        bool changed = xor != 0;
        packer.WriteBit(changed);
        if (changed)
            WriteFloatWindowPayload(packer, xor);
        return changed;
    }

    internal static void ReadFloatXorWindow(BitPacker packer, float oldValue, ref float value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        uint bits = FloatBits(oldValue) ^ ReadFloatWindowPayload(packer);
        value = FloatFromBits(bits);
    }

    internal static bool WriteFloatHybrid(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = FloatBits(oldValue);
        uint newBits = FloatBits(newValue);
        bool changed = oldBits != newBits;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        byte kind = SelectFloatHybrid(oldBits, newBits, out _);
        WriteFloatHybridPayload(packer, oldBits, newBits, kind);
        return true;
    }

    internal static void ReadFloatHybrid(BitPacker packer, float oldValue, ref float value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        uint bits = ReadFloatHybridPayload(packer, FloatBits(oldValue));
        value = FloatFromBits(bits);
    }

    internal static bool WriteDoubleXorLeb(BitPacker packer, double oldValue, double newValue)
    {
        ulong oldBits = DoubleBits(oldValue);
        ulong newBits = DoubleBits(newValue);
        ulong xor = oldBits ^ newBits;
        bool changed = xor != 0;
        packer.WriteBit(changed);
        if (changed)
            WriteLeb(packer, xor);
        return changed;
    }

    internal static void ReadDoubleXorLeb(BitPacker packer, double oldValue, ref double value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        ulong bits = DoubleBits(oldValue) ^ ReadLeb(packer);
        value = DoubleFromBits(bits);
    }

    internal static bool WriteDoubleXorWindow(BitPacker packer, double oldValue, double newValue)
    {
        ulong xor = DoubleBits(oldValue) ^ DoubleBits(newValue);
        bool changed = xor != 0;
        packer.WriteBit(changed);
        if (changed)
            WriteDoubleWindowPayload(packer, xor);
        return changed;
    }

    internal static void ReadDoubleXorWindow(BitPacker packer, double oldValue, ref double value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        ulong bits = DoubleBits(oldValue) ^ ReadDoubleWindowPayload(packer);
        value = DoubleFromBits(bits);
    }

    internal static bool WriteDoubleHybrid(BitPacker packer, double oldValue, double newValue)
    {
        ulong oldBits = DoubleBits(oldValue);
        ulong newBits = DoubleBits(newValue);
        bool changed = oldBits != newBits;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        byte kind = SelectDoubleHybrid(oldBits, newBits, out _);
        WriteDoubleHybridPayload(packer, oldBits, newBits, kind);
        return true;
    }

    internal static void ReadDoubleHybrid(BitPacker packer, double oldValue, ref double value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        ulong bits = ReadDoubleHybridPayload(packer, DoubleBits(oldValue));
        value = DoubleFromBits(bits);
    }

    internal static bool WriteVector3Adaptive(BitPacker packer, Vector3 oldValue, Vector3 newValue)
    {
        uint oldX = FloatBits(oldValue.x);
        uint oldY = FloatBits(oldValue.y);
        uint oldZ = FloatBits(oldValue.z);
        uint newX = FloatBits(newValue.x);
        uint newY = FloatBits(newValue.y);
        uint newZ = FloatBits(newValue.z);

        byte mask = BuildVector3Mask(oldX, oldY, oldZ, newX, newY, newZ);
        bool changed = mask != 0;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int deltaPayloadBits = PredictVector3DeltaPayloadBits(
            oldX, oldY, oldZ, newX, newY, newZ, mask);
        bool writeRaw = deltaPayloadBits >= 96;
        packer.WriteBit(writeRaw);

        if (writeRaw)
        {
            packer.WriteBits(newX, 32);
            packer.WriteBits(newY, 32);
            packer.WriteBits(newZ, 32);
            return true;
        }

        packer.WriteBits(mask, 3);
        if ((mask & 1) != 0)
            WriteFloatHybridPayload(packer, oldX, newX, SelectFloatHybrid(oldX, newX, out _));
        if ((mask & 2) != 0)
            WriteFloatHybridPayload(packer, oldY, newY, SelectFloatHybrid(oldY, newY, out _));
        if ((mask & 4) != 0)
            WriteFloatHybridPayload(packer, oldZ, newZ, SelectFloatHybrid(oldZ, newZ, out _));
        return true;
    }

    internal static void ReadVector3Adaptive(BitPacker packer, Vector3 oldValue, ref Vector3 value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        uint x;
        uint y;
        uint z;
        if (packer.ReadBit())
        {
            x = (uint)packer.ReadBits(32);
            y = (uint)packer.ReadBits(32);
            z = (uint)packer.ReadBits(32);
        }
        else
        {
            byte mask = (byte)packer.ReadBits(3);
            x = FloatBits(oldValue.x);
            y = FloatBits(oldValue.y);
            z = FloatBits(oldValue.z);
            if ((mask & 1) != 0)
                x = ReadFloatHybridPayload(packer, x);
            if ((mask & 2) != 0)
                y = ReadFloatHybridPayload(packer, y);
            if ((mask & 4) != 0)
                z = ReadFloatHybridPayload(packer, z);
        }

        value.x = FloatFromBits(x);
        value.y = FloatFromBits(y);
        value.z = FloatFromBits(z);
    }

    internal static bool WriteQuaternionAdaptive(BitPacker packer, Quaternion oldValue, Quaternion newValue)
    {
        uint oldX = FloatBits(oldValue.x);
        uint oldY = FloatBits(oldValue.y);
        uint oldZ = FloatBits(oldValue.z);
        uint oldW = FloatBits(oldValue.w);
        uint newX = FloatBits(newValue.x);
        uint newY = FloatBits(newValue.y);
        uint newZ = FloatBits(newValue.z);
        uint newW = FloatBits(newValue.w);

        bool changed = oldX != newX || oldY != newY || oldZ != newZ || oldW != newW;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        byte mode = SelectQuaternionMode(
            oldX, oldY, oldZ, oldW, newX, newY, newZ, newW, out byte mask);
        packer.WriteBits(mode, 2);

        if (mode == QuaternionRaw)
        {
            packer.WriteBits(newX, 32);
            packer.WriteBits(newY, 32);
            packer.WriteBits(newZ, 32);
            packer.WriteBits(newW, 32);
            return true;
        }

        bool flipSign = mode == QuaternionSignFlippedDelta;
        if (flipSign)
        {
            newX ^= FloatSignMask;
            newY ^= FloatSignMask;
            newZ ^= FloatSignMask;
            newW ^= FloatSignMask;
        }

        packer.WriteBits(mask, 4);
        if ((mask & 1) != 0)
            WriteFloatHybridPayload(packer, oldX, newX, SelectFloatHybrid(oldX, newX, out _));
        if ((mask & 2) != 0)
            WriteFloatHybridPayload(packer, oldY, newY, SelectFloatHybrid(oldY, newY, out _));
        if ((mask & 4) != 0)
            WriteFloatHybridPayload(packer, oldZ, newZ, SelectFloatHybrid(oldZ, newZ, out _));
        if ((mask & 8) != 0)
            WriteFloatHybridPayload(packer, oldW, newW, SelectFloatHybrid(oldW, newW, out _));
        return true;
    }

    internal static void ReadQuaternionAdaptive(BitPacker packer, Quaternion oldValue, ref Quaternion value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        byte mode = (byte)packer.ReadBits(2);
        uint x;
        uint y;
        uint z;
        uint w;

        if (mode == QuaternionRaw)
        {
            x = (uint)packer.ReadBits(32);
            y = (uint)packer.ReadBits(32);
            z = (uint)packer.ReadBits(32);
            w = (uint)packer.ReadBits(32);
        }
        else
        {
            byte mask = (byte)packer.ReadBits(4);
            x = FloatBits(oldValue.x);
            y = FloatBits(oldValue.y);
            z = FloatBits(oldValue.z);
            w = FloatBits(oldValue.w);
            if ((mask & 1) != 0)
                x = ReadFloatHybridPayload(packer, x);
            if ((mask & 2) != 0)
                y = ReadFloatHybridPayload(packer, y);
            if ((mask & 4) != 0)
                z = ReadFloatHybridPayload(packer, z);
            if ((mask & 8) != 0)
                w = ReadFloatHybridPayload(packer, w);

            if (mode == QuaternionSignFlippedDelta)
            {
                x ^= FloatSignMask;
                y ^= FloatSignMask;
                z ^= FloatSignMask;
                w ^= FloatSignMask;
            }
        }

        value.x = FloatFromBits(x);
        value.y = FloatFromBits(y);
        value.z = FloatFromBits(z);
        value.w = FloatFromBits(w);
    }

    internal static int PredictIntAdaptiveBits(int oldValue, int newValue)
    {
        if (oldValue == newValue)
            return 1;
        int delta = unchecked(newValue - oldValue);
        return 2 + Math.Min(32, LebBitCost(PackingIntegers.ZigzagEncode(delta)));
    }

    internal static int PredictLongAdaptiveBits(long oldValue, long newValue)
    {
        if (oldValue == newValue)
            return 1;
        long delta = unchecked(newValue - oldValue);
        return 2 + Math.Min(64, LebBitCost(PackingIntegers.ZigzagEncode(delta)));
    }

    internal static int PredictFloatXorLebBits(float oldValue, float newValue)
    {
        uint xor = FloatBits(oldValue) ^ FloatBits(newValue);
        return xor == 0 ? 1 : 1 + LebBitCost(xor);
    }

    internal static int PredictFloatXorWindowBits(float oldValue, float newValue)
    {
        uint xor = FloatBits(oldValue) ^ FloatBits(newValue);
        return xor == 0 ? 1 : 1 + FloatWindowPayloadBitCost(xor);
    }

    internal static int PredictFloatHybridBits(float oldValue, float newValue)
    {
        uint oldBits = FloatBits(oldValue);
        uint newBits = FloatBits(newValue);
        if (oldBits == newBits)
            return 1;
        SelectFloatHybrid(oldBits, newBits, out int encodedBits);
        return 1 + encodedBits;
    }

    internal static int PredictDoubleXorLebBits(double oldValue, double newValue)
    {
        ulong xor = DoubleBits(oldValue) ^ DoubleBits(newValue);
        return xor == 0 ? 1 : 1 + LebBitCost(xor);
    }

    internal static int PredictDoubleXorWindowBits(double oldValue, double newValue)
    {
        ulong xor = DoubleBits(oldValue) ^ DoubleBits(newValue);
        return xor == 0 ? 1 : 1 + DoubleWindowPayloadBitCost(xor);
    }

    internal static int PredictDoubleHybridBits(double oldValue, double newValue)
    {
        ulong oldBits = DoubleBits(oldValue);
        ulong newBits = DoubleBits(newValue);
        if (oldBits == newBits)
            return 1;
        SelectDoubleHybrid(oldBits, newBits, out int encodedBits);
        return 1 + encodedBits;
    }

    internal static int PredictVector3AdaptiveBits(Vector3 oldValue, Vector3 newValue)
    {
        uint oldX = FloatBits(oldValue.x);
        uint oldY = FloatBits(oldValue.y);
        uint oldZ = FloatBits(oldValue.z);
        uint newX = FloatBits(newValue.x);
        uint newY = FloatBits(newValue.y);
        uint newZ = FloatBits(newValue.z);
        byte mask = BuildVector3Mask(oldX, oldY, oldZ, newX, newY, newZ);
        if (mask == 0)
            return 1;

        int deltaPayload = PredictVector3DeltaPayloadBits(
            oldX, oldY, oldZ, newX, newY, newZ, mask);
        return 2 + Math.Min(96, deltaPayload);
    }

    internal static int PredictQuaternionAdaptiveBits(Quaternion oldValue, Quaternion newValue)
    {
        uint oldX = FloatBits(oldValue.x);
        uint oldY = FloatBits(oldValue.y);
        uint oldZ = FloatBits(oldValue.z);
        uint oldW = FloatBits(oldValue.w);
        uint newX = FloatBits(newValue.x);
        uint newY = FloatBits(newValue.y);
        uint newZ = FloatBits(newValue.z);
        uint newW = FloatBits(newValue.w);
        if (oldX == newX && oldY == newY && oldZ == newZ && oldW == newW)
            return 1;

        int direct = PredictQuaternionDeltaPayloadBits(
            oldX, oldY, oldZ, oldW, newX, newY, newZ, newW, false, out _);
        int flipped = PredictQuaternionDeltaPayloadBits(
            oldX, oldY, oldZ, oldW, newX, newY, newZ, newW, true, out _);
        return 3 + Math.Min(128, Math.Min(direct, flipped));
    }

    private static void WriteFloatHybridPayload(BitPacker packer, uint oldBits, uint newBits, byte kind)
    {
        WriteHybridKind(packer, kind);
        switch (kind)
        {
            case HybridRaw:
                packer.WriteBits(newBits, 32);
                break;
            case HybridArithmeticLeb:
            {
                int delta = unchecked((int)(newBits - oldBits));
                WriteLeb(packer, PackingIntegers.ZigzagEncode(delta));
                break;
            }
            case HybridXorLeb:
                WriteLeb(packer, oldBits ^ newBits);
                break;
            default:
                WriteFloatWindowPayload(packer, oldBits ^ newBits);
                break;
        }
    }

    private static uint ReadFloatHybridPayload(BitPacker packer, uint oldBits)
    {
        byte kind = ReadHybridKind(packer);
        switch (kind)
        {
            case HybridRaw:
                return (uint)packer.ReadBits(32);
            case HybridArithmeticLeb:
            {
                int delta = PackingIntegers.ZigzagDecode((uint)ReadLeb(packer));
                return unchecked(oldBits + (uint)delta);
            }
            case HybridXorLeb:
                return oldBits ^ (uint)ReadLeb(packer);
            default:
                return oldBits ^ ReadFloatWindowPayload(packer);
        }
    }

    private static byte SelectFloatHybrid(uint oldBits, uint newBits, out int encodedBits)
    {
        byte kind = HybridRaw;
        encodedBits = 1 + 32;

        int arithmeticDelta = unchecked((int)(newBits - oldBits));
        int arithmeticBits = 2 + LebBitCost(PackingIntegers.ZigzagEncode(arithmeticDelta));
        if (arithmeticBits < encodedBits)
        {
            kind = HybridArithmeticLeb;
            encodedBits = arithmeticBits;
        }

        uint xor = oldBits ^ newBits;
        int xorLebBits = 3 + LebBitCost(xor);
        if (xorLebBits < encodedBits)
        {
            kind = HybridXorLeb;
            encodedBits = xorLebBits;
        }

        int xorWindowBits = 3 + FloatWindowPayloadBitCost(xor);
        if (xorWindowBits < encodedBits)
        {
            kind = HybridXorWindow;
            encodedBits = xorWindowBits;
        }

        return kind;
    }

    private static void WriteDoubleHybridPayload(BitPacker packer, ulong oldBits, ulong newBits, byte kind)
    {
        WriteHybridKind(packer, kind);
        switch (kind)
        {
            case HybridRaw:
                packer.WriteBits(newBits, 64);
                break;
            case HybridArithmeticLeb:
            {
                long delta = unchecked((long)(newBits - oldBits));
                WriteLeb(packer, PackingIntegers.ZigzagEncode(delta));
                break;
            }
            case HybridXorLeb:
                WriteLeb(packer, oldBits ^ newBits);
                break;
            default:
                WriteDoubleWindowPayload(packer, oldBits ^ newBits);
                break;
        }
    }

    private static ulong ReadDoubleHybridPayload(BitPacker packer, ulong oldBits)
    {
        byte kind = ReadHybridKind(packer);
        switch (kind)
        {
            case HybridRaw:
                return packer.ReadBits(64);
            case HybridArithmeticLeb:
            {
                long delta = PackingIntegers.ZigzagDecode(ReadLeb(packer));
                return unchecked(oldBits + (ulong)delta);
            }
            case HybridXorLeb:
                return oldBits ^ ReadLeb(packer);
            default:
                return oldBits ^ ReadDoubleWindowPayload(packer);
        }
    }

    private static byte SelectDoubleHybrid(ulong oldBits, ulong newBits, out int encodedBits)
    {
        byte kind = HybridRaw;
        encodedBits = 1 + 64;

        long arithmeticDelta = unchecked((long)(newBits - oldBits));
        int arithmeticBits = 2 + LebBitCost(PackingIntegers.ZigzagEncode(arithmeticDelta));
        if (arithmeticBits < encodedBits)
        {
            kind = HybridArithmeticLeb;
            encodedBits = arithmeticBits;
        }

        ulong xor = oldBits ^ newBits;
        int xorLebBits = 3 + LebBitCost(xor);
        if (xorLebBits < encodedBits)
        {
            kind = HybridXorLeb;
            encodedBits = xorLebBits;
        }

        int xorWindowBits = 3 + DoubleWindowPayloadBitCost(xor);
        if (xorWindowBits < encodedBits)
        {
            kind = HybridXorWindow;
            encodedBits = xorWindowBits;
        }

        return kind;
    }

    private static void WriteFloatWindowPayload(BitPacker packer, uint xor)
    {
        int leading = CountLeadingZeros(xor);
        int trailing = CountTrailingZeros(xor);
        int significant = 32 - leading - trailing;
        packer.WriteBits((ulong)leading, 5);
        packer.WriteBits((ulong)(significant - 1), 5);
        packer.WriteBits(xor >> trailing, (byte)significant);
    }

    private static uint ReadFloatWindowPayload(BitPacker packer)
    {
        int leading = (int)packer.ReadBits(5);
        int significant = (int)packer.ReadBits(5) + 1;
        int trailing = 32 - leading - significant;
        return (uint)packer.ReadBits((byte)significant) << trailing;
    }

    private static void WriteDoubleWindowPayload(BitPacker packer, ulong xor)
    {
        int leading = CountLeadingZeros(xor);
        int trailing = CountTrailingZeros(xor);
        int significant = 64 - leading - trailing;
        packer.WriteBits((ulong)leading, 6);
        packer.WriteBits((ulong)(significant - 1), 6);
        packer.WriteBits(xor >> trailing, (byte)significant);
    }

    private static ulong ReadDoubleWindowPayload(BitPacker packer)
    {
        int leading = (int)packer.ReadBits(6);
        int significant = (int)packer.ReadBits(6) + 1;
        int trailing = 64 - leading - significant;
        return packer.ReadBits((byte)significant) << trailing;
    }

    private static int FloatWindowPayloadBitCost(uint xor)
    {
        return 10 + 32 - CountLeadingZeros(xor) - CountTrailingZeros(xor);
    }

    private static int DoubleWindowPayloadBitCost(ulong xor)
    {
        return 12 + 64 - CountLeadingZeros(xor) - CountTrailingZeros(xor);
    }

    private static byte BuildVector3Mask(
        uint oldX, uint oldY, uint oldZ, uint newX, uint newY, uint newZ)
    {
        byte mask = 0;
        if (oldX != newX) mask |= 1;
        if (oldY != newY) mask |= 2;
        if (oldZ != newZ) mask |= 4;
        return mask;
    }

    private static int PredictVector3DeltaPayloadBits(
        uint oldX, uint oldY, uint oldZ, uint newX, uint newY, uint newZ, byte mask)
    {
        int bits = 3;
        if ((mask & 1) != 0)
        {
            SelectFloatHybrid(oldX, newX, out int encoded);
            bits += encoded;
        }
        if ((mask & 2) != 0)
        {
            SelectFloatHybrid(oldY, newY, out int encoded);
            bits += encoded;
        }
        if ((mask & 4) != 0)
        {
            SelectFloatHybrid(oldZ, newZ, out int encoded);
            bits += encoded;
        }
        return bits;
    }

    private static byte SelectQuaternionMode(
        uint oldX, uint oldY, uint oldZ, uint oldW,
        uint newX, uint newY, uint newZ, uint newW, out byte mask)
    {
        int directBits = PredictQuaternionDeltaPayloadBits(
            oldX, oldY, oldZ, oldW, newX, newY, newZ, newW, false, out byte directMask);
        int flippedBits = PredictQuaternionDeltaPayloadBits(
            oldX, oldY, oldZ, oldW, newX, newY, newZ, newW, true, out byte flippedMask);

        byte mode = QuaternionDirectDelta;
        int bestBits = directBits;
        mask = directMask;
        if (flippedBits < bestBits)
        {
            mode = QuaternionSignFlippedDelta;
            bestBits = flippedBits;
            mask = flippedMask;
        }
        if (128 < bestBits)
        {
            mode = QuaternionRaw;
            mask = 0;
        }
        return mode;
    }

    private static int PredictQuaternionDeltaPayloadBits(
        uint oldX, uint oldY, uint oldZ, uint oldW,
        uint newX, uint newY, uint newZ, uint newW, bool flipSign, out byte mask)
    {
        if (flipSign)
        {
            newX ^= FloatSignMask;
            newY ^= FloatSignMask;
            newZ ^= FloatSignMask;
            newW ^= FloatSignMask;
        }

        mask = 0;
        int bits = 4;
        if (oldX != newX)
        {
            mask |= 1;
            SelectFloatHybrid(oldX, newX, out int encoded);
            bits += encoded;
        }
        if (oldY != newY)
        {
            mask |= 2;
            SelectFloatHybrid(oldY, newY, out int encoded);
            bits += encoded;
        }
        if (oldZ != newZ)
        {
            mask |= 4;
            SelectFloatHybrid(oldZ, newZ, out int encoded);
            bits += encoded;
        }
        if (oldW != newW)
        {
            mask |= 8;
            SelectFloatHybrid(oldW, newW, out int encoded);
            bits += encoded;
        }
        return bits;
    }

    // Prefix codes favor the bounded raw escape without penalizing the common arithmetic mode:
    // raw=0, arithmetic=10, XOR-LEB=110, XOR-window=111.
    private static void WriteHybridKind(BitPacker packer, byte kind)
    {
        if (kind == HybridRaw)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        if (kind == HybridArithmeticLeb)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        packer.WriteBit(kind == HybridXorWindow);
    }

    private static byte ReadHybridKind(BitPacker packer)
    {
        if (!packer.ReadBit())
            return HybridRaw;
        if (!packer.ReadBit())
            return HybridArithmeticLeb;
        return packer.ReadBit() ? HybridXorWindow : HybridXorLeb;
    }

    private static void WriteLeb(BitPacker packer, ulong value)
    {
        PackingIntegers.Write(packer, new PackedULong(value));
    }

    private static ulong ReadLeb(BitPacker packer)
    {
        PackedULong value = default;
        PackingIntegers.Read(packer, ref value);
        return value.value;
    }

    private static int LebBitCost(ulong value)
    {
        int chunks = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            chunks++;
        }
        return chunks * 8;
    }

    private static uint FloatBits(float value)
    {
        return unchecked((uint)BitConverter.SingleToInt32Bits(value));
    }

    private static float FloatFromBits(uint bits)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static ulong DoubleBits(double value)
    {
        return unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
    }

    private static double DoubleFromBits(ulong bits)
    {
        return BitConverter.Int64BitsToDouble(unchecked((long)bits));
    }

    private static int CountLeadingZeros(uint value)
    {
        if (value == 0) return 32;
        int count = 0;
        if ((value & 0xFFFF0000U) == 0) { count += 16; value <<= 16; }
        if ((value & 0xFF000000U) == 0) { count += 8; value <<= 8; }
        if ((value & 0xF0000000U) == 0) { count += 4; value <<= 4; }
        if ((value & 0xC0000000U) == 0) { count += 2; value <<= 2; }
        if ((value & 0x80000000U) == 0) count++;
        return count;
    }

    private static int CountTrailingZeros(uint value)
    {
        if (value == 0) return 32;
        int count = 0;
        if ((value & 0x0000FFFFU) == 0) { count += 16; value >>= 16; }
        if ((value & 0x000000FFU) == 0) { count += 8; value >>= 8; }
        if ((value & 0x0000000FU) == 0) { count += 4; value >>= 4; }
        if ((value & 0x00000003U) == 0) { count += 2; value >>= 2; }
        if ((value & 0x00000001U) == 0) count++;
        return count;
    }

    private static int CountLeadingZeros(ulong value)
    {
        if (value == 0) return 64;
        int count = 0;
        if ((value & 0xFFFFFFFF00000000UL) == 0) { count += 32; value <<= 32; }
        if ((value & 0xFFFF000000000000UL) == 0) { count += 16; value <<= 16; }
        if ((value & 0xFF00000000000000UL) == 0) { count += 8; value <<= 8; }
        if ((value & 0xF000000000000000UL) == 0) { count += 4; value <<= 4; }
        if ((value & 0xC000000000000000UL) == 0) { count += 2; value <<= 2; }
        if ((value & 0x8000000000000000UL) == 0) count++;
        return count;
    }

    private static int CountTrailingZeros(ulong value)
    {
        if (value == 0) return 64;
        int count = 0;
        if ((value & 0x00000000FFFFFFFFUL) == 0) { count += 32; value >>= 32; }
        if ((value & 0x000000000000FFFFUL) == 0) { count += 16; value >>= 16; }
        if ((value & 0x00000000000000FFUL) == 0) { count += 8; value >>= 8; }
        if ((value & 0x000000000000000FUL) == 0) { count += 4; value >>= 4; }
        if ((value & 0x0000000000000003UL) == 0) { count += 2; value >>= 2; }
        if ((value & 0x0000000000000001UL) == 0) count++;
        return count;
    }
}
