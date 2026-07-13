using System;
using PurrNet.Packing;

internal enum PackedBenchmarkKind : byte
{
    PackedByte,
    PackedSByte,
    PackedUShort,
    PackedShort,
    PackedUInt,
    PackedInt,
    PackedULong,
    PackedLong,
    Size
}

internal readonly struct PackedBenchmarkValue : IEquatable<PackedBenchmarkValue>
{
    internal readonly PackedBenchmarkKind kind;
    internal readonly ulong raw;

    internal PackedBenchmarkValue(PackedBenchmarkKind kind, ulong raw)
    {
        this.kind = kind;
        this.raw = raw & Mask(kind);
    }

    internal static int GetWidth(PackedBenchmarkKind kind)
    {
        switch (kind)
        {
            case PackedBenchmarkKind.PackedByte:
            case PackedBenchmarkKind.PackedSByte:
                return 8;
            case PackedBenchmarkKind.PackedUShort:
            case PackedBenchmarkKind.PackedShort:
                return 16;
            case PackedBenchmarkKind.PackedUInt:
            case PackedBenchmarkKind.PackedInt:
            case PackedBenchmarkKind.Size:
                return 32;
            case PackedBenchmarkKind.PackedULong:
            case PackedBenchmarkKind.PackedLong:
                return 64;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    internal static bool IsSigned(PackedBenchmarkKind kind)
    {
        return kind == PackedBenchmarkKind.PackedSByte ||
               kind == PackedBenchmarkKind.PackedShort ||
               kind == PackedBenchmarkKind.PackedInt ||
               kind == PackedBenchmarkKind.PackedLong;
    }

    internal static ulong Mask(PackedBenchmarkKind kind)
    {
        int width = GetWidth(kind);
        return width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
    }

    internal long SignedValue
    {
        get
        {
            int width = GetWidth(kind);
            if (width == 64)
                return unchecked((long)raw);
            int shift = 64 - width;
            return unchecked((long)(raw << shift)) >> shift;
        }
    }

    public bool Equals(PackedBenchmarkValue other) => kind == other.kind && raw == other.raw;

    public override bool Equals(object obj) => obj is PackedBenchmarkValue other && Equals(other);

    public override int GetHashCode() => ((int)kind * 397) ^ raw.GetHashCode();

    public override string ToString() => IsSigned(kind) ? $"{kind}({SignedValue})" : $"{kind}({raw})";
}

/// <summary>
/// Isolated PackedX/Size benchmark adapters and candidate formats. This deliberately is not a
/// static class, preventing the serializer post-processor from registering benchmark methods.
/// </summary>
internal sealed class PackedIntegerBenchmarkCodecs
{
    private PackedIntegerBenchmarkCodecs() { }

    internal static bool WriteCurrentAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        WriteCurrentValue(packer, newValue);
        return true;
    }

    internal static void ReadCurrentAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        value = ReadCurrentValue(packer, oldValue.kind);
    }

    internal static bool WriteRawAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        packer.WriteBits(newValue.raw, checked((byte)PackedBenchmarkValue.GetWidth(newValue.kind)));
        return true;
    }

    internal static void ReadRawAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        value = new PackedBenchmarkValue(oldValue.kind, packer.ReadBits(checked((byte)width)));
    }

    internal static bool WriteCompactAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        WriteCompactPayload(packer, newValue.kind, newValue.raw);
        return true;
    }

    internal static void ReadCompactAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        value = new PackedBenchmarkValue(oldValue.kind, ReadCompactPayload(packer, oldValue.kind));
    }

    internal static bool WriteAdaptiveAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        int compactBits = CompactPayloadBits(newValue.kind, newValue.raw);
        bool raw = width <= compactBits;
        packer.WriteBit(raw);
        if (raw)
            packer.WriteBits(newValue.raw, checked((byte)width));
        else
            WriteCompactPayload(packer, newValue.kind, newValue.raw);
        return true;
    }

    internal static void ReadAdaptiveAbsolute(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        ulong raw = packer.ReadBit()
            ? packer.ReadBits(checked((byte)width))
            : ReadCompactPayload(packer, oldValue.kind);
        value = new PackedBenchmarkValue(oldValue.kind, raw);
    }

    internal static bool WriteCurrentDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        switch (newValue.kind)
        {
            case PackedBenchmarkKind.PackedByte:
                return DeltaPacker<PackedByte>.WriteFunc(packer, new PackedByte((byte)oldValue.raw),
                    new PackedByte((byte)newValue.raw));
            case PackedBenchmarkKind.PackedSByte:
                return DeltaPacker<PackedSByte>.WriteFunc(packer, new PackedSByte(unchecked((sbyte)oldValue.raw)),
                    new PackedSByte(unchecked((sbyte)newValue.raw)));
            case PackedBenchmarkKind.PackedUShort:
                return DeltaPacker<PackedUShort>.WriteFunc(packer, new PackedUShort((ushort)oldValue.raw),
                    new PackedUShort((ushort)newValue.raw));
            case PackedBenchmarkKind.PackedShort:
                return DeltaPacker<PackedShort>.WriteFunc(packer, new PackedShort(unchecked((short)oldValue.raw)),
                    new PackedShort(unchecked((short)newValue.raw)));
            case PackedBenchmarkKind.PackedUInt:
                return DeltaPacker<PackedUInt>.WriteFunc(packer, new PackedUInt((uint)oldValue.raw),
                    new PackedUInt((uint)newValue.raw));
            case PackedBenchmarkKind.PackedInt:
                return DeltaPacker<PackedInt>.WriteFunc(packer, new PackedInt(unchecked((int)oldValue.raw)),
                    new PackedInt(unchecked((int)newValue.raw)));
            case PackedBenchmarkKind.PackedULong:
                return DeltaPacker<PackedULong>.WriteFunc(packer, new PackedULong(oldValue.raw),
                    new PackedULong(newValue.raw));
            case PackedBenchmarkKind.PackedLong:
                return DeltaPacker<PackedLong>.WriteFunc(packer, new PackedLong(unchecked((long)oldValue.raw)),
                    new PackedLong(unchecked((long)newValue.raw)));
            case PackedBenchmarkKind.Size:
                return DeltaPacker<Size>.WriteFunc(packer, new Size((uint)oldValue.raw),
                    new Size((uint)newValue.raw));
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal static void ReadCurrentDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        switch (oldValue.kind)
        {
            case PackedBenchmarkKind.PackedByte:
            {
                var old = new PackedByte((byte)oldValue.raw);
                PackedByte result = default;
                DeltaPacker<PackedByte>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, result.value);
                break;
            }
            case PackedBenchmarkKind.PackedSByte:
            {
                var old = new PackedSByte(unchecked((sbyte)oldValue.raw));
                PackedSByte result = default;
                DeltaPacker<PackedSByte>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, unchecked((byte)result.value));
                break;
            }
            case PackedBenchmarkKind.PackedUShort:
            {
                var old = new PackedUShort((ushort)oldValue.raw);
                PackedUShort result = default;
                DeltaPacker<PackedUShort>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, result.value);
                break;
            }
            case PackedBenchmarkKind.PackedShort:
            {
                var old = new PackedShort(unchecked((short)oldValue.raw));
                PackedShort result = default;
                DeltaPacker<PackedShort>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, unchecked((ushort)result.value));
                break;
            }
            case PackedBenchmarkKind.PackedUInt:
            {
                var old = new PackedUInt((uint)oldValue.raw);
                PackedUInt result = default;
                DeltaPacker<PackedUInt>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, result.value);
                break;
            }
            case PackedBenchmarkKind.PackedInt:
            {
                var old = new PackedInt(unchecked((int)oldValue.raw));
                PackedInt result = default;
                DeltaPacker<PackedInt>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, unchecked((uint)result.value));
                break;
            }
            case PackedBenchmarkKind.PackedULong:
            {
                var old = new PackedULong(oldValue.raw);
                PackedULong result = default;
                DeltaPacker<PackedULong>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, result.value);
                break;
            }
            case PackedBenchmarkKind.PackedLong:
            {
                var old = new PackedLong(unchecked((long)oldValue.raw));
                PackedLong result = default;
                DeltaPacker<PackedLong>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, unchecked((ulong)result.value));
                break;
            }
            case PackedBenchmarkKind.Size:
            {
                var old = new Size((uint)oldValue.raw);
                Size result = default;
                DeltaPacker<Size>.ReadFunc(packer, old, ref result);
                value = new PackedBenchmarkValue(oldValue.kind, result.value);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    internal static bool WriteRawOnChange(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        bool changed = oldValue.raw != newValue.raw;
        packer.WriteBit(changed);
        if (changed)
            packer.WriteBits(newValue.raw,
                checked((byte)PackedBenchmarkValue.GetWidth(newValue.kind)));
        return changed;
    }

    internal static void ReadRawOnChange(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        value = new PackedBenchmarkValue(oldValue.kind, packer.ReadBits(checked((byte)width)));
    }

    internal static bool WriteCompactModularDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        bool changed = oldValue.raw != newValue.raw;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        ulong mask = PackedBenchmarkValue.Mask(newValue.kind);
        ulong deltaRaw = unchecked(newValue.raw - oldValue.raw) & mask;
        ulong encoded = NonZeroZigzagEncodeRaw(deltaRaw, width);
        WriteShiftedTier(packer, encoded, width, 7);
        return true;
    }

    internal static void ReadCompactModularDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        ulong mask = PackedBenchmarkValue.Mask(oldValue.kind);
        ulong encoded = ReadShiftedTier(packer, width, 7);
        ulong deltaRaw = NonZeroZigzagDecodeRaw(encoded, width);
        value = new PackedBenchmarkValue(oldValue.kind, unchecked(oldValue.raw + deltaRaw) & mask);
    }

    internal static bool WriteCompactForwardDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        bool changed = oldValue.raw != newValue.raw;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        ulong delta = unchecked(newValue.raw - oldValue.raw) & PackedBenchmarkValue.Mask(newValue.kind);
        WriteShiftedTier(packer, delta - 1UL, width, 7);
        return true;
    }

    internal static void ReadCompactForwardDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        ulong encoded = ReadShiftedTier(packer, width, 7);
        ulong mask = PackedBenchmarkValue.Mask(oldValue.kind);
        if (encoded == mask)
            throw new InvalidOperationException("A forward packed delta uses the reserved terminal code.");
        ulong delta = encoded + 1UL;
        value = new PackedBenchmarkValue(oldValue.kind, unchecked(oldValue.raw + delta));
    }

    internal static bool WriteAdaptiveModularDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        bool changed = oldValue.raw != newValue.raw;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        ulong mask = PackedBenchmarkValue.Mask(newValue.kind);
        ulong deltaRaw = unchecked(newValue.raw - oldValue.raw) & mask;
        ulong encoded = NonZeroZigzagEncodeRaw(deltaRaw, width);
        int compactBits = ShiftedTierBits(encoded, width, 7);
        bool raw = width <= compactBits;
        packer.WriteBit(raw);
        if (raw)
            packer.WriteBits(newValue.raw, checked((byte)width));
        else
            WriteShiftedTier(packer, encoded, width, 7);
        return true;
    }

    internal static void ReadAdaptiveModularDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        if (packer.ReadBit())
        {
            value = new PackedBenchmarkValue(oldValue.kind, packer.ReadBits(checked((byte)width)));
            return;
        }

        ulong deltaRaw = NonZeroZigzagDecodeRaw(ReadShiftedTier(packer, width, 7), width);
        value = new PackedBenchmarkValue(oldValue.kind, unchecked(oldValue.raw + deltaRaw));
    }

    internal static bool WriteCompactTwoBitDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        bool changed = oldValue.raw != newValue.raw;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        ulong mask = PackedBenchmarkValue.Mask(newValue.kind);
        ulong deltaRaw = unchecked(newValue.raw - oldValue.raw) & mask;
        ulong encoded = NonZeroZigzagEncodeRaw(deltaRaw, width);
        WriteShiftedTier(packer, encoded, width, 2);
        return true;
    }

    internal static void ReadCompactTwoBitDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        ulong mask = PackedBenchmarkValue.Mask(oldValue.kind);
        ulong encoded = ReadShiftedTier(packer, width, 2);
        ulong deltaRaw = NonZeroZigzagDecodeRaw(encoded, width);
        value = new PackedBenchmarkValue(oldValue.kind, unchecked(oldValue.raw + deltaRaw) & mask);
    }

    internal static bool WriteAdaptiveTwoBitDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        bool changed = oldValue.raw != newValue.raw;
        packer.WriteBit(changed);
        if (!changed)
            return false;

        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        ulong mask = PackedBenchmarkValue.Mask(newValue.kind);
        ulong deltaRaw = unchecked(newValue.raw - oldValue.raw) & mask;
        ulong encoded = NonZeroZigzagEncodeRaw(deltaRaw, width);
        bool raw = width <= ShiftedTierBits(encoded, width, 2);
        packer.WriteBit(raw);
        if (raw)
            packer.WriteBits(newValue.raw, checked((byte)width));
        else
            WriteShiftedTier(packer, encoded, width, 2);
        return true;
    }

    internal static void ReadAdaptiveTwoBitDelta(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue;
            return;
        }

        int width = PackedBenchmarkValue.GetWidth(oldValue.kind);
        if (packer.ReadBit())
        {
            value = new PackedBenchmarkValue(oldValue.kind, packer.ReadBits(checked((byte)width)));
            return;
        }

        ulong deltaRaw = NonZeroZigzagDecodeRaw(ReadShiftedTier(packer, width, 2), width);
        value = new PackedBenchmarkValue(oldValue.kind, unchecked(oldValue.raw + deltaRaw));
    }

    internal static int CompactAbsoluteBits(PackedBenchmarkValue value) =>
        CompactPayloadBits(value.kind, value.raw);

    internal static int CompactModularDeltaBits(PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue)
    {
        EnsureSameKind(oldValue, newValue);
        if (oldValue.raw == newValue.raw)
            return 1;
        int width = PackedBenchmarkValue.GetWidth(newValue.kind);
        ulong deltaRaw = unchecked(newValue.raw - oldValue.raw) & PackedBenchmarkValue.Mask(newValue.kind);
        return 1 + ShiftedTierBits(NonZeroZigzagEncodeRaw(deltaRaw, width), width, 7);
    }

    private static void WriteCurrentValue(BitPacker packer, PackedBenchmarkValue value)
    {
        switch (value.kind)
        {
            case PackedBenchmarkKind.PackedByte:
                Packer<PackedByte>.WriteFunc(packer, new PackedByte((byte)value.raw));
                break;
            case PackedBenchmarkKind.PackedSByte:
                Packer<PackedSByte>.WriteFunc(packer, new PackedSByte(unchecked((sbyte)value.raw)));
                break;
            case PackedBenchmarkKind.PackedUShort:
                Packer<PackedUShort>.WriteFunc(packer, new PackedUShort((ushort)value.raw));
                break;
            case PackedBenchmarkKind.PackedShort:
                Packer<PackedShort>.WriteFunc(packer, new PackedShort(unchecked((short)value.raw)));
                break;
            case PackedBenchmarkKind.PackedUInt:
                Packer<PackedUInt>.WriteFunc(packer, new PackedUInt((uint)value.raw));
                break;
            case PackedBenchmarkKind.PackedInt:
                Packer<PackedInt>.WriteFunc(packer, new PackedInt(unchecked((int)value.raw)));
                break;
            case PackedBenchmarkKind.PackedULong:
                Packer<PackedULong>.WriteFunc(packer, new PackedULong(value.raw));
                break;
            case PackedBenchmarkKind.PackedLong:
                Packer<PackedLong>.WriteFunc(packer, new PackedLong(unchecked((long)value.raw)));
                break;
            case PackedBenchmarkKind.Size:
                Packer<Size>.WriteFunc(packer, new Size((uint)value.raw));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static PackedBenchmarkValue ReadCurrentValue(BitPacker packer, PackedBenchmarkKind kind)
    {
        switch (kind)
        {
            case PackedBenchmarkKind.PackedByte:
            {
                PackedByte value = default;
                Packer<PackedByte>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, value.value);
            }
            case PackedBenchmarkKind.PackedSByte:
            {
                PackedSByte value = default;
                Packer<PackedSByte>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, unchecked((byte)value.value));
            }
            case PackedBenchmarkKind.PackedUShort:
            {
                PackedUShort value = default;
                Packer<PackedUShort>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, value.value);
            }
            case PackedBenchmarkKind.PackedShort:
            {
                PackedShort value = default;
                Packer<PackedShort>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, unchecked((ushort)value.value));
            }
            case PackedBenchmarkKind.PackedUInt:
            {
                PackedUInt value = default;
                Packer<PackedUInt>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, value.value);
            }
            case PackedBenchmarkKind.PackedInt:
            {
                PackedInt value = default;
                Packer<PackedInt>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, unchecked((uint)value.value));
            }
            case PackedBenchmarkKind.PackedULong:
            {
                PackedULong value = default;
                Packer<PackedULong>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, value.value);
            }
            case PackedBenchmarkKind.PackedLong:
            {
                PackedLong value = default;
                Packer<PackedLong>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, unchecked((ulong)value.value));
            }
            case PackedBenchmarkKind.Size:
            {
                Size value = default;
                Packer<Size>.ReadFunc(packer, ref value);
                return new PackedBenchmarkValue(kind, value.value);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static void WriteCompactPayload(BitPacker packer, PackedBenchmarkKind kind, ulong raw)
    {
        int width = PackedBenchmarkValue.GetWidth(kind);
        if (kind == PackedBenchmarkKind.PackedByte || kind == PackedBenchmarkKind.PackedSByte)
        {
            packer.WriteBits(raw, 8);
            return;
        }

        ulong encoded = PackedBenchmarkValue.IsSigned(kind) ? ZigzagEncodeRaw(raw, width) : raw;
        WriteShiftedTier(packer, encoded, width, kind == PackedBenchmarkKind.Size ? 2 : 7);
    }

    private static ulong ReadCompactPayload(BitPacker packer, PackedBenchmarkKind kind)
    {
        int width = PackedBenchmarkValue.GetWidth(kind);
        if (kind == PackedBenchmarkKind.PackedByte || kind == PackedBenchmarkKind.PackedSByte)
            return packer.ReadBits(8);

        ulong encoded = ReadShiftedTier(packer, width, kind == PackedBenchmarkKind.Size ? 2 : 7);
        return PackedBenchmarkValue.IsSigned(kind) ? ZigzagDecodeRaw(encoded, width) : encoded;
    }

    private static int CompactPayloadBits(PackedBenchmarkKind kind, ulong raw)
    {
        int width = PackedBenchmarkValue.GetWidth(kind);
        if (kind == PackedBenchmarkKind.PackedByte || kind == PackedBenchmarkKind.PackedSByte)
            return 8;
        ulong encoded = PackedBenchmarkValue.IsSigned(kind) ? ZigzagEncodeRaw(raw, width) : raw;
        return ShiftedTierBits(encoded, width, kind == PackedBenchmarkKind.Size ? 2 : 7);
    }

    private static void WriteShiftedTier(BitPacker packer, ulong value, int width, int chunkBits)
    {
        int continuedChunks = (width - 1) / chunkBits;
        ulong chunkMask = (1UL << chunkBits) - 1UL;
        ulong offset = 0;
        ulong groupSize = 1UL << chunkBits;

        for (int tier = 1; tier <= continuedChunks; tier++)
        {
            ulong threshold = offset + groupSize;
            if (value < threshold)
            {
                ulong payload = value - offset;
                for (int chunk = 0; chunk < tier; chunk++)
                {
                    packer.WriteBits(payload & chunkMask, checked((byte)chunkBits));
                    payload >>= chunkBits;
                    packer.WriteBit(chunk + 1 < tier);
                }
                return;
            }

            offset = threshold;
            if (tier < continuedChunks)
                groupSize <<= chunkBits;
        }

        ulong terminalPayload = value - offset;
        for (int chunk = 0; chunk < continuedChunks; chunk++)
        {
            packer.WriteBits(terminalPayload & chunkMask, checked((byte)chunkBits));
            terminalPayload >>= chunkBits;
            packer.WriteBit(true);
        }

        int terminalBits = GetTerminalBits(width, continuedChunks, chunkBits, offset);
        if (terminalBits > 0)
            packer.WriteBits(terminalPayload, checked((byte)terminalBits));
    }

    private static ulong ReadShiftedTier(BitPacker packer, int width, int chunkBits)
    {
        int continuedChunks = (width - 1) / chunkBits;
        ulong offset = 0;
        ulong groupSize = 1UL << chunkBits;
        ulong payload = 0;
        int shift = 0;

        for (int tier = 1; tier <= continuedChunks; tier++)
        {
            payload |= packer.ReadBits(checked((byte)chunkBits)) << shift;
            shift += chunkBits;
            if (!packer.ReadBit())
                return offset + payload;

            offset += groupSize;
            if (tier < continuedChunks)
                groupSize <<= chunkBits;
        }

        ulong maxValue = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        int terminalBits = GetTerminalBits(width, continuedChunks, chunkBits, offset);
        if (terminalBits > 0)
            payload |= packer.ReadBits(checked((byte)terminalBits)) << shift;
        if (payload > maxValue - offset)
            throw new InvalidOperationException("A shifted-tier packed integer exceeds its declared width.");
        return offset + payload;
    }

    private static int ShiftedTierBits(ulong value, int width, int chunkBits)
    {
        int continuedChunks = (width - 1) / chunkBits;
        ulong offset = 0;
        ulong groupSize = 1UL << chunkBits;

        for (int tier = 1; tier <= continuedChunks; tier++)
        {
            ulong threshold = offset + groupSize;
            if (value < threshold)
                return tier * (chunkBits + 1);
            offset = threshold;
            if (tier < continuedChunks)
                groupSize <<= chunkBits;
        }

        int terminalBits = GetTerminalBits(width, continuedChunks, chunkBits, offset);
        return continuedChunks * (chunkBits + 1) + terminalBits;
    }

    private static int GetTerminalBits(int width, int continuedChunks, int chunkBits, ulong offset)
    {
        ulong maxValue = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        ulong maxResidual = maxValue - offset;
        int residualBits = maxResidual == 0 ? 0 : 64 - CountLeadingZeroBits(maxResidual);
        return Math.Max(0, residualBits - continuedChunks * chunkBits);
    }

    private static int CountLeadingZeroBits(ulong value)
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

    private static ulong ZigzagEncodeRaw(ulong raw, int width)
    {
        ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        ulong sign = raw >> (width - 1);
        return ((raw << 1) ^ (0UL - sign)) & mask;
    }

    private static ulong ZigzagDecodeRaw(ulong encoded, int width)
    {
        ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        return ((encoded >> 1) ^ (0UL - (encoded & 1UL))) & mask;
    }

    private static ulong NonZeroZigzagEncodeRaw(ulong raw, int width)
    {
        ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        ulong signBit = 1UL << (width - 1);
        if (raw == signBit)
            return mask - 1UL;

        ulong signedZigzag = ZigzagEncodeRaw(raw, width);
        return (raw & signBit) == 0 ? signedZigzag - 2UL : signedZigzag;
    }

    private static ulong NonZeroZigzagDecodeRaw(ulong encoded, int width)
    {
        ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        ulong signBit = 1UL << (width - 1);
        if (encoded == mask)
            throw new InvalidOperationException("A non-zero Zigzag delta uses the reserved terminal code.");
        if (encoded == mask - 1UL)
            return signBit;

        ulong zigzag = (encoded & 1UL) == 0 ? encoded + 2UL : encoded;
        return ZigzagDecodeRaw(zigzag, width);
    }

    private static void EnsureSameKind(PackedBenchmarkValue oldValue, PackedBenchmarkValue newValue)
    {
        if (oldValue.kind != newValue.kind)
            throw new ArgumentException("Packed benchmark pairs must use the same kind.");
    }
}
