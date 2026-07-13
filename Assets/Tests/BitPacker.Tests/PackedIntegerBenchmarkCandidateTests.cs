using System;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public struct PackedIntegerWireProbe : IPackedAuto
{
    public PackedByte packedByte;
    public PackedSByte packedSByte;
    public PackedUShort packedUShort;
    public PackedShort packedShort;
    public PackedUInt packedUInt;
    public PackedInt packedInt;
    public PackedULong packedULong;
    public PackedLong packedLong;
    public Size size;
}

public sealed class PackedIntegerBenchmarkCandidateTests
{
    private BitPacker _packer;
    private BitPacker _referencePacker;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        NetworkManager.CallAllRegisters();
    }

    [SetUp]
    public void SetUp()
    {
        _packer = new BitPacker();
        _referencePacker = new BitPacker();
    }

    [TearDown]
    public void TearDown()
    {
        _packer.Dispose();
        _referencePacker.Dispose();
        _packer = null;
        _referencePacker = null;
    }

    [Test]
    public void CompactAbsolute_ExhaustivelyRoundTripsEightAndSixteenBitTypes()
    {
        PackedBenchmarkKind[] kinds =
        {
            PackedBenchmarkKind.PackedByte,
            PackedBenchmarkKind.PackedSByte,
            PackedBenchmarkKind.PackedUShort,
            PackedBenchmarkKind.PackedShort
        };

        foreach (PackedBenchmarkKind kind in kinds)
        {
            ulong mask = PackedBenchmarkValue.Mask(kind);
            var baseline = new PackedBenchmarkValue(kind, 0);
            for (ulong raw = 0; raw <= mask; raw++)
            {
                var expected = new PackedBenchmarkValue(kind, raw);
                _packer.ResetPositionAndMode(false);
                PackedIntegerBenchmarkCodecs.WriteCompactAbsolute(_packer, baseline, expected);
                int writtenBits = _packer.positionInBits;
                Assert.That(writtenBits, Is.EqualTo(PackedIntegerBenchmarkCodecs.CompactAbsoluteBits(expected)),
                    $"Predicted absolute bits differ for {expected}.");

                _packer.ResetPositionAndMode(true);
                PackedBenchmarkValue actual = default;
                PackedIntegerBenchmarkCodecs.ReadCompactAbsolute(_packer, baseline, ref actual);
                Assert.That(actual, Is.EqualTo(expected), $"Compact absolute round-trip failed for {expected}.");
                Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));

                _referencePacker.ResetPositionAndMode(false);
                PackedIntegerBenchmarkCodecs.WriteCurrentAbsolute(_referencePacker, baseline, expected);
                Assert.That(_referencePacker.positionInBits, Is.EqualTo(writtenBits),
                    $"Production bit count differs for {expected}.");
                _referencePacker.ResetPositionAndMode(true);
                PackedBenchmarkValue production = default;
                PackedIntegerBenchmarkCodecs.ReadCurrentAbsolute(_referencePacker, baseline, ref production);
                Assert.That(production, Is.EqualTo(expected),
                    $"Production absolute round-trip failed for {expected}.");
                Assert.That(_referencePacker.positionInBits, Is.EqualTo(writtenBits));

                if (raw == mask)
                    break;
            }
        }
    }

    [Test]
    public void CompactAbsolute_IsPointwiseNoLargerThanLegacyAcrossBenchmarkCorpora()
    {
        foreach (PackedBenchmarkKind kind in Enum.GetValues(typeof(PackedBenchmarkKind)))
        {
            foreach (var scenario in PackedIntegerBenchmarkCorpora.BuildAbsoluteScenarios(kind))
            {
                for (int i = 0; i < scenario.pairs.Length; i++)
                {
                    var pair = scenario.pairs[i];
                    _packer.ResetPositionAndMode(false);
                    PackedIntegerBenchmarkCodecs.WriteCompactAbsolute(_packer, pair.oldValue, pair.newValue);
                    int compactBits = _packer.positionInBits;
                    int legacyBits = LegacyAbsoluteBits(pair.newValue);

                    Assert.That(compactBits, Is.LessThanOrEqualTo(legacyBits),
                        $"Compact absolute encoding regressed {kind}/{scenario.name} at pair {i}: " +
                        $"{legacyBits} -> {compactBits} bits for {pair.newValue}.");
                }
            }
        }
    }

    [Test]
    public void ProductionCodecs_MatchSelectedCandidateWireAcrossBenchmarkCorpora()
    {
        foreach (PackedBenchmarkKind kind in Enum.GetValues(typeof(PackedBenchmarkKind)))
        {
            foreach (var scenario in PackedIntegerBenchmarkCorpora.BuildAbsoluteScenarios(kind))
            {
                for (int i = 0; i < scenario.pairs.Length; i++)
                {
                    var pair = scenario.pairs[i];
                    _packer.ResetPositionAndMode(false);
                    PackedIntegerBenchmarkCodecs.WriteCurrentAbsolute(_packer, pair.oldValue, pair.newValue);
                    _referencePacker.ResetPositionAndMode(false);
                    PackedIntegerBenchmarkCodecs.WriteCompactAbsolute(
                        _referencePacker, pair.oldValue, pair.newValue);
                    AssertSameBits($"absolute {kind}/{scenario.name}/{i}");
                }
            }

            foreach (var scenario in PackedIntegerBenchmarkCorpora.BuildDeltaScenarios(kind))
            {
                for (int i = 0; i < scenario.pairs.Length; i++)
                {
                    var pair = scenario.pairs[i];
                    _packer.ResetPositionAndMode(false);
                    bool productionChanged = PackedIntegerBenchmarkCodecs.WriteCurrentDelta(
                        _packer, pair.oldValue, pair.newValue);
                    _referencePacker.ResetPositionAndMode(false);
                    bool candidateChanged = PackedIntegerBenchmarkCodecs.WriteCompactModularDelta(
                        _referencePacker, pair.oldValue, pair.newValue);
                    Assert.That(productionChanged, Is.EqualTo(candidateChanged));
                    AssertSameBits($"delta {kind}/{scenario.name}/{i}");
                }
            }
        }
    }

    [Test]
    public void ProductionRegistrations_UseExplicitManagedAndNativePackedCodecs()
    {
        AssertRegistrations<PackedByte>();
        AssertRegistrations<PackedSByte>();
        AssertRegistrations<PackedUShort>();
        AssertRegistrations<PackedShort>();
        AssertRegistrations<PackedUInt>();
        AssertRegistrations<PackedInt>();
        AssertRegistrations<PackedULong>();
        AssertRegistrations<PackedLong>();
        AssertRegistrations<Size>();

        Assert.That(Packer<byte>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackUIntegers)));
        Assert.That(Packer<sbyte>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackIntegers)));
        Assert.That(Packer<ushort>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackUIntegers)));
        Assert.That(Packer<short>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackIntegers)));
        Assert.That(Packer<uint>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackUIntegers)));
        Assert.That(Packer<int>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackIntegers)));
        Assert.That(Packer<ulong>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackUIntegers)));
        Assert.That(Packer<long>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackIntegers)));
    }

    [Test]
    public void ProductionAbsolute_MaximaRoundTripAtEveryBitOffsetWithTrailingMarker()
    {
        var values = new[]
        {
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedByte, byte.MaxValue),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedSByte, unchecked((byte)sbyte.MinValue)),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedUShort, ushort.MaxValue),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedShort, unchecked((ushort)short.MinValue)),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedUInt, uint.MaxValue),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedInt, unchecked((uint)int.MinValue)),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedULong, ulong.MaxValue),
            new PackedBenchmarkValue(PackedBenchmarkKind.PackedLong, unchecked((ulong)long.MinValue)),
            new PackedBenchmarkValue(PackedBenchmarkKind.Size, uint.MaxValue)
        };
        int[] expectedBits = { 8, 8, 18, 18, 36, 36, 72, 72, 47 };

        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            var value = values[valueIndex];
            var baseline = new PackedBenchmarkValue(value.kind, 0);
            for (int offset = 0; offset < 8; offset++)
            {
                _packer.ResetPositionAndMode(false);
                if (offset > 0)
                    _packer.WriteBits((1UL << offset) - 1UL, (byte)offset);
                int payloadStart = _packer.positionInBits;
                PackedIntegerBenchmarkCodecs.WriteCurrentAbsolute(_packer, baseline, value);
                int payloadEnd = _packer.positionInBits;
                _packer.WriteBits(0x15A5, 13);

                Assert.That(payloadEnd - payloadStart, Is.EqualTo(expectedBits[valueIndex]));
                _packer.ResetPositionAndMode(true);
                if (offset > 0)
                    Assert.That(_packer.ReadBits((byte)offset), Is.EqualTo((1UL << offset) - 1UL));
                PackedBenchmarkValue actual = default;
                PackedIntegerBenchmarkCodecs.ReadCurrentAbsolute(_packer, baseline, ref actual);
                Assert.That(actual, Is.EqualTo(value), $"{value.kind} failed at offset {offset}.");
                Assert.That(_packer.positionInBits, Is.EqualTo(payloadEnd));
                Assert.That(_packer.ReadBits(13), Is.EqualTo(0x15A5));
            }
        }
    }

    [Test]
    public void GeneratedAllPackedFields_NormalAndDeltaRoundTrip()
    {
        var oldValue = new PackedIntegerWireProbe
        {
            packedByte = new PackedByte(1), packedSByte = new PackedSByte(-1),
            packedUShort = new PackedUShort(2), packedShort = new PackedShort(-2),
            packedUInt = new PackedUInt(3), packedInt = new PackedInt(-3),
            packedULong = new PackedULong(4), packedLong = new PackedLong(-4), size = new Size(5)
        };
        var newValue = new PackedIntegerWireProbe
        {
            packedByte = new PackedByte(byte.MaxValue), packedSByte = new PackedSByte(sbyte.MinValue),
            packedUShort = new PackedUShort(ushort.MaxValue), packedShort = new PackedShort(short.MinValue),
            packedUInt = new PackedUInt(uint.MaxValue), packedInt = new PackedInt(int.MinValue),
            packedULong = new PackedULong(ulong.MaxValue), packedLong = new PackedLong(long.MinValue),
            size = new Size(uint.MaxValue)
        };

        _packer.ResetPositionAndMode(false);
        Packer<PackedIntegerWireProbe>.Write(_packer, newValue);
        _packer.ResetPositionAndMode(true);
        PackedIntegerWireProbe packedResult = default;
        Packer<PackedIntegerWireProbe>.Read(_packer, ref packedResult);
        AssertProbe(packedResult, newValue);

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<PackedIntegerWireProbe>.Write(_packer, oldValue, newValue), Is.True);
        _packer.ResetPositionAndMode(true);
        PackedIntegerWireProbe deltaResult = default;
        DeltaPacker<PackedIntegerWireProbe>.Read(_packer, oldValue, ref deltaResult);
        AssertProbe(deltaResult, newValue);
        Assert.That(NativePacker<PackedIntegerWireProbe>.HasPacker(), Is.True);
        Assert.That(NativeDeltaPacker<PackedIntegerWireProbe>.HasPacker(), Is.True);
    }

    [Test]
    public void ProductionNativeCodecs_CrossRoundTripEveryPackedType()
    {
        AssertNativeCrossRoundTrip(new PackedByte(1), new PackedByte(byte.MaxValue), value => value.value);
        AssertNativeCrossRoundTrip(new PackedSByte(-1), new PackedSByte(sbyte.MinValue),
            value => unchecked((byte)value.value));
        AssertNativeCrossRoundTrip(new PackedUShort(2), new PackedUShort(ushort.MaxValue), value => value.value);
        AssertNativeCrossRoundTrip(new PackedShort(-2), new PackedShort(short.MinValue),
            value => unchecked((ushort)value.value));
        AssertNativeCrossRoundTrip(new PackedUInt(3), new PackedUInt(uint.MaxValue), value => value.value);
        AssertNativeCrossRoundTrip(new PackedInt(-3), new PackedInt(int.MinValue),
            value => unchecked((uint)value.value));
        AssertNativeCrossRoundTrip(new PackedULong(4), new PackedULong(ulong.MaxValue), value => value.value);
        AssertNativeCrossRoundTrip(new PackedLong(-4), new PackedLong(long.MinValue),
            value => unchecked((ulong)value.value));
        AssertNativeCrossRoundTrip(new Size(5), new Size(uint.MaxValue), value => value.value);
    }

    [Test]
    public void ProductionReaders_RejectTerminalOverflowAndReservedDeltaCodes()
    {
        AssertMalformedAbsolute<PackedUShort>(packer =>
        {
            packer.WriteBits(0x80, 8);
            packer.WriteBits(0x80, 8);
            packer.WriteBits(3, 2);
        });
        AssertMalformedAbsolute<PackedUInt>(packer =>
        {
            for (int i = 0; i < 4; i++) packer.WriteBits(0x80, 8);
            packer.WriteBits(15, 4);
        });
        AssertMalformedAbsolute<PackedULong>(packer =>
        {
            for (int i = 0; i < 9; i++) packer.WriteBits(0xFF, 8);
        });
        AssertMalformedAbsolute<Size>(packer =>
        {
            for (int i = 0; i < 15; i++) packer.WriteBits(4, 3);
            packer.WriteBits(3, 2);
        });

        AssertReservedDelta(new PackedByte(0), PackedBenchmarkKind.PackedByte);
        AssertReservedDelta(new PackedUShort(0), PackedBenchmarkKind.PackedUShort);
        AssertReservedDelta(new PackedUInt(0), PackedBenchmarkKind.PackedUInt);
        AssertReservedDelta(new PackedULong(0), PackedBenchmarkKind.PackedULong);
        AssertReservedDelta(new Size(0), PackedBenchmarkKind.PackedUInt);
    }

    [Test]
    public void DeltaCandidates_RoundTripAllBenchmarkCorporaAndMatchPredictedCompactBits()
    {
        foreach (PackedBenchmarkKind kind in Enum.GetValues(typeof(PackedBenchmarkKind)))
        {
            foreach (var scenario in PackedIntegerBenchmarkCorpora.BuildDeltaScenarios(kind))
            {
                for (int i = 0; i < scenario.pairs.Length; i++)
                {
                    var pair = scenario.pairs[i];
                    RoundTripDelta(pair, PackedIntegerBenchmarkCodecs.WriteCompactModularDelta,
                        PackedIntegerBenchmarkCodecs.ReadCompactModularDelta, "compact modular");
                    Assert.That(_packer.positionInBits,
                        Is.EqualTo(PackedIntegerBenchmarkCodecs.CompactModularDeltaBits(
                            pair.oldValue, pair.newValue)),
                        $"Predicted compact delta bits differ for {kind}/{scenario.name}/{i}.");

                    RoundTripDelta(pair, PackedIntegerBenchmarkCodecs.WriteCompactForwardDelta,
                        PackedIntegerBenchmarkCodecs.ReadCompactForwardDelta, "compact forward");
                    RoundTripDelta(pair, PackedIntegerBenchmarkCodecs.WriteAdaptiveModularDelta,
                        PackedIntegerBenchmarkCodecs.ReadAdaptiveModularDelta, "adaptive modular/raw");
                    RoundTripDelta(pair, PackedIntegerBenchmarkCodecs.WriteCompactTwoBitDelta,
                        PackedIntegerBenchmarkCodecs.ReadCompactTwoBitDelta, "compact two-bit");
                    RoundTripDelta(pair, PackedIntegerBenchmarkCodecs.WriteAdaptiveTwoBitDelta,
                        PackedIntegerBenchmarkCodecs.ReadAdaptiveTwoBitDelta, "adaptive two-bit/raw");
                    RoundTripDelta(pair, PackedIntegerBenchmarkCodecs.WriteRawOnChange,
                        PackedIntegerBenchmarkCodecs.ReadRawOnChange, "raw-on-change");
                }
            }
        }
    }

    [Test]
    public void ProductionDelta_ExhaustivelyRoundTripsEightBitPairsAndSixteenBitDeltas()
    {
        PackedBenchmarkKind[] eightBitKinds =
        {
            PackedBenchmarkKind.PackedByte,
            PackedBenchmarkKind.PackedSByte
        };
        foreach (PackedBenchmarkKind kind in eightBitKinds)
        {
            for (ulong oldRaw = 0; oldRaw <= byte.MaxValue; oldRaw++)
            for (ulong newRaw = 0; newRaw <= byte.MaxValue; newRaw++)
                VerifyProductionDelta(kind, oldRaw, newRaw);
        }

        PackedBenchmarkKind[] sixteenBitKinds =
        {
            PackedBenchmarkKind.PackedUShort,
            PackedBenchmarkKind.PackedShort
        };
        foreach (PackedBenchmarkKind kind in sixteenBitKinds)
        {
            const ulong oldRaw = 0x5A3C;
            for (ulong deltaRaw = 0; deltaRaw <= ushort.MaxValue; deltaRaw++)
                VerifyProductionDelta(kind, oldRaw, unchecked(oldRaw + deltaRaw) & ushort.MaxValue);
        }
    }

    [Test]
    public void ShiftedSize_UsesReclaimedTiersAndWidthAwareTerminal()
    {
        AssertSizeBits(0, 3);
        AssertSizeBits(3, 3);
        AssertSizeBits(4, 6);
        AssertSizeBits(19, 6);
        AssertSizeBits(20, 9);
        AssertSizeBits(83, 9);
        AssertSizeBits(84, 12);
        AssertSizeBits(339, 12);
        AssertSizeBits(340, 15);
        AssertSizeBits(uint.MaxValue, 47);
    }

    [Test]
    public void CompactPackedLong_ReclaimsTheFinalContinuationBit()
    {
        var zero = new PackedBenchmarkValue(PackedBenchmarkKind.PackedULong, 0);
        var maximum = new PackedBenchmarkValue(PackedBenchmarkKind.PackedULong, ulong.MaxValue);
        _packer.ResetPositionAndMode(false);
        PackedIntegerBenchmarkCodecs.WriteCompactAbsolute(_packer, zero, maximum);
        Assert.That(_packer.positionInBits, Is.EqualTo(72));
    }

    private delegate bool DeltaWrite(BitPacker packer, PackedBenchmarkValue oldValue,
        PackedBenchmarkValue newValue);

    private delegate void DeltaRead(BitPacker packer, PackedBenchmarkValue oldValue,
        ref PackedBenchmarkValue value);

    private void RoundTripDelta(DeltaBenchmarkPair<PackedBenchmarkValue> pair,
        DeltaWrite write, DeltaRead read, string codec)
    {
        _packer.ResetPositionAndMode(false);
        bool changed = write(_packer, pair.oldValue, pair.newValue);
        int writtenBits = _packer.positionInBits;
        Assert.That(changed, Is.EqualTo(!pair.oldValue.Equals(pair.newValue)),
            $"{codec} returned the wrong changed state.");

        _packer.ResetPositionAndMode(true);
        PackedBenchmarkValue actual = default;
        read(_packer, pair.oldValue, ref actual);
        Assert.That(actual, Is.EqualTo(pair.newValue),
            $"{codec} round-trip failed for {pair.oldValue} -> {pair.newValue}.");
        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
    }

    private void AssertNativeCrossRoundTrip<T>(T oldValue, T newValue, Func<T, ulong> raw)
    {
        const ulong marker = 0x15A5;

        _packer.ResetPositionAndMode(false);
        NativePacker<T>.Write(_packer, newValue);
        int payloadEnd = _packer.positionInBits;
        _packer.WriteBits(marker, 13);
        _packer.ResetPositionAndMode(true);
        T value = default;
        Packer<T>.Read(_packer, ref value);
        Assert.That(raw(value), Is.EqualTo(raw(newValue)));
        Assert.That(_packer.positionInBits, Is.EqualTo(payloadEnd));
        Assert.That(_packer.ReadBits(13), Is.EqualTo(marker));

        _packer.ResetPositionAndMode(false);
        Packer<T>.Write(_packer, newValue);
        payloadEnd = _packer.positionInBits;
        _packer.WriteBits(marker, 13);
        _packer.ResetPositionAndMode(true);
        value = default;
        NativePacker<T>.Read(_packer, ref value);
        Assert.That(raw(value), Is.EqualTo(raw(newValue)));
        Assert.That(_packer.positionInBits, Is.EqualTo(payloadEnd));
        Assert.That(_packer.ReadBits(13), Is.EqualTo(marker));

        _packer.ResetPositionAndMode(false);
        Assert.That(NativeDeltaPacker<T>.Write(_packer, oldValue, newValue), Is.True);
        payloadEnd = _packer.positionInBits;
        _packer.WriteBits(marker, 13);
        _packer.ResetPositionAndMode(true);
        value = default;
        DeltaPacker<T>.Read(_packer, oldValue, ref value);
        Assert.That(raw(value), Is.EqualTo(raw(newValue)));
        Assert.That(_packer.positionInBits, Is.EqualTo(payloadEnd));
        Assert.That(_packer.ReadBits(13), Is.EqualTo(marker));

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<T>.Write(_packer, oldValue, newValue), Is.True);
        payloadEnd = _packer.positionInBits;
        _packer.WriteBits(marker, 13);
        _packer.ResetPositionAndMode(true);
        value = default;
        NativeDeltaPacker<T>.Read(_packer, oldValue, ref value);
        Assert.That(raw(value), Is.EqualTo(raw(newValue)));
        Assert.That(_packer.positionInBits, Is.EqualTo(payloadEnd));
        Assert.That(_packer.ReadBits(13), Is.EqualTo(marker));
    }

    private void VerifyProductionDelta(PackedBenchmarkKind kind, ulong oldRaw, ulong newRaw)
    {
        var oldValue = new PackedBenchmarkValue(kind, oldRaw);
        var expected = new PackedBenchmarkValue(kind, newRaw);
        _packer.ResetPositionAndMode(false);
        bool changed = PackedIntegerBenchmarkCodecs.WriteCurrentDelta(_packer, oldValue, expected);
        int writtenBits = _packer.positionInBits;
        bool expectedChanged = oldValue.raw != expected.raw;
        if (changed == expectedChanged)
        {
            _packer.ResetPositionAndMode(true);
            PackedBenchmarkValue actual = default;
            PackedIntegerBenchmarkCodecs.ReadCurrentDelta(_packer, oldValue, ref actual);
            if (!actual.Equals(expected) || _packer.positionInBits != writtenBits)
                Assert.Fail($"Production delta failed for {kind}: 0x{oldRaw:X} -> 0x{newRaw:X}.");
            return;
        }

        Assert.Fail($"Production delta returned the wrong changed flag for {kind}: " +
                    $"0x{oldRaw:X} -> 0x{newRaw:X}.");
    }

    private void AssertMalformedAbsolute<T>(Action<BitPacker> writeMalformed)
    {
        _packer.ResetPositionAndMode(false);
        writeMalformed(_packer);
        _packer.ResetPositionAndMode(true);
        T value = default;
        Assert.Throws<InvalidOperationException>(() => Packer<T>.Read(_packer, ref value));
    }

    private void AssertReservedDelta<T>(T oldValue, PackedBenchmarkKind wireKind)
    {
        _packer.ResetPositionAndMode(false);
        _packer.WriteBit(true);
        var zero = new PackedBenchmarkValue(wireKind, 0);
        var reserved = new PackedBenchmarkValue(wireKind, PackedBenchmarkValue.Mask(wireKind));
        PackedIntegerBenchmarkCodecs.WriteCompactAbsolute(_packer, zero, reserved);
        _packer.ResetPositionAndMode(true);
        T value = default;
        Assert.Throws<InvalidOperationException>(() => DeltaPacker<T>.Read(_packer, oldValue, ref value));
    }

    private void AssertSameBits(string context)
    {
        int actualBits = _packer.positionInBits;
        int expectedBits = _referencePacker.positionInBits;
        Assert.That(actualBits, Is.EqualTo(expectedBits), $"Bit count differs for {context}.");

        _packer.ResetPositionAndMode(true);
        _referencePacker.ResetPositionAndMode(true);
        int remaining = actualBits;
        while (remaining > 0)
        {
            byte bits = (byte)Math.Min(64, remaining);
            Assert.That(_packer.ReadBits(bits), Is.EqualTo(_referencePacker.ReadBits(bits)),
                $"Wire bits differ for {context}.");
            remaining -= bits;
        }
    }

    private static void AssertRegistrations<T>()
    {
        Assert.That(Packer<T>.HasPacker(), Is.True, $"Packer<{typeof(T).Name}> is missing.");
        Assert.That(NativePacker<T>.HasPacker(), Is.True, $"NativePacker<{typeof(T).Name}> is missing.");
        Assert.That(DeltaPacker<T>.HasPacker(), Is.True, $"DeltaPacker<{typeof(T).Name}> is missing.");
        Assert.That(NativeDeltaPacker<T>.HasPacker(), Is.True,
            $"NativeDeltaPacker<{typeof(T).Name}> is missing.");
        Assert.That(Packer<T>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(PackingIntegers)));
        Assert.That(Packer<T>.DirectRead.Method.DeclaringType, Is.EqualTo(typeof(PackingIntegers)));
        Assert.That(DeltaPacker<T>.DirectWrite.Method.DeclaringType, Is.EqualTo(typeof(DeltaPackInteger)));
        Assert.That(DeltaPacker<T>.DirectRead.Method.DeclaringType, Is.EqualTo(typeof(DeltaPackInteger)));
    }

    private static int LegacyAbsoluteBits(PackedBenchmarkValue value)
    {
        int width = PackedBenchmarkValue.GetWidth(value.kind);
        int chunkBits = value.kind == PackedBenchmarkKind.Size ? 2 : 7;
        ulong encoded = PackedBenchmarkValue.IsSigned(value.kind)
            ? ZigzagEncodeRaw(value.raw, width)
            : value.raw;
        int bitLength = encoded == 0 ? 0 : 64 - CountLeadingZeroBits(encoded);
        int tiers = Math.Max(1, (bitLength + chunkBits - 1) / chunkBits);
        return tiers * (chunkBits + 1);
    }

    private static ulong ZigzagEncodeRaw(ulong raw, int width)
    {
        ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        ulong sign = raw >> (width - 1);
        return ((raw << 1) ^ (0UL - sign)) & mask;
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

    private static void AssertProbe(PackedIntegerWireProbe actual, PackedIntegerWireProbe expected)
    {
        Assert.That(actual.packedByte.value, Is.EqualTo(expected.packedByte.value));
        Assert.That(actual.packedSByte.value, Is.EqualTo(expected.packedSByte.value));
        Assert.That(actual.packedUShort.value, Is.EqualTo(expected.packedUShort.value));
        Assert.That(actual.packedShort.value, Is.EqualTo(expected.packedShort.value));
        Assert.That(actual.packedUInt.value, Is.EqualTo(expected.packedUInt.value));
        Assert.That(actual.packedInt.value, Is.EqualTo(expected.packedInt.value));
        Assert.That(actual.packedULong.value, Is.EqualTo(expected.packedULong.value));
        Assert.That(actual.packedLong.value, Is.EqualTo(expected.packedLong.value));
        Assert.That(actual.size.value, Is.EqualTo(expected.size.value));
    }

    private void AssertSizeBits(uint value, int expectedBits)
    {
        var baseline = new PackedBenchmarkValue(PackedBenchmarkKind.Size, 0);
        var current = new PackedBenchmarkValue(PackedBenchmarkKind.Size, value);
        _packer.ResetPositionAndMode(false);
        PackedIntegerBenchmarkCodecs.WriteCompactAbsolute(_packer, baseline, current);
        Assert.That(_packer.positionInBits, Is.EqualTo(expectedBits), $"Unexpected Size cost for {value}.");

        _packer.ResetPositionAndMode(true);
        PackedBenchmarkValue decoded = default;
        PackedIntegerBenchmarkCodecs.ReadCompactAbsolute(_packer, baseline, ref decoded);
        Assert.That(decoded, Is.EqualTo(current));
        Assert.That(_packer.positionInBits, Is.EqualTo(expectedBits));
    }
}
