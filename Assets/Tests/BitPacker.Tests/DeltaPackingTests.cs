using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;

public class DeltaPackedNumberTests
{
    private BitPacker packer;

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    [SetUp]
    public void Setup()
    {
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void TestDeltaDoubleEdgeCases()
    {
        double[] values =
        {
            0d, double.Epsilon, double.MinValue, double.MaxValue, double.NaN, double.PositiveInfinity,
            double.NegativeInfinity
        };

        foreach (double oldVal in values)
        {
            foreach (double newVal in values)
            {
                double readValue = oldVal;
                packer.ResetPositionAndMode(false);
                DeltaPacker<double>.Write(packer, oldVal, newVal);
                packer.ResetPositionAndMode(true);
                DeltaPacker<double>.Read(packer, oldVal, ref readValue);

                if (double.IsNaN(newVal))
                    Assert.That(double.IsNaN(readValue), $"NaN failed with old:{oldVal} new:{newVal}");
                else
                    Assert.That(readValue, Is.EqualTo(newVal), $"Failed with old:{oldVal} new:{newVal}");
            }
        }
    }

}

public class DeltaFloatBitPerfectionTests
{
    private BitPacker packer;

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    [SetUp]
    public void Setup()
    {
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    // +0, -0, min/max denormal, min normal, 1, 1+ulp, -1, min/max, ±inf,
    // quiet NaN, quiet NaN with payload, signaling NaN, negative NaN, pi
    static readonly uint[] floatPatterns =
    {
        0x00000000, 0x80000000, 0x00000001, 0x807FFFFF, 0x00800000,
        0x3F800000, 0x3F800001, 0xBF800000, 0x7F7FFFFF, 0xFF7FFFFF,
        0x7F800000, 0xFF800000, 0x7FC00000, 0x7FC12345, 0x7F800001,
        0xFFC00000, 0x40490FDB,
    };

    static readonly ulong[] doublePatterns =
    {
        0x0000000000000000, 0x8000000000000000, 0x0000000000000001, 0x800FFFFFFFFFFFFF, 0x0010000000000000,
        0x3FF0000000000000, 0x3FF0000000000001, 0xBFF0000000000000, 0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
        0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000, 0x7FF8123456789ABC, 0x7FF0000000000001,
        0xFFF8000000000000, 0x400921FB54442D18,
    };

    [Test]
    public void FloatDelta_AllEdgePairs_AreBitExact()
    {
        foreach (uint oldPattern in floatPatterns)
        {
            foreach (uint newPattern in floatPatterns)
            {
                float oldVal = BitConverter.Int32BitsToSingle((int)oldPattern);
                float newVal = BitConverter.Int32BitsToSingle((int)newPattern);
                float readValue = default;

                uint oldBits = (uint)BitConverter.SingleToInt32Bits(oldVal);
                uint newBits = (uint)BitConverter.SingleToInt32Bits(newVal);

                packer.ResetPositionAndMode(false);
                bool wasChanged = DeltaPacker<float>.Write(packer, oldVal, newVal);
                packer.ResetPositionAndMode(true);
                DeltaPacker<float>.Read(packer, oldVal, ref readValue);

                Assert.That(wasChanged, Is.EqualTo(oldBits != newBits),
                    $"Change flag wrong for old:{oldBits:X8} new:{newBits:X8}");
                Assert.That((uint)BitConverter.SingleToInt32Bits(readValue), Is.EqualTo(newBits),
                    $"Not bit-exact for old:{oldBits:X8} new:{newBits:X8}");
            }
        }
    }

    [Test]
    public void DoubleDelta_AllEdgePairs_AreBitExact()
    {
        foreach (ulong oldBits in doublePatterns)
        {
            foreach (ulong newBits in doublePatterns)
            {
                double oldVal = BitConverter.Int64BitsToDouble((long)oldBits);
                double newVal = BitConverter.Int64BitsToDouble((long)newBits);
                double readValue = default;

                packer.ResetPositionAndMode(false);
                bool wasChanged = DeltaPacker<double>.Write(packer, oldVal, newVal);
                packer.ResetPositionAndMode(true);
                DeltaPacker<double>.Read(packer, oldVal, ref readValue);

                Assert.That(wasChanged, Is.EqualTo(oldBits != newBits),
                    $"Change flag wrong for old:{oldBits:X16} new:{newBits:X16}");
                Assert.That((ulong)BitConverter.DoubleToInt64Bits(readValue), Is.EqualTo(newBits),
                    $"Not bit-exact for old:{oldBits:X16} new:{newBits:X16}");
            }
        }
    }

    [Test]
    public void FloatDelta_UnchangedValue_WritesSingleBit()
    {
        foreach (uint bits in floatPatterns)
        {
            float value = BitConverter.Int32BitsToSingle((int)bits);
            packer.ResetPositionAndMode(false);
            bool wasChanged = DeltaPacker<float>.Write(packer, value, value);

            Assert.That(wasChanged, Is.False, $"Identical bits reported as changed: {bits:X8}");
            Assert.That(packer.positionInBits, Is.EqualTo(1), $"Unchanged value not 1 bit: {bits:X8}");
        }
    }

    [Test]
    public void DoubleDelta_UnchangedValue_WritesSingleBit()
    {
        foreach (ulong bits in doublePatterns)
        {
            double value = BitConverter.Int64BitsToDouble((long)bits);
            packer.ResetPositionAndMode(false);
            bool wasChanged = DeltaPacker<double>.Write(packer, value, value);

            Assert.That(wasChanged, Is.False, $"Identical bits reported as changed: {bits:X16}");
            Assert.That(packer.positionInBits, Is.EqualTo(1), $"Unchanged value not 1 bit: {bits:X16}");
        }
    }

    [Test]
    public void FloatDelta_RandomPatternChains_AreBitExact()
    {
        var rng = new Random(9001);

        packer.ResetPositionAndMode(false);
        var values = new float[10_000];
        float previous = 0f;
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.Int32BitsToSingle(rng.Next(int.MinValue, int.MaxValue));
            DeltaPacker<float>.Write(packer, previous, values[i]);
            previous = values[i];
        }

        packer.ResetPositionAndMode(true);
        previous = 0f;
        for (int i = 0; i < values.Length; i++)
        {
            float readValue = default;
            DeltaPacker<float>.Read(packer, previous, ref readValue);
            Assert.That(BitConverter.SingleToInt32Bits(readValue),
                Is.EqualTo(BitConverter.SingleToInt32Bits(values[i])), $"Chain diverged at {i}");
            previous = readValue;
        }
    }

    [Test]
    public void DoubleDelta_RandomPatternChains_AreBitExact()
    {
        var rng = new Random(9002);

        packer.ResetPositionAndMode(false);
        var values = new double[10_000];
        double previous = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            long bits = ((long)rng.Next(int.MinValue, int.MaxValue) << 32) | (uint)rng.Next(int.MinValue, int.MaxValue);
            values[i] = BitConverter.Int64BitsToDouble(bits);
            DeltaPacker<double>.Write(packer, previous, values[i]);
            previous = values[i];
        }

        packer.ResetPositionAndMode(true);
        previous = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            double readValue = default;
            DeltaPacker<double>.Read(packer, previous, ref readValue);
            Assert.That(BitConverter.DoubleToInt64Bits(readValue),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(values[i])), $"Chain diverged at {i}");
            previous = readValue;
        }
    }
}

public class ClientDeltaTrackerTests
{
    [Test]
    public void CleanupRetainsARecentlyReferencedBaseline()
    {
        using var tracker = new ClientDeltaTracker<int>();

        for (uint id = 1; id <= 64; id++)
            tracker.Set(id, (int)id);

        SetAllEntryTimes(tracker, float.NegativeInfinity);
        tracker.ValidateId(10);

        Assert.That(tracker.FindBestMatch(out uint baselineId), Is.EqualTo(9));
        Assert.That(baselineId, Is.EqualTo(10));

        uint firstRetainedId = tracker.CleanupUpTo(0.5f);

        Assert.That(firstRetainedId, Is.EqualTo(10));
        Assert.That(tracker.ContainsKey(9), Is.False);
        Assert.That(tracker.ContainsKey(10), Is.True);
        Assert.That(tracker.ContainsKey(64), Is.True);
    }

    private static void SetAllEntryTimes<T>(ClientDeltaTracker<T> tracker, float value)
    {
        var historyField = typeof(ClientDeltaTracker<T>).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(historyField, Is.Not.Null);

        var history = (IList)historyField!.GetValue(tracker);
        Assert.That(history, Is.Not.Null.And.Count.GreaterThan(0));

        var enterTimeField = history![0]!.GetType().GetField("enterTime", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(enterTimeField, Is.Not.Null);

        for (int i = 0; i < history.Count; i++)
        {
            object entry = history[i];
            enterTimeField!.SetValue(entry, value);
            history[i] = entry;
        }
    }
}
