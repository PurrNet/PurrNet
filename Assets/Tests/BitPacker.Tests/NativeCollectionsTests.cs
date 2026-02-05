using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Utils;
using Unity.Collections;
using UnityEngine;

public class NativeCollectionsTests
{
    private BitPacker packer;
    // Use Persistent so allocations are valid for the whole test; Temp can be invalidated in test runner.
    private const Allocator TestAllocator = Allocator.Persistent;

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
        PackCollections.RegisterNativeArray<int>();
        PackCollections.RegisterNativeList<int>();
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void NativeArray_WriteRead_SameContent()
    {
        var arr = new NativeArray<int>(4, TestAllocator);
        arr[0] = 10;
        arr[1] = 20;
        arr[2] = 30;
        arr[3] = 40;
        try
        {
            Packer<NativeArray<int>>.Write(packer, arr);
            packer.ResetPositionAndMode(true);

            var read = default(NativeArray<int>);
            Packer<NativeArray<int>>.Read(packer, ref read);

            Assert.IsTrue(read.IsCreated);
            Assert.AreEqual(4, read.Length);
            Assert.AreEqual(10, read[0]);
            Assert.AreEqual(20, read[1]);
            Assert.AreEqual(30, read[2]);
            Assert.AreEqual(40, read[3]);

            if (read.IsCreated)
                read.Dispose();
        }
        finally
        {
            arr.Dispose();
        }
    }

    [Test]
    public void NativeArray_WriteRead_NotCreated()
    {
        var arr = default(NativeArray<int>);
        Packer<NativeArray<int>>.Write(packer, arr);
        packer.ResetPositionAndMode(true);

        var read = new NativeArray<int>(1, TestAllocator);
        try
        {
            Packer<NativeArray<int>>.Read(packer, ref read);
            Assert.IsFalse(read.IsCreated);
        }
        finally
        {
            if (read.IsCreated)
                read.Dispose();
        }
    }

    [Test]
    public void NativeList_WriteRead_SameContent()
    {
        var list = new NativeList<int>(4, TestAllocator);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        list.Add(4);
        try
        {
            Packer<NativeList<int>>.Write(packer, list);
            packer.ResetPositionAndMode(true);

            var read = default(NativeList<int>);
            Packer<NativeList<int>>.Read(packer, ref read);

            Assert.IsTrue(read.IsCreated);
            Assert.AreEqual(4, read.Length);
            Assert.AreEqual(1, read[0]);
            Assert.AreEqual(2, read[1]);
            Assert.AreEqual(3, read[2]);
            Assert.AreEqual(4, read[3]);

            if (read.IsCreated)
                read.Dispose();
        }
        finally
        {
            list.Dispose();
        }
    }

    [Test]
    public void NativeList_WriteRead_NotCreated()
    {
        var list = default(NativeList<int>);
        Packer<NativeList<int>>.Write(packer, list);
        packer.ResetPositionAndMode(true);

        var read = new NativeList<int>(TestAllocator);
        try
        {
            Packer<NativeList<int>>.Read(packer, ref read);
            Assert.IsFalse(read.IsCreated);
        }
        finally
        {
            if (read.IsCreated)
                read.Dispose();
        }
    }

    [Test]
    public void NativeList_WriteRead_Empty()
    {
        var list = new NativeList<int>(0, TestAllocator);
        try
        {
            Packer<NativeList<int>>.Write(packer, list);
            packer.ResetPositionAndMode(true);

            var read = default(NativeList<int>);
            Packer<NativeList<int>>.Read(packer, ref read);

            Assert.IsTrue(read.IsCreated);
            Assert.AreEqual(0, read.Length);

            if (read.IsCreated)
                read.Dispose();
        }
        finally
        {
            list.Dispose();
        }
    }
}
