using System;
using System.Collections.Generic;

/// <summary>
/// Deterministic corpora shared by the absolute and delta packed-integer benchmarks.
/// Raw values are always the width-limited, two's-complement bit pattern of the
/// represented value. This keeps every edge case (including signed wraparound)
/// available without converting through a wider numeric type.
/// </summary>
internal static class PackedIntegerBenchmarkCorpora
{
    private const int PairCount = 256;
    private const int TargetOperations = 50_000;

    internal static DeltaBenchmarkScenario<PackedBenchmarkValue>[] BuildAbsoluteScenarios(
        PackedBenchmarkKind kind)
    {
        KindInfo info = Describe(kind);

        return new[]
        {
            Scenario("tiny", ExpandAbsolute(info, BuildTinyAnchors(info), Seed(info, 0x11u))),
            Scenario("typical", ExpandAbsolute(info, BuildTypicalAnchors(info), Seed(info, 0x22u))),
            Scenario("tier-boundary", ExpandAbsolute(info, BuildTierBoundaryAnchors(info), Seed(info, 0x33u))),
            Scenario("random", BuildRandomAbsolutePairs(info, Seed(info, 0x44u))),
            Scenario("extreme", ExpandAbsolute(info, BuildExtremeAnchors(info), Seed(info, 0x55u)))
        };
    }

    internal static DeltaBenchmarkScenario<PackedBenchmarkValue>[] BuildDeltaScenarios(
        PackedBenchmarkKind kind)
    {
        KindInfo info = Describe(kind);

        return new[]
        {
            Scenario("unchanged", BuildUnchangedPairs(info, Seed(info, 0x61u))),
            Scenario("increment", BuildIncrementPairs(info, Seed(info, 0x62u))),
            Scenario("decrement", BuildDecrementPairs(info, Seed(info, 0x63u))),
            Scenario("wrap", ExpandDelta(info, BuildWrapAnchors(info), Seed(info, 0x64u))),
            Scenario("mixed", ExpandDelta(info, BuildMixedAnchors(info), Seed(info, 0x65u))),
            Scenario("random", BuildRandomDeltaPairs(info, Seed(info, 0x66u)))
        };
    }

    private static DeltaBenchmarkScenario<PackedBenchmarkValue> Scenario(
        string name,
        DeltaBenchmarkPair<PackedBenchmarkValue>[] pairs)
    {
        return new DeltaBenchmarkScenario<PackedBenchmarkValue>(name, pairs, TargetOperations);
    }

    private static List<ulong> BuildTinyAnchors(KindInfo info)
    {
        var result = new List<ulong>();
        if (info.signed)
        {
            long[] values =
            {
                0, 1, -1, 2, -2, 3, -3, 4, -4, 7, -7, 8, -8,
                15, -15, 16, -16, 31, -31, 32, -32, 63, -63
            };

            for (int i = 0; i < values.Length; i++)
                AddSignedAnchor(result, info, values[i]);
        }
        else
        {
            ulong[] values =
            {
                0, 1, 2, 3, 4, 5, 7, 8, 15, 16, 31, 32, 63, 64, 100, 127
            };

            for (int i = 0; i < values.Length; i++)
                AddUnsignedAnchor(result, info, values[i]);
        }

        return result;
    }

    private static List<ulong> BuildTypicalAnchors(KindInfo info)
    {
        var result = new List<ulong>();
        if (info.signed)
        {
            long[] values =
            {
                0, 1, -1, 10, -10, 30, -30, 60, -60, 100, -100,
                1_000, -1_000, 10_000, -10_000, 65_535, -65_535,
                1L << 20, -(1L << 20), 1L << 30, -(1L << 30),
                1L << 40, -(1L << 40), 1L << 55, -(1L << 55)
            };

            for (int i = 0; i < values.Length; i++)
                AddSignedAnchor(result, info, values[i]);

            AddSignedAnchor(result, info, info.signedMin);
            AddSignedAnchor(result, info, info.signedMax);
        }
        else
        {
            ulong[] values =
            {
                0, 1, 2, 3, 4, 8, 15, 16, 31, 32, 63, 64,
                100, 127, 128, 255, 256, 512, 1_000, 1_024, 4_096,
                16_383, 16_384, 65_535, 1_000_000, 1UL << 20,
                1UL << 24, 1UL << 30, 1UL << 32, 1UL << 48
            };

            for (int i = 0; i < values.Length; i++)
                AddUnsignedAnchor(result, info, values[i]);

            AddUnsignedAnchor(result, info, info.mask >> 1);
            AddUnsignedAnchor(result, info, info.mask);
        }

        return result;
    }

    private static List<ulong> BuildTierBoundaryAnchors(KindInfo info)
    {
        var result = new List<ulong>();
        AddEncodedAnchor(result, info, 0);
        AddEncodedAnchor(result, info, 1);

        int chunkBits = info.isSize ? 2 : 7;
        ulong shiftedBoundary = 0;

        // Cover both today's power-of-base thresholds and the cumulative
        // thresholds used by the shifted-tier candidate format.
        for (int shift = chunkBits; shift < info.bits; shift += chunkBits)
        {
            ulong currentBoundary = 1UL << shift;
            AddAroundEncodedBoundary(result, info, currentBoundary);

            shiftedBoundary += currentBoundary;
            AddAroundEncodedBoundary(result, info, shiftedBoundary);
        }

        AddEncodedAnchor(result, info, info.mask - 1);
        AddEncodedAnchor(result, info, info.mask);
        return result;
    }

    private static List<ulong> BuildExtremeAnchors(KindInfo info)
    {
        var result = new List<ulong>();
        if (info.signed)
        {
            AddSignedAnchor(result, info, info.signedMin);
            AddSignedAnchor(result, info, info.signedMin + 1);
            AddSignedAnchor(result, info, info.signedMin + 2);
            AddSignedAnchor(result, info, -1);
            AddSignedAnchor(result, info, 0);
            AddSignedAnchor(result, info, 1);
            AddSignedAnchor(result, info, info.signedMax - 2);
            AddSignedAnchor(result, info, info.signedMax - 1);
            AddSignedAnchor(result, info, info.signedMax);
        }
        else
        {
            ulong highBit = 1UL << (info.bits - 1);
            AddUnsignedAnchor(result, info, 0);
            AddUnsignedAnchor(result, info, 1);
            AddUnsignedAnchor(result, info, highBit - 1);
            AddUnsignedAnchor(result, info, highBit);
            AddUnsignedAnchor(result, info, highBit + 1);
            AddUnsignedAnchor(result, info, info.mask - 2);
            AddUnsignedAnchor(result, info, info.mask - 1);
            AddUnsignedAnchor(result, info, info.mask);
        }

        AddRawAnchor(result, info, 0xAAAAAAAAAAAAAAAAUL);
        AddRawAnchor(result, info, 0x5555555555555555UL);
        AddRawAnchor(result, info, 0x8000000000000001UL);
        AddRawAnchor(result, info, 0x7FFFFFFFFFFFFFFEUL);
        return result;
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] BuildRandomAbsolutePairs(
        KindInfo info,
        uint seed)
    {
        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        var rng = new DeterministicRandom(seed);
        PackedBenchmarkValue zero = Value(info, 0);

        for (int i = 0; i < result.Length; i++)
            result[i] = new DeltaBenchmarkPair<PackedBenchmarkValue>(zero, Value(info, rng.NextULong()));

        return result;
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] BuildUnchangedPairs(
        KindInfo info,
        uint seed)
    {
        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        var rng = new DeterministicRandom(seed);

        for (int i = 0; i < result.Length; i++)
        {
            PackedBenchmarkValue value = Value(info, rng.NextULong());
            result[i] = new DeltaBenchmarkPair<PackedBenchmarkValue>(value, value);
        }

        return result;
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] BuildIncrementPairs(
        KindInfo info,
        uint seed)
    {
        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        var rng = new DeterministicRandom(seed);
        ulong[] steps = { 1, 1, 1, 2, 3, 7, 15, 63 };

        for (int i = 0; i < result.Length; i++)
        {
            ulong step = steps[i % steps.Length];
            ulong oldRaw;
            ulong newRaw;

            if (info.signed)
            {
                long oldValue = SignExtend(rng.NextULong(), info);
                long signedStep = (long)step;
                if (oldValue > info.signedMax - signedStep)
                    oldValue = info.signedMax - signedStep;

                oldRaw = ToRaw(oldValue, info);
                newRaw = ToRaw(oldValue + signedStep, info);
            }
            else
            {
                oldRaw = rng.NextULong() & info.mask;
                if (oldRaw > info.mask - step)
                    oldRaw = info.mask - step;

                newRaw = oldRaw + step;
            }

            result[i] = Pair(info, oldRaw, newRaw);
        }

        return result;
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] BuildDecrementPairs(
        KindInfo info,
        uint seed)
    {
        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        var rng = new DeterministicRandom(seed);
        ulong[] steps = { 1, 1, 1, 2, 3, 7, 15, 63 };

        for (int i = 0; i < result.Length; i++)
        {
            ulong step = steps[i % steps.Length];
            ulong oldRaw;
            ulong newRaw;

            if (info.signed)
            {
                long oldValue = SignExtend(rng.NextULong(), info);
                long signedStep = (long)step;
                if (oldValue < info.signedMin + signedStep)
                    oldValue = info.signedMin + signedStep;

                oldRaw = ToRaw(oldValue, info);
                newRaw = ToRaw(oldValue - signedStep, info);
            }
            else
            {
                oldRaw = rng.NextULong() & info.mask;
                if (oldRaw < step)
                    oldRaw = step;

                newRaw = oldRaw - step;
            }

            result[i] = Pair(info, oldRaw, newRaw);
        }

        return result;
    }

    private static List<RawPair> BuildWrapAnchors(KindInfo info)
    {
        var result = new List<RawPair>();
        if (info.signed)
        {
            ulong min = ToRaw(info.signedMin, info);
            ulong max = ToRaw(info.signedMax, info);
            AddPair(result, info, max, min);
            AddPair(result, info, min, max);
            AddPair(result, info, max - 1, min);
            AddPair(result, info, min + 1, max);
            AddPair(result, info, max, min + 1);
            AddPair(result, info, min, max - 1);
            AddPair(result, info, info.mask, 0);
            AddPair(result, info, 0, info.mask);
        }
        else
        {
            AddPair(result, info, info.mask, 0);
            AddPair(result, info, 0, info.mask);
            AddPair(result, info, info.mask, 1);
            AddPair(result, info, 1, info.mask);
            AddPair(result, info, info.mask - 1, 0);
            AddPair(result, info, 0, info.mask - 1);
            AddPair(result, info, info.mask - 1, 1);
            AddPair(result, info, 1, info.mask - 1);
        }

        return result;
    }

    private static List<RawPair> BuildMixedAnchors(KindInfo info)
    {
        var result = BuildWrapAnchors(info);

        AddPair(result, info, 0, 0);
        AddPair(result, info, 0, 1);
        AddPair(result, info, 1, 0);
        AddPair(result, info, 1, 2);
        AddPair(result, info, 2, 1);
        AddPair(result, info, info.mask, info.mask);
        AddPair(result, info, info.mask, 0);
        AddPair(result, info, 0, info.mask);

        if (info.signed)
        {
            AddSignedPair(result, info, -1, 0);
            AddSignedPair(result, info, 0, -1);
            AddSignedPair(result, info, -64, 63);
            AddSignedPair(result, info, 63, -64);
            AddSignedPair(result, info, info.signedMin, 0);
            AddSignedPair(result, info, 0, info.signedMin);
            AddSignedPair(result, info, info.signedMax, 0);
            AddSignedPair(result, info, 0, info.signedMax);
        }
        else
        {
            ulong highBit = 1UL << (info.bits - 1);
            AddPair(result, info, highBit - 1, highBit);
            AddPair(result, info, highBit, highBit - 1);
            AddPair(result, info, info.mask, info.mask - 1);
            AddPair(result, info, info.mask - 1, info.mask);
        }

        AddDeltaTierAnchors(result, info);
        AddAbsoluteTransitionAnchors(result, info);
        return result;
    }

    private static void AddDeltaTierAnchors(List<RawPair> result, KindInfo info)
    {
        // Deltas are symmetric signed codes, even for unsigned values and Size.
        // Exercise both the current powers-of-128 and shifted-tier thresholds.
        ulong shiftedBoundary = 0;
        ulong oldRaw = (0x9E3779B97F4A7C15UL ^ info.mask) & info.mask;

        for (int shift = 7; shift < info.bits; shift += 7)
        {
            ulong currentBoundary = 1UL << shift;
            AddAroundDeltaBoundary(result, info, oldRaw, currentBoundary);

            shiftedBoundary += currentBoundary;
            AddAroundDeltaBoundary(result, info, oldRaw, shiftedBoundary);
        }
    }

    private static void AddAroundDeltaBoundary(
        List<RawPair> result,
        KindInfo info,
        ulong oldRaw,
        ulong boundary)
    {
        for (long offset = -1; offset <= 1; offset++)
        {
            ulong encoded;
            if (!TryOffset(boundary, offset, info.mask, out encoded))
                continue;

            long delta = ZigzagDecode(encoded);
            ulong deltaRaw = unchecked((ulong)delta) & info.mask;
            ulong newRaw = unchecked(oldRaw + deltaRaw) & info.mask;
            AddPair(result, info, oldRaw, newRaw);

            ulong reverseRaw = unchecked(oldRaw - deltaRaw) & info.mask;
            AddPair(result, info, oldRaw, reverseRaw);
        }
    }

    private static void AddAbsoluteTransitionAnchors(List<RawPair> result, KindInfo info)
    {
        int chunkBits = info.isSize ? 2 : 7;
        ulong shiftedBoundary = 0;

        for (int shift = chunkBits; shift < info.bits; shift += chunkBits)
        {
            ulong currentBoundary = 1UL << shift;
            AddEncodedTransition(result, info, currentBoundary);

            shiftedBoundary += currentBoundary;
            AddEncodedTransition(result, info, shiftedBoundary);
        }
    }

    private static void AddEncodedTransition(List<RawPair> result, KindInfo info, ulong boundary)
    {
        if (boundary == 0 || boundary > info.mask)
            return;

        ulong before = DecodeEncodedRaw(info, boundary - 1);
        ulong at = DecodeEncodedRaw(info, boundary);
        AddPair(result, info, before, at);
        AddPair(result, info, at, before);
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] BuildRandomDeltaPairs(
        KindInfo info,
        uint seed)
    {
        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        var rng = new DeterministicRandom(seed);

        for (int i = 0; i < result.Length; i++)
            result[i] = Pair(info, rng.NextULong(), rng.NextULong());

        return result;
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] ExpandAbsolute(
        KindInfo info,
        List<ulong> anchors,
        uint seed)
    {
        if (anchors.Count == 0)
            throw new InvalidOperationException("Packed benchmark corpus has no absolute anchors.");

        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        PackedBenchmarkValue zero = Value(info, 0);
        for (int i = 0; i < result.Length; i++)
        {
            ulong raw = anchors[i % anchors.Count];
            result[i] = new DeltaBenchmarkPair<PackedBenchmarkValue>(zero, Value(info, raw));
        }

        Shuffle(result, seed);
        return result;
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue>[] ExpandDelta(
        KindInfo info,
        List<RawPair> anchors,
        uint seed)
    {
        if (anchors.Count == 0)
            throw new InvalidOperationException("Packed benchmark corpus has no delta anchors.");

        var result = new DeltaBenchmarkPair<PackedBenchmarkValue>[PairCount];
        for (int i = 0; i < result.Length; i++)
        {
            RawPair anchor = anchors[i % anchors.Count];
            result[i] = Pair(info, anchor.oldRaw, anchor.newRaw);
        }

        Shuffle(result, seed);
        return result;
    }

    private static void Shuffle(DeltaBenchmarkPair<PackedBenchmarkValue>[] values, uint seed)
    {
        var rng = new DeterministicRandom(seed);
        for (int i = values.Length - 1; i > 0; i--)
        {
            int other = (int)(rng.NextUInt() % (uint)(i + 1));
            DeltaBenchmarkPair<PackedBenchmarkValue> temporary = values[i];
            values[i] = values[other];
            values[other] = temporary;
        }
    }

    private static void AddAroundEncodedBoundary(List<ulong> result, KindInfo info, ulong boundary)
    {
        for (long offset = -2; offset <= 1; offset++)
        {
            ulong encoded;
            if (TryOffset(boundary, offset, info.mask, out encoded))
                AddEncodedAnchor(result, info, encoded);
        }
    }

    private static bool TryOffset(ulong value, long offset, ulong maximum, out ulong result)
    {
        if (offset < 0)
        {
            ulong distance = (ulong)(-offset);
            if (value < distance)
            {
                result = 0;
                return false;
            }

            result = value - distance;
            return result <= maximum;
        }

        ulong addition = (ulong)offset;
        if (value > maximum || addition > maximum - value)
        {
            result = 0;
            return false;
        }

        result = value + addition;
        return true;
    }

    private static void AddEncodedAnchor(List<ulong> result, KindInfo info, ulong encoded)
    {
        if (encoded > info.mask)
            return;

        AddUnique(result, DecodeEncodedRaw(info, encoded));
    }

    private static ulong DecodeEncodedRaw(KindInfo info, ulong encoded)
    {
        if (!info.signed)
            return encoded & info.mask;

        return ToRaw(ZigzagDecode(encoded), info);
    }

    private static void AddSignedAnchor(List<ulong> result, KindInfo info, long value)
    {
        if (value < info.signedMin || value > info.signedMax)
            return;

        AddUnique(result, ToRaw(value, info));
    }

    private static void AddUnsignedAnchor(List<ulong> result, KindInfo info, ulong value)
    {
        if (value <= info.mask)
            AddUnique(result, value);
    }

    private static void AddRawAnchor(List<ulong> result, KindInfo info, ulong raw)
    {
        AddUnique(result, raw & info.mask);
    }

    private static void AddUnique(List<ulong> values, ulong value)
    {
        if (!values.Contains(value))
            values.Add(value);
    }

    private static void AddSignedPair(
        List<RawPair> values,
        KindInfo info,
        long oldValue,
        long newValue)
    {
        if (oldValue < info.signedMin || oldValue > info.signedMax ||
            newValue < info.signedMin || newValue > info.signedMax)
            return;

        AddPair(values, info, ToRaw(oldValue, info), ToRaw(newValue, info));
    }

    private static void AddPair(List<RawPair> values, KindInfo info, ulong oldRaw, ulong newRaw)
    {
        var pair = new RawPair(oldRaw & info.mask, newRaw & info.mask);
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i].oldRaw == pair.oldRaw && values[i].newRaw == pair.newRaw)
                return;
        }

        values.Add(pair);
    }

    private static DeltaBenchmarkPair<PackedBenchmarkValue> Pair(
        KindInfo info,
        ulong oldRaw,
        ulong newRaw)
    {
        return new DeltaBenchmarkPair<PackedBenchmarkValue>(Value(info, oldRaw), Value(info, newRaw));
    }

    private static PackedBenchmarkValue Value(KindInfo info, ulong raw)
    {
        return new PackedBenchmarkValue(info.kind, raw & info.mask);
    }

    private static ulong ToRaw(long value, KindInfo info)
    {
        return unchecked((ulong)value) & info.mask;
    }

    private static long SignExtend(ulong raw, KindInfo info)
    {
        raw &= info.mask;
        if (info.bits == 64)
            return unchecked((long)raw);

        ulong signBit = 1UL << (info.bits - 1);
        if ((raw & signBit) == 0)
            return (long)raw;

        return unchecked((long)(raw | ~info.mask));
    }

    private static long ZigzagDecode(ulong value)
    {
        return unchecked((long)((value >> 1) ^ (ulong)-(long)(value & 1UL)));
    }

    private static uint Seed(KindInfo info, uint scenario)
    {
        uint hash = 2_166_136_261u;
        hash ^= unchecked((uint)Convert.ToUInt64(info.kind));
        hash *= 16_777_619u;
        hash ^= scenario * 0x9E3779B9u;
        return hash == 0 ? 0x6D2B79F5u : hash;
    }

    private static KindInfo Describe(PackedBenchmarkKind kind)
    {
        switch (kind)
        {
            case PackedBenchmarkKind.PackedByte:
            case PackedBenchmarkKind.PackedSByte:
            case PackedBenchmarkKind.PackedUShort:
            case PackedBenchmarkKind.PackedShort:
            case PackedBenchmarkKind.PackedUInt:
            case PackedBenchmarkKind.PackedInt:
            case PackedBenchmarkKind.PackedULong:
            case PackedBenchmarkKind.PackedLong:
            case PackedBenchmarkKind.Size:
                return new KindInfo(kind, kind == PackedBenchmarkKind.Size);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    "No packed-integer benchmark corpus descriptor exists for this kind.");
        }
    }

    private readonly struct RawPair
    {
        public readonly ulong oldRaw;
        public readonly ulong newRaw;

        public RawPair(ulong oldRaw, ulong newRaw)
        {
            this.oldRaw = oldRaw;
            this.newRaw = newRaw;
        }
    }

    private readonly struct KindInfo
    {
        public readonly PackedBenchmarkKind kind;
        public readonly int bits;
        public readonly bool signed;
        public readonly bool isSize;
        public readonly ulong mask;
        public readonly long signedMin;
        public readonly long signedMax;

        public KindInfo(PackedBenchmarkKind kind, bool isSize)
        {
            this.kind = kind;
            bits = PackedBenchmarkValue.GetWidth(kind);
            signed = PackedBenchmarkValue.IsSigned(kind);
            this.isSize = isSize;
            mask = PackedBenchmarkValue.Mask(kind);

            if (signed)
            {
                signedMin = bits == 64 ? long.MinValue : -(1L << (bits - 1));
                signedMax = bits == 64 ? long.MaxValue : (1L << (bits - 1)) - 1L;
            }
            else
            {
                signedMin = 0;
                signedMax = 0;
            }
        }
    }

    private struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = seed == 0 ? 0x6D2B79F5u : seed;
        }

        public uint NextUInt()
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }

        public ulong NextULong()
        {
            return ((ulong)NextUInt() << 32) | NextUInt();
        }
    }
}
