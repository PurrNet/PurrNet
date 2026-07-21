using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Modules;
using PurrNet.Packing;

public class DeltaPackedNumberTests
{
    private BitPacker packer;

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
