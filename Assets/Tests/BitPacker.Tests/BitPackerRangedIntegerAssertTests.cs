using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The ranged BitPacker.WriteInteger overloads assert that the value lies inside [minValue, maxValue].
/// An in-range value must not log an assert, and a value outside the range must log the matching one.
/// </summary>
public class BitPackerRangedIntegerAssertTests
{
    private BitPacker _packer;

    [SetUp]
    public void Setup()
    {
        NetworkManager.LoadOrGenerateHashes();
        _packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        _packer?.Dispose();
    }

    [TestCase(0L)]
    [TestCase(5L)]
    [TestCase(10L)]
    public void WriteInteger_Long_InRangeValue_LogsNoAssertAndRoundTrips(long value)
    {
        const long min = 0, max = 10;
        var bitCount = BitPacker.GetBitCount(min, max);

        _packer.WriteInteger(value, min, max, bitCount);
        LogAssert.NoUnexpectedReceived();

        _packer.ResetPositionAndMode(true);
        long read = 0;
        _packer.ReadInteger(ref read, min, max, bitCount);
        Assert.That(read, Is.EqualTo(value));
    }

    [TestCase(0UL)]
    [TestCase(5UL)]
    [TestCase(10UL)]
    public void WriteInteger_ULong_InRangeValue_LogsNoAssertAndRoundTrips(ulong value)
    {
        const ulong min = 0, max = 10;
        var bitCount = BitPacker.GetBitCount(min, max);

        _packer.WriteInteger(value, min, max, bitCount);
        LogAssert.NoUnexpectedReceived();

        _packer.ResetPositionAndMode(true);
        ulong read = 0;
        _packer.ReadInteger(ref read, min, max, bitCount);
        Assert.That(read, Is.EqualTo(value));
    }

    [Test]
    public void WriteInteger_Long_BelowMin_LogsMinAssert()
    {
        LogAssert.Expect(LogType.Assert, "Data is less than min value; -1 < 0");
        _packer.WriteInteger(-1L, 0L, 10L, BitPacker.GetBitCount(0L, 10L));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void WriteInteger_Long_AboveMax_LogsMaxAssert()
    {
        LogAssert.Expect(LogType.Assert, "Data is greater than max value; 11 > 10");
        _packer.WriteInteger(11L, 0L, 10L, BitPacker.GetBitCount(0L, 10L));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void WriteInteger_ULong_BelowMin_LogsMinAssert()
    {
        LogAssert.Expect(LogType.Assert, "Data is less than min value; 4 < 5");
        _packer.WriteInteger(4UL, 5UL, 15UL, BitPacker.GetBitCount(5UL, 15UL));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void WriteInteger_ULong_AboveMax_LogsMaxAssert()
    {
        LogAssert.Expect(LogType.Assert, "Data is greater than max value; 16 > 15");
        _packer.WriteInteger(16UL, 5UL, 15UL, BitPacker.GetBitCount(5UL, 15UL));
        LogAssert.NoUnexpectedReceived();
    }
}
