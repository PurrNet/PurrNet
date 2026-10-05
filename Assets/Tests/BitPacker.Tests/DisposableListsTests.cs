using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

public class DisposableListsTests
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
    public void TestDeltaDisposableList()
    {
        var oldList = DisposableList<int>.Create(5);
        var newList = DisposableList<int>.Create(5);

        for (int i = 0; i < 5; i++)
        {
            oldList.Add(i);
            newList.Add(i);
        }

        // Test with the same list
        bool hasChanged = packer.WriteDisposableDeltaList(oldList, newList);
        bool hasChangedEquality = PurrEquality<DisposableList<int>>.Equals(oldList, newList);
        Assert.IsFalse(hasChanged, "Lists should be equal");
        Assert.IsTrue(hasChangedEquality, "Lists should be equal");

        // Modify the new list
        newList[0] = 10;

        // Test with different lists
        hasChanged = packer.WriteDisposableDeltaList(oldList, newList);
        hasChangedEquality = PurrEquality<DisposableList<int>>.Equals(oldList, newList);
        Assert.IsTrue(hasChanged, "Lists should not be equal");
        Assert.IsFalse(hasChangedEquality, "Lists should not be equal");
    }

    [Test]
    public void TestDuplicate()
    {
        var list = DisposableList<int>.Create(5);

        for (int i = 0; i < 5; i++)
            list.Add(i);

        var copy = PurrCopy<DisposableList<int>>.Copy(list);
        var areEqual = PurrEquality<DisposableList<int>>.Equals(list, copy);

        Assert.IsTrue(areEqual, "Lists should be equal");
    }

    [Test]
    public void DisposableList_ConcreteEnumerator_WalksItems()
    {
        var list = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            var enumerator = list.GetEnumerator();
            Assert.AreEqual(typeof(List<int>.Enumerator), enumerator.GetType());

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(1, enumerator.Current);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(2, enumerator.Current);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(3, enumerator.Current);
            Assert.IsFalse(enumerator.MoveNext());
        }
        finally
        {
            list.Dispose();
        }
    }

    [Test]
    public void DisposableArray_ConcreteEnumerator_WalksLogicalItemsOnly()
    {
        var array = DisposableArray<int>.Create(new[] { 7, 8 });
        try
        {
            var values = new List<int>();
            foreach (var item in array)
                values.Add(item);

            CollectionAssert.AreEqual(new[] { 7, 8 }, values);
        }
        finally
        {
            array.Dispose();
        }
    }

    [Test]
    public void DisposableArray_Contains_IgnoresRentedTail()
    {
        var array = DisposableArray<int>.Create(new[] { 1, 2 });
        try
        {
            Assert.IsFalse(array.Contains(0));
        }
        finally
        {
            array.Dispose();
        }
    }

    [Test]
    public void DisposableArray_CopyTo_HonorsArrayIndex()
    {
        var array = DisposableArray<int>.Create(new[] { 4, 5 });
        try
        {
            var destination = new[] { -1, -1, -1, -1 };
            array.CopyTo(destination, 1);
            CollectionAssert.AreEqual(new[] { -1, 4, 5, -1 }, destination);
        }
        finally
        {
            array.Dispose();
        }
    }

    [Test]
    public void SyncList_ConcreteEnumerator_WalksItems()
    {
        var syncList = new SyncList<int>(new List<int> { 4, 5, 6 });
        var enumerator = syncList.GetEnumerator();
        Assert.AreEqual(typeof(List<int>.Enumerator), enumerator.GetType());

        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual(4, enumerator.Current);
        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual(5, enumerator.Current);
        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual(6, enumerator.Current);
        Assert.IsFalse(enumerator.MoveNext());
    }

    [Test]
    public void CopiedHandleIsDisposedWithOriginal()
    {
        var list = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var copy = list;

        list.Dispose();

        Assert.IsTrue(copy.isDisposed);
        Assert.IsNull(copy.list);
        Assert.Throws<ObjectDisposedException>(() => _ = copy.Count);
        Assert.DoesNotThrow(() => copy.Dispose());
    }

    [Test]
    public void CopiedArrayHandleIsDisposedWithOriginal()
    {
        var array = DisposableArray<string>.Create(new[] { "a", "b" });
        var copy = array;

        array.Dispose();

        Assert.IsTrue(copy.isDisposed);
        Assert.IsNull(copy.array);
        Assert.Throws<ObjectDisposedException>(() => _ = copy[0]);
        Assert.DoesNotThrow(() => copy.Dispose());
    }


    [Test]
    public void TestDeltaSameLength()
    {
        var old = DisposableList<int>.Create(5);
        var @new = DisposableList<int>.Create(5);

        for (int i = 0; i < 5; i++)
        {
            old.Add(i);
            @new.Add(i * 2);
        }

        bool hasChanged = packer.WriteDisposableDeltaList(old, @new);
        Assert.IsTrue(hasChanged, "Lists should not be equal");

        Debug.Log("Written bits: " + packer.positionInBits);
        packer.ResetPositionAndMode(true);

        var readList = default(DisposableList<int>);
        packer.ReadDisposableDeltaList(old, ref readList);

        Assert.AreEqual(5, readList.Count, "Read list should have the same count");

        for (int i = 0; i < 5; i++)
            Assert.AreEqual(i * 2, readList[i], $"Read list item {i} should be equal to {@new[i]}");
    }

    [Test]
    public void AddRangeFromDisposableListAppendsItsItems()
    {
        var target = DisposableList<int>.Create(new[] { 1, 2 });
        var source = DisposableList<int>.Create(new[] { 3, 4 });

        target.AddRange(source);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, target);
        CollectionAssert.AreEqual(new[] { 3, 4 }, source);
        target.Dispose();
        source.Dispose();
    }

    [Test]
    public void AddRangeFromItselfAppendsItsOriginalItems()
    {
        var list = DisposableList<int>.Create(new[] { 1, 2 });

        list.AddRange(list);

        CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, list);
        list.Dispose();
    }

    [Test]
    public void InsertRangeFromDisposableListInsertsAtTheIndex()
    {
        var target = DisposableList<int>.Create(new[] { 1, 4 });
        var source = DisposableList<int>.Create(new[] { 2, 3 });

        target.InsertRange(1, source);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, target);
        target.Dispose();
        source.Dispose();
    }

    [Test]
    public void RangeOverloadsRejectADisposedSource()
    {
        var target = DisposableList<int>.Create(new[] { 1 });
        var source = DisposableList<int>.Create(new[] { 2 });
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => target.AddRange(source));
        Assert.Throws<ObjectDisposedException>(() => target.InsertRange(0, source));
        CollectionAssert.AreEqual(new[] { 1 }, target);
        target.Dispose();
    }

    [Test]
    public void ContinueDisposableReplacesAnUnchangedValueWithACopyOfTheBaseline()
    {
        var old = DisposableList<int>.Create(new[] { 5, 6 });
        var value = DisposableList<int>.Create(new[] { 9 });
        var previous = value;

        packer.WriteBit(false);
        packer.ResetPositionAndMode(true);

        Assert.IsFalse(DeltaReadingScope.ContinueDisposable(packer, old, ref value));
        Assert.IsTrue(previous.isDisposed, "the overwritten value must be disposed");
        CollectionAssert.AreEqual(new[] { 5, 6 }, value);
        Assert.AreNotSame(old.list, value.list, "the baseline must be copied, not shared");
        old.Dispose();
        value.Dispose();
    }

    [Test]
    public void ContinueDisposableLeavesAChangedValueToTheReader()
    {
        var old = DisposableList<int>.Create(new[] { 5 });
        var value = DisposableList<int>.Create(new[] { 9 });

        packer.WriteBit(true);
        packer.ResetPositionAndMode(true);

        Assert.IsTrue(DeltaReadingScope.ContinueDisposable(packer, old, ref value));
        CollectionAssert.AreEqual(new[] { 9 }, value);
        old.Dispose();
        value.Dispose();
    }
}
