using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public class DeltaListMyersBenchmarkCandidateTests
{
    [SetUp]
    public void SetUp()
    {
        NetworkManager.CallAllRegisters();
    }

    [Test]
    public void CompactEstimateMatchesWrittenPayload()
    {
        var baseline = BuildRange(96);
        var cases = new List<List<int>>
        {
            Mutate(baseline, list => list[48]++),
            Mutate(baseline, list => list.InsertRange(24, new[] { 24, 25, 26, 27 })),
            Mutate(baseline, list => list.RemoveRange(31, 4)),
            Mutate(baseline, list =>
            {
                list[16] += 1;
                list[18] += 1;
                list[40] += 2;
                list[43] += 2;
            }),
            Mutate(baseline, list =>
            {
                for (int i = 32; i < 48; i++)
                    list[i] = 10_000 + i;
            })
        };

        foreach (var current in cases)
        {
            Assert.IsTrue(DeltaListMyersBenchmarkCandidates.PrepareCompact(
                baseline, current, out long estimatedBits));
            using var packer = new BitPacker();
            DeltaListMyersBenchmarkCandidates.WritePreparedCompact(packer, baseline, current);
            Assert.AreEqual(estimatedBits, packer.positionInBits);

            packer.ResetPositionAndMode(true);
            List<int> decoded = DeltaListMyersBenchmarkCandidates.ReadCompact(packer, baseline);
            CollectionAssert.AreEqual(current, decoded);
            Assert.AreEqual(estimatedBits, packer.positionInBits);
        }
    }

    [Test]
    public void ExhaustiveDuplicateHeavyShortListsRoundTrip()
    {
        List<List<int>> values = BuildSequences(maxLength: 4, alphabetSize: 3);
        foreach (var oldValue in values)
        foreach (var newValue in values)
            AssertRoundTrip(oldValue, newValue);
    }

    [Test]
    public void EditDistanceBoundaryAndFallbackRoundTrip()
    {
        var baseline = BuildRange(64);
        var atLimit = new List<int>(baseline);
        var overLimit = new List<int>(baseline);
        for (int i = 0; i < 16; i++)
            atLimit[i] = 10_000 + i;
        for (int i = 0; i < 17; i++)
            overLimit[i] = 20_000 + i;

        Assert.IsTrue(DeltaListMyersBenchmarkCandidates.PrepareCompact(
            baseline, atLimit, out _), "D=32 should use the bounded script.");
        Assert.IsFalse(DeltaListMyersBenchmarkCandidates.PrepareCompact(
            baseline, overLimit, out _), "D=34 should use the full fallback.");
        AssertRoundTrip(baseline, atLimit);
        AssertRoundTrip(baseline, overLimit);
    }

    [Test]
    public void CountBoundariesNullsAndExtremeIntsRoundTrip()
    {
        int[] counts = { 1, 4, 5, 20, 21, 84, 85, 340 };
        foreach (int count in counts)
        {
            var oldValue = BuildRange(12);
            var newValue = new List<int>(oldValue);
            for (int i = 0; i < count; i++)
                newValue.Add(unchecked(int.MinValue + i));
            AssertRoundTrip(oldValue, newValue);
        }

        AssertRoundTrip(null, null);
        AssertRoundTrip(null, new List<int> { int.MinValue, 0, int.MaxValue });
        AssertRoundTrip(new List<int> { int.MinValue, 0, int.MaxValue }, null);
        AssertRoundTrip(new List<int>(), new List<int>());
    }

    [Test]
    public void SeededStructuralFuzzRoundTrips()
    {
        var random = new Random(0x5EED_2026);
        for (int iteration = 0; iteration < 250; iteration++)
        {
            int oldCount = random.Next(0, 80);
            var oldValue = new List<int>(oldCount);
            for (int i = 0; i < oldCount; i++)
                oldValue.Add(random.Next(-4, 5));

            var newValue = new List<int>(oldValue);
            int edits = random.Next(0, 45);
            for (int edit = 0; edit < edits; edit++)
            {
                switch (random.Next(3))
                {
                    case 0 when newValue.Count != 0:
                        newValue[random.Next(newValue.Count)] = random.Next(-8, 9);
                        break;
                    case 1 when newValue.Count != 0:
                        newValue.RemoveAt(random.Next(newValue.Count));
                        break;
                    default:
                        newValue.Insert(random.Next(newValue.Count + 1), random.Next(-8, 9));
                        break;
                }
            }

            AssertRoundTrip(oldValue, newValue);
        }
    }

    private static void AssertRoundTrip(List<int> oldValue, List<int> newValue)
    {
        using var packer = new BitPacker();
        bool changed = DeltaListMyersBenchmarkCandidates.WriteBoundedMyers(packer, oldValue, newValue);
        Assert.AreEqual(!ListsEqual(oldValue, newValue), changed);
        int writtenBits = packer.positionInBits;

        packer.ResetPositionAndMode(true);
        List<int> decoded = null;
        DeltaListMyersBenchmarkCandidates.ReadBoundedMyers(packer, oldValue, ref decoded);
        if (newValue == null)
            Assert.IsNull(decoded);
        else
            CollectionAssert.AreEqual(newValue, decoded);
        Assert.AreEqual(writtenBits, packer.positionInBits);
    }

    private static List<int> BuildRange(int count)
    {
        var result = new List<int>(count);
        for (int i = 0; i < count; i++)
            result.Add(i);
        return result;
    }

    private static List<int> Mutate(List<int> baseline, Action<List<int>> mutation)
    {
        var result = new List<int>(baseline);
        mutation(result);
        return result;
    }

    private static List<List<int>> BuildSequences(int maxLength, int alphabetSize)
    {
        var result = new List<List<int>> { new List<int>() };
        for (int length = 1; length <= maxLength; length++)
        {
            int count = 1;
            for (int i = 0; i < length; i++)
                count *= alphabetSize;
            for (int encoded = 0; encoded < count; encoded++)
            {
                int value = encoded;
                var sequence = new List<int>(length);
                for (int i = 0; i < length; i++)
                {
                    sequence.Add(value % alphabetSize);
                    value /= alphabetSize;
                }
                result.Add(sequence);
            }
        }
        return result;
    }

    private static bool ListsEqual(List<int> left, List<int> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (left[i] != right[i]) return false;
        return true;
    }
}
