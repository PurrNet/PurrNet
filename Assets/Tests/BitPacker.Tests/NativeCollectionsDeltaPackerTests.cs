using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Utils;
using Unity.Collections;
using UnityEngine;

public class NativeCollectionsDeltaPackerTests
{
    private BitPacker packer;
    // Use Persistent so allocations stay valid; Allocator.Temp can cause crashes in test runner.
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

    static NativeList<int> ToNativeList(int[] data, Allocator allocator)
    {
        var list = new NativeList<int>(data?.Length ?? 0, allocator);
        if (data != null)
            for (int i = 0; i < data.Length; i++)
                list.Add(data[i]);
        return list;
    }

    static bool SharesAllocation<T>(NativeList<T> left, NativeList<T> right) where T : unmanaged
    {
        return left.IsCreated && right.IsCreated && left.AsArray().Equals(right.AsArray());
    }

    static NativeArray<int> ToNativeArray(int[] data, Allocator allocator)
    {
        if (data == null || data.Length == 0)
            return default;
        var arr = new NativeArray<int>(data.Length, allocator);
        for (int i = 0; i < data.Length; i++)
            arr[i] = data[i];
        return arr;
    }

    static void AssertListEquals(NativeList<int> list, int[] expected)
    {
        Assert.IsTrue(list.IsCreated);
        Assert.AreEqual(expected?.Length ?? 0, list.Length);
        if (expected != null)
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], list[i], $"Index {i}");
    }

    static void AssertArrayEquals(NativeArray<int> arr, int[] expected)
    {
        if (expected == null || expected.Length == 0)
        {
            Assert.IsFalse(arr.IsCreated);
            return;
        }
        Assert.IsTrue(arr.IsCreated);
        Assert.AreEqual(expected.Length, arr.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], arr[i], $"Index {i}");
    }

    // ---------- NativeList delta ----------

    [Test]
    public void NativeList_SameListNoChanges()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsFalse(hasChanged);
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_FloatElementsPreserveSignedZeroAndNaNPayloadBits()
    {
        PackCollections.RegisterNativeList<float>();
        float negativeZero = System.BitConverter.Int32BitsToSingle(unchecked((int)0x80000000u));
        float oldNaN = System.BitConverter.Int32BitsToSingle(unchecked((int)0x7FC00001u));
        float newNaN = System.BitConverter.Int32BitsToSingle(unchecked((int)0x7FC01234u));
        var old = new NativeList<float>(2, TestAllocator);
        old.Add(0f);
        old.Add(oldNaN);
        var current = new NativeList<float>(2, TestAllocator);
        current.Add(negativeZero);
        current.Add(newNaN);
        var result = default(NativeList<float>);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<float>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeList<float>>.Read(packer, old, ref result);

            Assert.AreEqual(System.BitConverter.SingleToInt32Bits(negativeZero),
                System.BitConverter.SingleToInt32Bits(result[0]));
            Assert.AreEqual(System.BitConverter.SingleToInt32Bits(newNaN),
                System.BitConverter.SingleToInt32Bits(result[1]));
        }
        finally
        {
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
            if (result.IsCreated) result.Dispose();
        }
    }

    [Test]
    public void NativeList_UnchangedReadIntoAliasedDestinationPreservesBaseline()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var result = old;
        bool decoded = false;
        bool resultAliasesOld = true;
        try
        {
            DeltaPacker<NativeList<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);
            decoded = true;
            resultAliasesOld = SharesAllocation(old, result);

            result.Add(4);
            AssertListEquals(old, new[] { 1, 2, 3 });
            AssertListEquals(result, new[] { 1, 2, 3, 4 });
        }
        finally
        {
            if (decoded && result.IsCreated && !resultAliasesOld) result.Dispose();
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeList_ChangedReadIntoAliasedDestinationPreservesBaseline()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 1, 2, 3, 4 }, TestAllocator);
        var result = old;
        bool decoded = false;
        bool resultAliasesOld = true;
        try
        {
            DeltaPacker<NativeList<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);
            decoded = true;
            resultAliasesOld = SharesAllocation(old, result);

            AssertListEquals(old, new[] { 1, 2, 3 });
            AssertListEquals(result, new[] { 1, 2, 3, 4 });
        }
        finally
        {
            if (decoded && result.IsCreated && !resultAliasesOld) result.Dispose();
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeList_NotCreatedReadIntoAliasedDestinationPreservesBaseline()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = default(NativeList<int>);
        var result = old;
        try
        {
            DeltaPacker<NativeList<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            Assert.IsFalse(result.IsCreated);
            AssertListEquals(old, new[] { 1, 2, 3 });
        }
        finally
        {
            if (old.IsCreated) old.Dispose();
        }
    }

    [Test]
    public void NativeList_EmptyToItems()
    {
        var old = ToNativeList(System.Array.Empty<int>(), TestAllocator);
        var current = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeList<int>);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            AssertListEquals(result, new[] { 1, 2, 3 });
            result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_ItemsToEmpty()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(System.Array.Empty<int>(), TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = ToNativeList(new[] { 99, 99 }, TestAllocator);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            Assert.AreEqual(0, result.Length);
            result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_AppendSingle()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 1, 2, 3, 4 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeList<int>);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            AssertListEquals(result, new[] { 1, 2, 3, 4 });
            result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_DeleteMiddle()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 1, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeList<int>);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            AssertListEquals(result, new[] { 1, 3 });
            result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_InsertMiddle()
    {
        var old = ToNativeList(new[] { 1, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeList<int>);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            AssertListEquals(result, new[] { 1, 2, 3 });
            result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_CompleteReplacement()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeList(new[] { 4, 5, 6 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeList<int>);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            AssertListEquals(result, new[] { 4, 5, 6 });
            result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_NotCreated_ToItems()
    {
        var old = default(NativeList<int>);
        var current = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeList<int>);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            AssertListEquals(result, new[] { 1, 2, 3 });
            result.Dispose();
        }
        finally
        {
            current.Dispose();
        }
    }

    [Test]
    public void NativeList_ItemsToNotCreated()
    {
        var old = ToNativeList(new[] { 1, 2, 3 }, TestAllocator);
        var current = default(NativeList<int>);
        try
        {
            bool hasChanged = DeltaPacker<NativeList<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = ToNativeList(new[] { 99 }, TestAllocator);
            DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

            Assert.IsFalse(result.IsCreated);
        }
        finally
        {
            old.Dispose();
        }
    }

    [Test]
    public void NativeList_MultipleRoundtrips()
    {
        var versions = new[]
        {
            new[] { 1, 2, 3 },
            new[] { 1, 2, 3, 4 },
            new[] { 1, 3, 4 },
            new[] { 0, 1, 3, 4, 5 },
            new[] { 0, 1 }
        };

        var old = ToNativeList(versions[0], TestAllocator);
        try
        {
            for (int i = 1; i < versions.Length; i++)
            {
                var current = ToNativeList(versions[i], TestAllocator);
                try
                {
                    packer.ResetPositionAndMode(false);
                    DeltaPacker<NativeList<int>>.Write(packer, old, current);

                    packer.ResetPositionAndMode(true);
                    var result = default(NativeList<int>);
                    DeltaPacker<NativeList<int>>.Read(packer, old, ref result);

                    AssertListEquals(result, versions[i]);

                    old.Dispose();
                    old = result;
                }
                finally
                {
                    current.Dispose();
                }
            }
        }
        finally
        {
            old.Dispose();
        }
    }

    // ---------- NativeArray delta (Myers via list conversion) ----------

    [Test]
    public void NativeArray_SameArrayNoChanges()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            Assert.IsFalse(hasChanged);
        }
        finally
        {
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeArray_ChangedReadIntoAliasedDestinationPreservesBaseline()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 4, 5, 6 }, TestAllocator);
        var result = old;
        try
        {
            DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            AssertArrayEquals(old, new[] { 1, 2, 3 });
            AssertArrayEquals(result, new[] { 4, 5, 6 });
            Assert.IsFalse(old.Equals(result));
        }
        finally
        {
            bool aliasesOld = old.IsCreated && result.IsCreated && old.Equals(result);
            if (result.IsCreated && !aliasesOld) result.Dispose();
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeArray_UnchangedReadIntoAliasedDestinationReturnsIndependentCopy()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var result = old;
        try
        {
            DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            Assert.IsFalse(old.Equals(result));
            result[0] = 99;
            AssertArrayEquals(old, new[] { 1, 2, 3 });
            AssertArrayEquals(result, new[] { 99, 2, 3 });
        }
        finally
        {
            bool aliasesOld = old.IsCreated && result.IsCreated && old.Equals(result);
            if (result.IsCreated && !aliasesOld) result.Dispose();
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeArray_LengthChangedReadIntoAliasedDestinationPreservesBaseline()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 1, 2, 3, 4 }, TestAllocator);
        var result = old;
        try
        {
            DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            AssertArrayEquals(old, new[] { 1, 2, 3 });
            AssertArrayEquals(result, new[] { 1, 2, 3, 4 });
            Assert.IsFalse(old.Equals(result));
        }
        finally
        {
            bool aliasesOld = old.IsCreated && result.IsCreated && old.Equals(result);
            if (result.IsCreated && !aliasesOld) result.Dispose();
            if (old.IsCreated) old.Dispose();
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeArray_NotCreatedReadIntoAliasedDestinationPreservesBaseline()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = default(NativeArray<int>);
        var result = old;
        try
        {
            DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            Assert.IsFalse(result.IsCreated);
            AssertArrayEquals(old, new[] { 1, 2, 3 });
        }
        finally
        {
            if (old.IsCreated) old.Dispose();
        }
    }

    [Test]
    public void NativeArray_EmptyToItems()
    {
        var old = default(NativeArray<int>);
        var current = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeArray<int>);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            AssertArrayEquals(result, new[] { 1, 2, 3 });
            if (result.IsCreated) result.Dispose();
        }
        finally
        {
            if (current.IsCreated) current.Dispose();
        }
    }

    [Test]
    public void NativeArray_ItemsToEmpty()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = default(NativeArray<int>);
        try
        {
            bool hasChanged = DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeArray<int>);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            Assert.IsFalse(result.IsCreated);
        }
        finally
        {
            old.Dispose();
        }
    }

    [Test]
    public void NativeArray_AppendSingle()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 1, 2, 3, 4 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeArray<int>);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            AssertArrayEquals(result, new[] { 1, 2, 3, 4 });
            if (result.IsCreated) result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeArray_DeleteMiddle()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 1, 3 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeArray<int>);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            AssertArrayEquals(result, new[] { 1, 3 });
            if (result.IsCreated) result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }

    [Test]
    public void NativeArray_CompleteReplacement()
    {
        var old = ToNativeArray(new[] { 1, 2, 3 }, TestAllocator);
        var current = ToNativeArray(new[] { 4, 5, 6 }, TestAllocator);
        try
        {
            bool hasChanged = DeltaPacker<NativeArray<int>>.Write(packer, old, current);
            Assert.IsTrue(hasChanged);

            packer.ResetPositionAndMode(true);
            var result = default(NativeArray<int>);
            DeltaPacker<NativeArray<int>>.Read(packer, old, ref result);

            AssertArrayEquals(result, new[] { 4, 5, 6 });
            if (result.IsCreated) result.Dispose();
        }
        finally
        {
            old.Dispose();
            current.Dispose();
        }
    }
}
