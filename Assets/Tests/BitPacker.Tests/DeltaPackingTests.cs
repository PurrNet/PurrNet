using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public class DeltaPackedNumberTests
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        NetworkManager.CallAllRegisters();
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

    [Test]
    public void DoubleDeltaPreservesSignedZeroAndNaNPayloadBits()
    {
        double negativeZero = System.BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));
        double oldNaN = System.BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000000001UL));
        double newNaN = System.BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000001234UL));

        AssertBitExactDoubleDelta(0d, negativeZero);
        AssertBitExactDoubleDelta(negativeZero, 0d);
        AssertBitExactDoubleDelta(oldNaN, newNaN);
    }

    private void AssertBitExactDoubleDelta(double oldValue, double newValue)
    {
        packer.ResetPositionAndMode(false);
        Assert.IsTrue(DeltaPacker<double>.Write(packer, oldValue, newValue));
        packer.ResetPositionAndMode(true);

        double decoded = oldValue;
        DeltaPacker<double>.Read(packer, oldValue, ref decoded);
        Assert.AreEqual(System.BitConverter.DoubleToInt64Bits(newValue),
            System.BitConverter.DoubleToInt64Bits(decoded));
    }

    /*[Test]
    public void TestDeltaFloatEdgeCases()
    {
        float[] values =
        {
            0f, float.Epsilon, float.MinValue, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity
        };

        foreach (float oldVal in values)
        {
            foreach (float newVal in values)
            {
                float readValue = oldVal;
                packer.ResetPositionAndMode(false);
                DeltaPacker<float>.Write(packer, oldVal, newVal);
                packer.ResetPositionAndMode(true);
                DeltaPacker<float>.Read(packer, oldVal, ref readValue);

                if (float.IsNaN(newVal))
                    Assert.That(float.IsNaN(readValue), $"NaN failed with old:{oldVal} new:{newVal}");
                else
                    Assert.That(readValue, Is.EqualTo(newVal), $"Failed with old:{oldVal} new:{newVal}");
            }
        }
    }

    [Test]
    public void TestDeltaSmallChanges()
    {
        float baseValue = 1000f;
        float[] deltas = { 0.001f, 0.01f, 0.1f, 1f, 10f };

        foreach (float delta in deltas)
        {
            float oldValue = baseValue;
            float newValue = baseValue + delta;
            float readValue = oldValue;

            packer.ResetPositionAndMode(false);
            DeltaPacker<float>.Write(packer, oldValue, newValue);
            packer.ResetPositionAndMode(true);
            DeltaPacker<float>.Read(packer, oldValue, ref readValue);

            Assert.That(readValue, Is.EqualTo(newValue),
                $"Small delta failed with old:{oldValue} new:{newValue} delta:{delta}");
        }
    }

    [Test]
    public void TestDeltaSequentialChanges()
    {
        float value = 1.0f;

        packer.ResetPositionAndMode(false);

        // Write sequence of small changes
        for (int i = 0; i < 10; i++)
        {
            float oldValue = value;
            value *= 1.1f; // 10% increase each time
            DeltaPacker<float>.Write(packer, oldValue, value);
        }

        packer.ResetPositionAndMode(true);

        // Read back sequence
        value = 1.0f;
        for (int i = 0; i < 10; i++)
        {
            float oldValue = value;
            value *= 1.1f;
            float readValue = oldValue;
            DeltaPacker<float>.Read(packer, oldValue, ref readValue);
            Assert.That(readValue, Is.EqualTo(value),
                $"Sequential change failed at step {i}");
        }
    }*/
}
