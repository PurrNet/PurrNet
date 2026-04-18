using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

public class DisposableHashSetsDeltaPackerTests
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
        PackCollections.RegisterDisposableHashSet<int>();
        PackCollections.RegisterDisposableHashSet<string>();
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void SameSetNoChanges()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsFalse(hasChanged);

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void EmptyToEmpty()
    {
        var old = DisposableHashSet<int>.Create();
        var current = DisposableHashSet<int>.Create();

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsFalse(hasChanged);

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void EmptyToItems()
    {
        var old = DisposableHashSet<int>.Create();
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ItemsToEmpty()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create();

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableHashSet<int>.Create(new[] { 99, 98 });
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(0, result.Count);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void DisposedSet()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create();
        current.Dispose();

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableHashSet<int>.Create(new[] { 99 });
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.IsTrue(result.isDisposed);

        old.Dispose();
    }

    [Test]
    public void AddSingle()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        Debug.Log($"Bits written: {packer.positionInBits}");
        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(4, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void RemoveSingle()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 3 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void CompleteReplacement()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 4, 5, 6 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void DefaultOld()
    {
        DisposableHashSet<int> old = default;
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, default, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void DefaultNew()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        DisposableHashSet<int> current = default;

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableHashSet<int>.Create(new[] { 99 });
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.IsTrue(result.isDisposed);

        old.Dispose();
    }

    [Test]
    public void DisposedNew()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4 });
        current.Dispose();

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableHashSet<int>.Create(new[] { 99 });
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.IsTrue(result.isDisposed);

        old.Dispose();
    }

    [Test]
    public void BothDisposed()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 4, 5, 6 });

        old.Dispose();
        current.Dispose();

        DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.Pass("No crash with both disposed");
    }

    [Test]
    public void BothDefault()
    {
        DisposableHashSet<int> old = default;
        DisposableHashSet<int> current = default;

        DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.Pass("No crash with both default");
    }

    [Test]
    public void MultipleRoundtrips()
    {
        var versions = new[]
        {
            new[] { 1, 2, 3 },
            new[] { 1, 2, 3, 4 },
            new[] { 1, 4 },
            new[] { 0, 1, 4, 5 },
            new[] { 0, 1 }
        };

        var old = DisposableHashSet<int>.Create(versions[0]);

        for (int i = 1; i < versions.Length; i++)
        {
            var current = DisposableHashSet<int>.Create(versions[i]);

            packer.ResetPositionAndMode(false);
            DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);

            packer.ResetPositionAndMode(true);

            var result = default(DisposableHashSet<int>);
            DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

            Assert.AreEqual(versions[i].Length, result.Count, $"Round {i} count mismatch");
            Assert.IsTrue(result.SetEquals(current), $"Round {i} content mismatch");

            old.Dispose();
            old = result;
            current.Dispose();
        }

        old.Dispose();
    }

    [Test]
    public void MultipleReadsWithSameOld()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        var versions = new[]
        {
            new[] { 1, 2, 3, 4 },
            new[] { 1, 2, 3, 4, 5 },
            new[] { 1, 2, 3, 4, 5, 6 }
        };

        foreach (var version in versions)
        {
            var current = DisposableHashSet<int>.Create(version);

            packer.ResetPositionAndMode(false);
            DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);

            packer.ResetPositionAndMode(true);

            var result = default(DisposableHashSet<int>);
            DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

            Assert.AreEqual(version.Length, result.Count);
            Assert.IsTrue(result.SetEquals(current));

            result.Dispose();
            current.Dispose();
        }

        old.Dispose();
    }

    [Test]
    public void ReadResultPreAllocated()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4, 5 });

        DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        packer.ResetPositionAndMode(true);

        var result = DisposableHashSet<int>.Create(new[] { 99, 98, 97 });

        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(5, result.Count);
        Assert.IsTrue(result.SetEquals(current));
        Assert.IsFalse(result.Contains(99));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void SameReferenceOldAndNew()
    {
        var set = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, set, set);
        Assert.IsFalse(hasChanged, "Same reference should have no changes");

        set.Dispose();
    }

    [Test]
    public void Strings()
    {
        var old = DisposableHashSet<string>.Create(new[] { "hello", "world" });
        var current = DisposableHashSet<string>.Create(new[] { "hello", "beautiful", "world" });

        bool hasChanged = DeltaPacker<DisposableHashSet<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<string>);
        DeltaPacker<DisposableHashSet<string>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void NoChangeRoundtrip()
    {
        var old = DisposableHashSet<int>.Create(new[] { 10, 20, 30 });
        var current = DisposableHashSet<int>.Create(new[] { 10, 20, 30 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsFalse(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.SetEquals(old));

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void OldDisposedBeforeWrite()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4 });

        old.Dispose();

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var empty = DisposableHashSet<int>.Create();
        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, empty, ref result);

        Assert.AreEqual(4, result.Count);
        Assert.IsTrue(result.SetEquals(current));

        empty.Dispose();
        current.Dispose();
        result.Dispose();
    }
}
