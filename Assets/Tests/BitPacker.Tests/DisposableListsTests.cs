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
}
