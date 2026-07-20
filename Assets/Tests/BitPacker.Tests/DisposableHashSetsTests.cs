using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Collections;
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
    public void DisposableHashSet_ConcreteEnumerator_WalksStoredOrder()
    {
        var set = DisposableHashSet<int>.Create(new[] { 3, 1, 2 });
        try
        {
            var enumerator = set.GetEnumerator();
            Assert.AreEqual(typeof(DisposableHashSet<int>.Enumerator), enumerator.GetType());

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(3, enumerator.Current);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(1, enumerator.Current);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(2, enumerator.Current);
            Assert.IsFalse(enumerator.MoveNext());
        }
        finally
        {
            set.Dispose();
        }
    }

    [Test]
    public void DisposableDictionary_ConcreteEnumerator_WalksKeysInInsertionOrder()
    {
        var dictionary = DisposableDictionary<int, string>.Create();
        try
        {
            dictionary.Add(2, "two");
            dictionary.Add(1, "one");

            var enumerator = dictionary.GetEnumerator();
            Assert.AreEqual(typeof(DisposableDictionary<int, string>.Enumerator), enumerator.GetType());

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(2, enumerator.Current.Key);
            Assert.AreEqual("two", enumerator.Current.Value);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(1, enumerator.Current.Key);
            Assert.AreEqual("one", enumerator.Current.Value);
            Assert.IsFalse(enumerator.MoveNext());
        }
        finally
        {
            dictionary.Dispose();
        }
    }

    [Test]
    public void PurrHashSet_ConcreteEnumerator_ReturnsHashSetEnumerator()
    {
        var set = new PurrHashSet<int>();
        set.Add(4);
        set.Add(5);

        var enumerator = set.GetEnumerator();
        Assert.AreEqual(typeof(HashSet<int>.Enumerator), enumerator.GetType());

        var values = new HashSet<int>();
        while (enumerator.MoveNext())
            values.Add(enumerator.Current);

        CollectionAssert.AreEquivalent(new[] { 4, 5 }, values);
    }

    [Test]
    public void CopiedHandleIsDisposedWithOriginal()
    {
        var set = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var copy = set;

        set.Dispose();

        Assert.IsTrue(copy.isDisposed);
        Assert.IsNull(copy.set);
        Assert.Throws<ObjectDisposedException>(() => _ = copy.Count);
        Assert.DoesNotThrow(() => copy.Dispose());
    }

    [Test]
    public void CopiedDictionaryHandleIsDisposedWithOriginal()
    {
        var dictionary = DisposableDictionary<int, string>.Create();
        dictionary.Add(1, "one");
        var copy = dictionary;

        dictionary.Dispose();

        Assert.IsTrue(copy.isDisposed);
        Assert.IsNull(copy.dictionary);
        Assert.Throws<ObjectDisposedException>(() => _ = copy.Count);
        Assert.DoesNotThrow(() => copy.Dispose());
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
