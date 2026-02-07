using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

public class DisposableListsDeltaPackerTests
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void SameListNoChanges()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsFalse(hasChanged);

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void EmptyToEmpty()
    {
        var old = DisposableList<int>.Create();
        var current = DisposableList<int>.Create();

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsFalse(hasChanged);

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void EmptyToItems()
    {
        var old = DisposableList<int>.Create();
        var current = DisposableList<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ItemsToEmpty()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create();

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableList<int>.Create(new[] { 99, 99 }); // pre-populate
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(0, result.Count);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void DisposedList()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create();
        current.Dispose();

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableList<int>.Create(new[] { 99 });
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.IsTrue(result.isDisposed);

        old.Dispose();
    }

    [Test]
    public void AppendSingle()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3, 4 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        Debug.Log($"Bits written: {packer.positionInBits}");
        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(4, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void DeleteMiddle()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 3 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(2, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void InsertMiddle()
    {
        var old = DisposableList<int>.Create(new[] { 1, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void CompleteReplacement()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 4, 5, 6 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void MixedOperations()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3, 4 });
        var current = DisposableList<int>.Create(new[] { 0, 1, 3, 4, 5 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(5, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ConsecutiveDeletes()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3, 4, 5 });
        var current = DisposableList<int>.Create(new[] { 1, 5 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(2, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ConsecutiveInserts()
    {
        var old = DisposableList<int>.Create(new[] { 1, 5 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3, 4, 5 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(5, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ModifySameLength()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3, 4, 5 });
        var current = DisposableList<int>.Create(new[] { 2, 4, 6, 8, 10 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        Debug.Log($"Bits written: {packer.positionInBits}");
        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(5, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void Strings()
    {
        var old = DisposableList<string>.Create(new[] { "hello", "world" });
        var current = DisposableList<string>.Create(new[] { "hello", "beautiful", "world" });

        bool isOldEqualToCurrent = PurrEquality<DisposableList<string>>.Equals(old, current);
        Assert.IsFalse(isOldEqualToCurrent, "Expected old and current to be different");

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        bool valuesEqual = PurrEquality<DisposableList<string>>.Equals(current, result);
        Assert.IsTrue(valuesEqual, "Expected values to be equal");
        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void LargeList()
    {
        var old = DisposableList<int>.Create();
        var current = DisposableList<int>.Create();

        for (int i = 0; i < 100; i++)
        {
            old.Add(i);
            if (i % 3 != 0)
                current.Add(i);
        }
        current.Add(200);
        current.Add(201);

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        Debug.Log($"Bits written for large list: {packer.positionInBits}");
        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(current.Count, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void MultipleRoundtrips()
    {
        var versions = new[]
        {
            new[] { 1, 2, 3 },
            new[] { 1, 2, 3, 4 },
            new[] { 1, 3, 4 },
            new[] { 0, 1, 3, 4, 5 },
            new[] { 0, 1 }
        };

        var old = DisposableList<int>.Create(versions[0]);

        for (int i = 1; i < versions.Length; i++)
        {
            var current = DisposableList<int>.Create(versions[i]);

            packer.ResetPositionAndMode(false);
            DeltaPacker<DisposableList<int>>.Write(packer, old, current);

            packer.ResetPositionAndMode(true);

            var result = default(DisposableList<int>);
            DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

            CollectionAssert.AreEqual(current, result);

            old.Dispose();
            old = result;
            current.Dispose();
        }

        old.Dispose();
    }

    [Test]
    public void SameReferenceOldAndNew()
    {
        var list = DisposableList<int>.Create(new[] { 1, 2, 3 });

        // Write with same reference for old and new
        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, list, list);
        Assert.IsFalse(hasChanged, "Same reference should have no changes");

        list.Dispose();
    }

    [Test]
    public void ReadIntoSameReferenceAsOld()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3, 4 });

        DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        packer.ResetPositionAndMode(true);

        var result = old; // Same reference!
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreNotSame(old, result, "Should create new instance");
        Assert.AreEqual(4, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ReuseResultReference()
    {
        var old1 = DisposableList<int>.Create(new[] { 1, 2 });
        var new1 = DisposableList<int>.Create(new[] { 1, 2, 3 });

        var old2 = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var new2 = DisposableList<int>.Create(new[] { 1, 2, 3, 4, 5 });

        // First read
        DeltaPacker<DisposableList<int>>.Write(packer, old1, new1);
        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old1, ref result);

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(new1, result);

        // Reuse same result reference for second read
        packer.ResetPositionAndMode(false);
        DeltaPacker<DisposableList<int>>.Write(packer, old2, new2);
        packer.ResetPositionAndMode(true);

        DeltaPacker<DisposableList<int>>.Read(packer, old2, ref result);

        Assert.AreEqual(5, result.Count);
        CollectionAssert.AreEqual(new2, result);

        old1.Dispose();
        new1.Dispose();
        old2.Dispose();
        new2.Dispose();
        result.Dispose();
    }

    [Test]
    public void SharedBackingList()
    {
        var backing = new System.Collections.Generic.List<int> { 1, 2, 3 };

        // Create two DisposableLists that share backing storage (if possible)
        var old = DisposableList<int>.Create();
        var current = DisposableList<int>.Create();

        old.AddRange(backing);
        current.AddRange(backing);
        current.Add(4);

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(4, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void OldDisposedMidOperation()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3, 4 });

        // Simulate old being disposed before write (prediction rollback scenario)
        old.Dispose();

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var empty = DisposableList<int>.Create();
        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, empty, ref result);

        Assert.AreEqual(4, result.Count);
        CollectionAssert.AreEqual(current, result);

        empty.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void NullOldReference()
    {
        DisposableList<int> old = default;
        var current = DisposableList<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, default, ref result);

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(current, result);

        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void NullNewReference()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        DisposableList<int> current = default;

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableList<int>.Create(new[] { 99 });
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.IsTrue(result.isDisposed);
    }

    [Test]
    public void DisposedNewReference()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3, 4 });
        current.Dispose();

        bool hasChanged = DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableList<int>.Create(new[] { 99 });
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.IsTrue(result.isDisposed);

        old.Dispose();
    }

    [Test]
    public void MultipleReadsWithSameOld()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });

        var versions = new[]
        {
            new[] { 1, 2, 3, 4 },
            new[] { 1, 2, 3, 4, 5 },
            new[] { 1, 2, 3, 4, 5, 6 }
        };

        foreach (var version in versions)
        {
            var current = DisposableList<int>.Create(version);

            packer.ResetPositionAndMode(false);
            DeltaPacker<DisposableList<int>>.Write(packer, old, current);

            packer.ResetPositionAndMode(true);

            var result = default(DisposableList<int>);
            DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

            Assert.AreEqual(version.Length, result.Count);
            CollectionAssert.AreEqual(version, result);

            result.Dispose();
            current.Dispose();
        }

        old.Dispose();
    }

    [Test]
    public void ReadResultPreAllocated()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2 });
        var current = DisposableList<int>.Create(new[] { 1, 2, 3, 4, 5 });

        DeltaPacker<DisposableList<int>>.Write(packer, old, current);
        packer.ResetPositionAndMode(true);

        // Pre-allocate result with different data
        var result = DisposableList<int>.Create(new[] { 99, 98, 97 });

        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        Assert.AreEqual(5, result.Count);
        CollectionAssert.AreEqual(current, result);
        // Make sure old junk data is gone
        Assert.IsFalse(result.Contains(99));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void BothDisposed()
    {
        var old = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableList<int>.Create(new[] { 4, 5, 6 });

        old.Dispose();
        current.Dispose();

        DeltaPacker<DisposableList<int>>.Write(packer, old, current);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<int>);
        DeltaPacker<DisposableList<int>>.Read(packer, old, ref result);

        // Should handle gracefully
        Assert.Pass("No crash with both disposed");
    }
}
