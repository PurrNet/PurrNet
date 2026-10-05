using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using K4os.Compression.LZ4;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using PurrNet.Utils;
using Unity.Collections;
using UnityEngine.TestTools;

public class DeserializationLimitsTests
{
    private int _previousCount;
    private long _previousStorage;
    private BitPacker _writer;

    [SetUp]
    public void Setup()
    {
        _previousCount = DeserializationLimits.maxCollectionLength;
        _previousStorage = DeserializationLimits.maxCollectionStorageBytes;
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
        _writer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        _writer?.Dispose();
        DeserializationLimits.maxCollectionLength = _previousCount;
        DeserializationLimits.maxCollectionStorageBytes = _previousStorage;
    }

    private BitPacker Reader() => BitPackerPool.Get(_writer.ToByteData());

    private static bool ReceiveBigData(SyncBigData sync, byte[] compressed, int declaredLength)
    {
        const int partSize = 768;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int parts = (compressed.Length + partSize - 1) / partSize;
        bool previous = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            var first = new ByteData(compressed, 0, Math.Min(partSize, compressed.Length));
            bool accepted = (bool)typeof(SyncBigData).GetMethod("HandleFirstPart", flags)
                .Invoke(sync, new object[] { new PackedUInt(1), first, parts, declaredLength });
            for (int i = 1; accepted && i < parts; i++)
            {
                var part = new ByteData(compressed, i * partSize, Math.Min(partSize, compressed.Length - i * partSize));
                typeof(SyncBigData).GetMethod("InsertConfirmedPart", flags).Invoke(sync, new object[] { part, i });
            }
            return accepted;
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previous;
        }
    }

    [Test]
    public void SyncBigData_RejectsDeclaredLengthAboveLimit()
    {
        var sync = new SyncBigData { maxSizeMB = 1 };
        Assert.IsFalse(ReceiveBigData(sync, new byte[1], 2 * 1024 * 1024));
        Assert.That(sync.compressedData.Count, Is.Zero);
    }

    [TestCase(1, false)]
    [TestCase(4, true)]
    public void SyncBigData_LimitsUncompressedSize(int maxSizeMB, bool accepted)
    {
        const int length = 2 * 1024 * 1024;
        var sync = new SyncBigData { maxSizeMB = maxSizeMB };
        var compressed = LZ4Pickler.Pickle(new byte[length]);
        Assert.Less(compressed.Length, 1024 * 1024);
        Assert.IsTrue(ReceiveBigData(sync, compressed, compressed.Length));
        Assert.That(sync.data.Count, Is.EqualTo(accepted ? length : 0));
        Assert.That(sync.syncStatus.isDone, Is.EqualTo(accepted));
    }

    [Test]
    public void ExcessiveArrayCount_IsRejectedBeforeAllocation()
    {
        _writer.WriteBit(true);
        _writer.WriteInteger((long)DeserializationLimits.maxCollectionLength + 1, 31);
        using var reader = Reader();
        int[] value = null;
        Assert.Throws<SerializationException>(() => PackCollections.ReadArray(reader, ref value));
        Assert.IsNull(value);
    }

    [Test]
    public void CountLimit_AlsoProtectsReusedLists()
    {
        DeserializationLimits.maxCollectionLength = 4;
        _writer.WriteBit(true);
        _writer.WriteInteger(5, 31);
        using var reader = Reader();
        var value = new List<int> { 42 };
        Assert.Throws<SerializationException>(() => PackCollections.ReadList(reader, ref value));
        CollectionAssert.AreEqual(new[] { 42 }, value);
    }

    [Test]
    public void StorageLimit_UsesElementSize()
    {
        DeserializationLimits.maxCollectionStorageBytes = 16;
        _writer.WriteBit(true);
        _writer.WriteInteger(3, 31);
        using var reader = Reader();
        long[] value = null;
        Assert.Throws<SerializationException>(() => PackCollections.ReadArray(reader, ref value));
        Assert.IsNull(value);
    }

    [Test]
    public void ConfiguredLargerCollection_RoundTrips()
    {
        DeserializationLimits.maxCollectionLength = 8;
        DeserializationLimits.maxCollectionStorageBytes = 64;
        var source = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        PackCollections.WriteList(_writer, source);
        using var reader = Reader();
        int[] value = null;
        PackCollections.ReadArray(reader, ref value);
        CollectionAssert.AreEqual(source, value);
    }

    [Test]
    public void BitPackedEntries_CanOutnumberPacketBytes()
    {
        var source = new bool[64];
        source[63] = true;
        PackCollections.WriteList(_writer, source);
        Assert.Less(_writer.length, source.Length);
        using var reader = Reader();
        bool[] value = null;
        PackCollections.ReadArray(reader, ref value);
        CollectionAssert.AreEqual(source, value);
    }

    private struct ZeroBitItem { }
    private static void ReadZeroBitItem(BitPacker packer, ref ZeroBitItem value) => value = default;

    [Test]
    public void CustomZeroBitElements_RemainSupported()
    {
        var previousReader = Packer<ZeroBitItem>.ReadFunc;
        Packer<ZeroBitItem>.ReadFunc = ReadZeroBitItem;
        try
        {
            _writer.WriteBit(true);
            _writer.WriteInteger(32, 31);
            using var reader = Reader();
            ZeroBitItem[] value = null;
            PackCollections.ReadArray(reader, ref value);
            Assert.AreEqual(32, value.Length);
        }
        finally
        {
            Packer<ZeroBitItem>.ReadFunc = previousReader;
        }
    }

    [Test]
    public void UIntSize_DoesNotWrapBeforeValidation()
    {
        _writer.WriteBit(true);
        Packer<Size>.Write(_writer, uint.MaxValue);
        using var reader = Reader();
        NativeArray<int> value = default;
        Assert.Throws<SerializationException>(() => PackNativeCollections.ReadNativeArray(reader, ref value));
        Assert.IsFalse(value.IsCreated);
    }

    [Test]
    public void TruncatedByteData_IsRejectedBeforeAllocation()
    {
        Packer<Size>.Write(_writer, 64 * 1024 * 1024);
        using var reader = Reader();
        ByteData value = default;
        Assert.Throws<SerializationException>(() => PackByteData.Read(reader, ref value));
        Assert.IsNull(value.data);
    }

    [Test]
    public void TruncatedEncodedString_IsRejectedBeforeRent()
    {
        _writer.WriteBit(true);
        _writer.WriteBits(int.MaxValue, 31);
        using var reader = Reader();
        Assert.Throws<SerializationException>(() => reader.ReadString(Encoding.UTF8));
    }

    [Test]
    public void TruncatedOrdinaryString_IsRejectedBeforeRent()
    {
        _writer.WriteBit(true);
        _writer.Write(65536);
        using var reader = Reader();
        string value = null;
        Assert.Throws<SerializationException>(() => PackStrings.Read(reader, ref value));
        Assert.IsNull(value);
    }

    [Test]
    public void LiteralBytes_AreValidatedAgainstPayloadRatherThanCollectionLimit()
    {
        DeserializationLimits.maxCollectionStorageBytes = 1;
        var source = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        PackByteData.Write(_writer, new ByteData(source, 0, source.Length));
        using var reader = Reader();
        ByteData value = default;
        PackByteData.Read(reader, ref value);
        CollectionAssert.AreEqual(source, value.data);
    }

    [Test]
    public void DeltaOperations_CannotExceedCombinedExpansionLimit()
    {
        DeserializationLimits.maxCollectionLength = 4;
        _writer.WriteBit(true);
        _writer.WriteBit(true);
        using var values = DisposableList<int>.Create(new[] { 1, 2, 3 });
        Packer<DiffOp<int>>.Write(_writer, new DiffOp<int>(OperationType.Add, 0, 3, values));
        Packer<DiffOp<int>>.Write(_writer, new DiffOp<int>(OperationType.Add, 0, 3, values));
        Packer<DiffOp<int>>.Write(_writer, DiffOp<int>.FinalOperation());
        using var reader = Reader();
        DisposableList<int> value = default;
        try
        {
            Assert.Throws<SerializationException>(() => MyersPackDisposableLists.ReadDisposableDeltaList(reader, default, ref value));
        }
        finally
        {
            value.Dispose();
        }
    }

    [Test]
    public void DeletionDelta_RemainsSupported()
    {
        using var baseline = DisposableList<int>.Create(new[] { 1, 2, 3 });
        using var updated = DisposableList<int>.Create(new[] { 1, 3 });
        MyersPackDisposableLists.WriteDisposableDeltaList(_writer, baseline, updated);
        using var reader = Reader();
        DisposableList<int> value = default;
        try
        {
            MyersPackDisposableLists.ReadDisposableDeltaList(reader, baseline, ref value);
            CollectionAssert.AreEqual(new[] { 1, 3 }, value);
        }
        finally
        {
            value.Dispose();
        }
    }

    [Test]
    public void TruncatedNativeArray_ReleasesPartialAllocation()
    {
        _writer.WriteBit(true);
        Packer<Size>.Write(_writer, 2);
        using var reader = Reader();
        NativeArray<int> value = default;
        Assert.Throws<IndexOutOfRangeException>(() => PackNativeCollections.ReadNativeArray(reader, ref value));
        Assert.IsFalse(value.IsCreated);
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void SpawnBatch_RejectsInvalidCountBeforeAllocation(int count)
    {
        Packer<SceneID>.Write(_writer, default);
        Packer<PackedInt>.Write(_writer, count);
        using var reader = Reader();
        SpawnPacketBatch value = default;
        Assert.Throws<SerializationException>(() => SpawnPacketBatchPacker.Unpack(reader, ref value));
        Assert.IsTrue(value.spawnPackets.isDisposed);
    }

    [Test]
    public void OwnershipRle_CanExpandBeyondPacketSize()
    {
        var scope = new PlayerID(1, false);
        using var identities = DisposableList<NetworkID>.Create(4096);
        for (int i = 0; i < 4096; i++)
            identities.Add(new NetworkID((ulong)i, scope));
        var source = new OwnershipChange { identities = identities, player = scope };
        Packer<OwnershipChange>.Write(_writer, source);
        Assert.Less(_writer.length, 4096);
        using var reader = Reader();
        OwnershipChange value = default;
        try
        {
            Packer<OwnershipChange>.Read(reader, ref value);
            Assert.AreEqual(4096, value.identities.Count);
            Assert.AreEqual(identities[4095], value.identities[4095]);
        }
        finally
        {
            value.Dispose();
        }
    }

    [TestCase(0u)]
    [TestCase(5u)]
    public void OwnershipRle_RejectsZeroOrOversizedRun(uint runLength)
    {
        Packer<SceneID>.Write(_writer, default);
        Packer<bool>.Write(_writer, false);
        Packer<PlayerID>.Write(_writer, PlayerID.Server);
        Packer<bool>.Write(_writer, false);
        _writer.WriteBit(true);
        Packer<Size>.Write(_writer, 4);
        _writer.WriteBit(true);
        Packer<PlayerID>.Write(_writer, PlayerID.Server);
        Packer<PackedULong>.Write(_writer, 0);
        Packer<Size>.Write(_writer, runLength);
        using var reader = Reader();
        OwnershipChange value = default;
        try
        {
            Assert.Throws<SerializationException>(() => Packer<OwnershipChange>.Read(reader, ref value));
        }
        finally
        {
            value.Dispose();
        }
    }
}
