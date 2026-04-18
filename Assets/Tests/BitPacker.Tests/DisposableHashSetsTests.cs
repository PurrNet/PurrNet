using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

public class DisposableHashSetsTests
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
    public void DefaultIsDisposed()
    {
        DisposableHashSet<int> set = default;
        Assert.IsTrue(set.isDisposed);
    }

    [Test]
    public void CreateIsNotDisposed()
    {
        var set = DisposableHashSet<int>.Create();
        Assert.IsFalse(set.isDisposed);
        set.Dispose();
    }

    [Test]
    public void DisposeDefaultIsNoop()
    {
        DisposableHashSet<int> set = default;
        Assert.DoesNotThrow(() => set.Dispose());
        Assert.IsTrue(set.isDisposed);
    }

    [Test]
    public void TestDuplicate()
    {
        var set = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        var copy = PurrCopy<DisposableHashSet<int>>.Copy(set);
        var areEqual = PurrEquality<DisposableHashSet<int>>.Equals(set, copy);

        Assert.IsTrue(areEqual, "Sets should be equal");

        set.Dispose();
        copy.Dispose();
    }

    [Test]
    public void TestDeltaNoChanges()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        bool hasChangedEquality = PurrEquality<DisposableHashSet<int>>.Equals(old, current);
        Assert.IsFalse(hasChanged, "Sets should be equal");
        Assert.IsTrue(hasChangedEquality, "Sets should be equal");

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void TestDeltaWithChanges()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var current = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        bool hasChangedEquality = PurrEquality<DisposableHashSet<int>>.Equals(old, current);
        Assert.IsTrue(hasChanged, "Sets should not be equal");
        Assert.IsFalse(hasChangedEquality, "Sets should not be equal");

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void TestDeltaSameSize()
    {
        var old = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4, 5 });
        var current = DisposableHashSet<int>.Create(new[] { 2, 4, 6, 8, 10 });

        bool hasChanged = DeltaPacker<DisposableHashSet<int>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged, "Sets should not be equal");

        Debug.Log("Written bits: " + packer.positionInBits);
        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        DeltaPacker<DisposableHashSet<int>>.Read(packer, old, ref result);

        Assert.AreEqual(5, result.Count, "Read set should have the same count");
        Assert.IsTrue(result.SetEquals(current), "Read set should contain the same elements");

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void InsertionOrderPreserved()
    {
        var set = DisposableHashSet<int>.Create();
        set.Add(3);
        set.Add(1);
        set.Add(2);

        var items = new int[3];
        int i = 0;
        foreach (var item in set)
            items[i++] = item;

        Assert.AreEqual(3, items[0]);
        Assert.AreEqual(1, items[1]);
        Assert.AreEqual(2, items[2]);

        set.Dispose();
    }

    [Test]
    public void RemovePreservesOrder()
    {
        var set = DisposableHashSet<int>.Create(new[] { 1, 2, 3, 4, 5 });
        set.Remove(3);

        var items = new int[4];
        int i = 0;
        foreach (var item in set)
            items[i++] = item;

        Assert.AreEqual(1, items[0]);
        Assert.AreEqual(2, items[1]);
        Assert.AreEqual(4, items[2]);
        Assert.AreEqual(5, items[3]);

        set.Dispose();
    }

    [Test]
    public void FullPackerRoundtrip()
    {
        var original = DisposableHashSet<int>.Create(new[] { 10, 20, 30 });

        Packer<DisposableHashSet<int>>.Write(packer, original);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableHashSet<int>);
        Packer<DisposableHashSet<int>>.Read(packer, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.SetEquals(original));

        original.Dispose();
        result.Dispose();
    }

    [Test]
    public void FullPackerDisposed()
    {
        DisposableHashSet<int> original = default;

        Packer<DisposableHashSet<int>>.Write(packer, original);

        packer.ResetPositionAndMode(true);

        var result = DisposableHashSet<int>.Create(new[] { 99 });
        Packer<DisposableHashSet<int>>.Read(packer, ref result);

        Assert.IsTrue(result.isDisposed);
    }
}
