using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DeltaParityAutoClass : IPackedAuto
{
    public int number;
    public string text;
}

public sealed class DeltaParityUnmanagedArrayField : IPackedAuto
{
    public int[] values;
}

public struct DeltaParityManagedStructWithArray : IPackedAuto
{
    public int number;
    public int[] values;
}

[RegisterNetworkType(typeof(DeltaParityDerivedA))]
[RegisterNetworkType(typeof(DeltaParityDerivedB))]
public class DeltaParityBase : IPackedAuto
{
    public int baseValue;
}

public sealed class DeltaParityDerivedA : DeltaParityBase
{
    public string a;
}

public sealed class DeltaParityDerivedB : DeltaParityBase
{
    public Vector3 b;
}

[DontPack]
public sealed class DeltaParityUnregisteredDerived : DeltaParityBase, IDisposable
{
    public bool disposed { get; private set; }

    public void Dispose()
    {
        disposed = true;
    }
}

public sealed class DeltaParityCustomPacked : IPacked
{
    public int serializedValue;
    public int localOnlyValue;

    public void Write(BitPacker packer)
    {
        Packer<int>.Write(packer, serializedValue);
    }

    public void Read(BitPacker packer)
    {
        Packer<int>.Read(packer, ref serializedValue);
    }
}

[RegisterNetworkType(typeof(DeltaParityInterfaceValue))]
public sealed class DeltaParityRegistrationAnchor
{
}

public interface IDeltaParityContract
{
    int value { get; }
}

public sealed class DeltaParityInterfaceValue : IDeltaParityContract, IPackedAuto
{
    public int serializedValue;
    public int value => serializedValue;
}

public class DeltaPackerParityTests
{
    private BitPacker _packer;

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();

        // Keep the collection portion of this suite independent of codegen discovery.
        PackCollections.RegisterArray<int>();
        PackCollections.RegisterList<int>();
        PackCollections.RegisterNullable<int>();
        PackCollections.RegisterHashSet<int>();
        PackCollections.RegisterDictionary<int, string>();
        PackCollections.RegisterQueue<int>();
        PackCollections.RegisterStack<int>();
        PackCollections.RegisterNativeArray<int>();
        PackCollections.RegisterDisposableDictionary<int, string>();
        PackCollections.RegisterDisposableHashSet<int>();
        PackCollections.RegisterDisposableArray<int>();

        _packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        _packer?.Dispose();
    }

    [Test]
    public void PrimitivePackerTypes_ChangedValuesRoundTripThroughDeltaPacker()
    {
        AssertChangedRoundTrip(false, true);
        AssertChangedRoundTrip((byte)1, byte.MaxValue);
        AssertChangedRoundTrip((sbyte)-1, sbyte.MaxValue);
        AssertChangedRoundTrip((short)-1234, short.MaxValue);
        AssertChangedRoundTrip((ushort)1234, ushort.MaxValue);
        AssertChangedRoundTrip(-123456, int.MaxValue);
        AssertChangedRoundTrip(123456u, uint.MaxValue);
        AssertChangedRoundTrip(-1234567890123L, long.MaxValue);
        AssertChangedRoundTrip(1234567890123UL, ulong.MaxValue);
        AssertChangedRoundTrip('a', '\u263A');
        AssertChangedRoundTrip(-123.5f, 987.25f);
        AssertChangedRoundTrip(-123.5d, 987.25d);
        AssertChangedRoundTrip(TimeSpan.FromSeconds(1), TimeSpan.FromDays(3));
        AssertChangedRoundTrip(new DateTime(100), new DateTime(9876543210));
    }

    [Test]
    public void PrimitivePackerTypes_UnchangedValuesRoundTripThroughDeltaPacker()
    {
        AssertUnchangedRoundTrip(true);
        AssertUnchangedRoundTrip((byte)42);
        AssertUnchangedRoundTrip((sbyte)-42);
        AssertUnchangedRoundTrip((short)-1234);
        AssertUnchangedRoundTrip((ushort)1234);
        AssertUnchangedRoundTrip(-123456);
        AssertUnchangedRoundTrip(123456u);
        AssertUnchangedRoundTrip(-1234567890123L);
        AssertUnchangedRoundTrip(1234567890123UL);
        AssertUnchangedRoundTrip('\u263A');
        AssertUnchangedRoundTrip(-123.5f);
        AssertUnchangedRoundTrip(-123.5d);
        AssertUnchangedRoundTrip(TimeSpan.FromDays(3));
        AssertUnchangedRoundTrip(new DateTime(9876543210));
    }

    [Test]
    public void OptimizedPackerTypes_RoundTripThroughDeltaPacker()
    {
        AssertChangedRoundTrip(new PackedByte(1), new PackedByte(byte.MaxValue));
        AssertChangedRoundTrip(new PackedSByte(-1), new PackedSByte(sbyte.MaxValue));
        AssertChangedRoundTrip(new PackedShort(-123), new PackedShort(short.MaxValue));
        AssertChangedRoundTrip(new PackedUShort(123), new PackedUShort(ushort.MaxValue));
        AssertChangedRoundTrip(new PackedInt(-123456), new PackedInt(int.MaxValue));
        AssertChangedRoundTrip(new PackedUInt(123456), new PackedUInt(uint.MaxValue));
        AssertChangedRoundTrip(new PackedLong(-1234567890123), new PackedLong(long.MaxValue));
        AssertChangedRoundTrip(new PackedULong(1234567890123), new PackedULong(ulong.MaxValue));
        AssertChangedRoundTrip(new Size(12), new Size(345678));
        AssertChangedRoundTrip(new PurrNet.Packing.Half(-1.5f), new PurrNet.Packing.Half(23.25f));
        AssertChangedRoundTrip(new CompressedFloat(-1.5f), new CompressedFloat(23.25f));
    }

    [Test]
    public void UnityPackerTypes_RoundTripThroughDeltaPacker()
    {
        AssertChangedRoundTrip(new Vector2(-1.5f, 2.5f), new Vector2(30.25f, -40.5f));
        AssertChangedRoundTrip(new Vector3(-1, 2, -3), new Vector3(30, -40, 50));
        AssertChangedRoundTrip(new Vector4(-1, 2, -3, 4), new Vector4(30, -40, 50, -60));
        AssertChangedRoundTrip(Quaternion.Euler(1, 2, 3), Quaternion.Euler(40, 50, 60));
        AssertChangedRoundTrip(new Vector2Int(-1, 2), new Vector2Int(30, -40));
        AssertChangedRoundTrip(new Vector3Int(-1, 2, -3), new Vector3Int(30, -40, 50));
        AssertChangedRoundTrip(
            new Pose(new Vector3(1, 2, 3), Quaternion.Euler(4, 5, 6)),
            new Pose(new Vector3(10, 20, 30), Quaternion.Euler(40, 50, 60)));
        AssertChangedRoundTrip(new Color32(1, 2, 3, 4), new Color32(100, 110, 120, 130));
        AssertChangedRoundTrip(new Color(0.1f, 0.2f, 0.3f, 0.4f), new Color(0.6f, 0.7f, 0.8f, 0.9f));
        AssertChangedRoundTrip(new Rect(1, 2, 3, 4), new Rect(10, 20, 30, 40));
        AssertChangedRoundTrip(
            new Bounds(new Vector3(1, 2, 3), new Vector3(4, 5, 6)),
            new Bounds(new Vector3(10, 20, 30), new Vector3(40, 50, 60)));
        AssertChangedRoundTrip(
            new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6)),
            new BoundsInt(new Vector3Int(10, 20, 30), new Vector3Int(40, 50, 60)));
        AssertChangedRoundTrip(
            new Ray(new Vector3(1, 2, 3), Vector3.up),
            new Ray(new Vector3(10, 20, 30), Vector3.right));
        AssertChangedRoundTrip(
            new Ray2D(new Vector2(1, 2), Vector2.up),
            new Ray2D(new Vector2(10, 20), Vector2.right));
        AssertChangedRoundTrip((LayerMask)1, (LayerMask)7);
        AssertChangedRoundTrip(ForceMode.Force, ForceMode.VelocityChange);
        AssertChangedRoundTrip(UnloadSceneOptions.None, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);
        AssertChangedRoundTrip(LoadSceneMode.Single, LoadSceneMode.Additive);
        AssertChangedRoundTrip(LocalPhysicsMode.None, LocalPhysicsMode.Physics3D);
        AssertChangedRoundTrip(
            new LoadSceneParameters(LoadSceneMode.Single, LocalPhysicsMode.None),
            new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D));
    }

    [Test]
    public void GeneratedReferenceType_ValueAndNullTransitionsRoundTripThroughDeltaPacker()
    {
        var oldValue = new DeltaParityAutoClass { number = 1, text = "old" };
        var newValue = new DeltaParityAutoClass { number = 2, text = "new" };
        var expected = ProjectThroughPacker(newValue);

        Assert.That(expected, Is.Not.Null, "The reference type must first be supported by Packer.");

        var result = RoundTrip(oldValue, newValue, true);
        Assert.That(result, Is.Not.SameAs(newValue));
        Assert.That(result, Is.Not.Null);
        Assert.That(result.number, Is.EqualTo(expected.number));
        Assert.That(result.text, Is.EqualTo(expected.text));

        result = RoundTrip<DeltaParityAutoClass>(null, newValue, true);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.number, Is.EqualTo(expected.number));
        Assert.That(result.text, Is.EqualTo(expected.text));

        result = RoundTrip(oldValue, null, true);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GeneratedManagedStruct_PackerCopyDeepCopiesArray()
    {
        var source = new DeltaParityManagedStructWithArray
        {
            number = 7,
            values = new[] { 1, 2, 3 }
        };

        var copy = Packer.Copy(source);

        Assert.That(copy.number, Is.EqualTo(source.number));
        CollectionAssert.AreEqual(source.values, copy.values);
        Assert.That(copy.values, Is.Not.SameAs(source.values));

        copy.values[0] = 99;
        Assert.That(source.values[0], Is.EqualTo(1));
    }

    [Test]
    public void GeneratedManagedStruct_UnchangedDeltaReadDeepCopiesBaselineArray()
    {
        var baseline = new DeltaParityManagedStructWithArray
        {
            number = 7,
            values = new[] { 1, 2, 3 }
        };
        var replacedArray = new[] { 8, 9 };
        var result = new DeltaParityManagedStructWithArray
        {
            number = -1,
            values = replacedArray
        };

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<DeltaParityManagedStructWithArray>.Write(_packer, baseline, baseline), Is.False);
        Assert.That(_packer.positionInBits, Is.EqualTo(1));

        _packer.ResetPositionAndMode(true);
        DeltaPacker<DeltaParityManagedStructWithArray>.Read(_packer, baseline, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(1));
        Assert.That(result.number, Is.EqualTo(baseline.number));
        CollectionAssert.AreEqual(baseline.values, result.values);
        Assert.That(result.values, Is.Not.SameAs(baseline.values));
        Assert.That(result.values, Is.Not.SameAs(replacedArray));

        result.values[0] = 99;
        Assert.That(baseline.values[0], Is.EqualTo(1));
    }

    [Test]
    public void GeneratedBaseType_PreservesRuntimeTypeLikePacker()
    {
        DeltaParityBase oldValue = new DeltaParityDerivedA { baseValue = 1, a = "old" };
        DeltaParityBase newValue = new DeltaParityDerivedB { baseValue = 2, b = new Vector3(3, 4, 5) };
        var expected = ProjectThroughPacker(newValue);

        Assert.That(expected, Is.TypeOf<DeltaParityDerivedB>(), "Packer must preserve the registered runtime type.");

        var result = RoundTrip(oldValue, newValue, true);

        Assert.That(result.GetType(), Is.EqualTo(expected.GetType()));
        var derived = (DeltaParityDerivedB)result;
        var expectedDerived = (DeltaParityDerivedB)expected;
        Assert.That(derived.baseValue, Is.EqualTo(expectedDerived.baseValue));
        Assert.That(derived.b, Is.EqualTo(expectedDerived.b));
    }

    [Test]
    public void GeneratedBaseType_RegisteredAndUnregisteredRuntimeTransitionsMatchPacker()
    {
        Assert.That(Hasher.IsRegistered(typeof(DeltaParityUnregisteredDerived)), Is.False);

        DeltaParityBase unregistered = new DeltaParityUnregisteredDerived { baseValue = 11 };
        DeltaParityBase registered = new DeltaParityDerivedB { baseValue = 22, b = new Vector3(3, 4, 5) };

        var toRegistered = RoundTripWithProjectedBaseline(unregistered, registered);
        Assert.That(toRegistered, Is.TypeOf<DeltaParityDerivedB>());
        Assert.That(toRegistered.baseValue, Is.EqualTo(registered.baseValue));
        Assert.That(((DeltaParityDerivedB)toRegistered).b, Is.EqualTo(((DeltaParityDerivedB)registered).b));

        var toUnregistered = RoundTripWithProjectedBaseline(registered, unregistered);
        var expected = ProjectThroughPacker(unregistered);
        Assert.That(toUnregistered.GetType(), Is.EqualTo(expected.GetType()));
        Assert.That(toUnregistered.baseValue, Is.EqualTo(expected.baseValue));
    }

    [Test]
    public void GeneratedBaseType_ReplacesWrongRuntimeDestinationAndConsumesDelta()
    {
        DeltaParityBase oldValue = new DeltaParityDerivedA { baseValue = 1, a = "old" };
        DeltaParityBase newValue = new DeltaParityDerivedA { baseValue = 2, a = "new" };
        var receiverBaseline = ProjectThroughPacker(oldValue);
        var replaced = new DeltaParityUnregisteredDerived { baseValue = 99 };
        DeltaParityBase result = replaced;

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<DeltaParityBase>.Write(_packer, oldValue, newValue), Is.True);
        int writtenBits = _packer.positionInBits;

        _packer.ResetPositionAndMode(true);
        DeltaPacker<DeltaParityBase>.Read(_packer, receiverBaseline, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        Assert.That(replaced.disposed, Is.True);
        Assert.That(result, Is.TypeOf<DeltaParityDerivedA>());
        Assert.That(result.baseValue, Is.EqualTo(newValue.baseValue));
        Assert.That(((DeltaParityDerivedA)result).a, Is.EqualTo(((DeltaParityDerivedA)newValue).a));
    }

    [Test]
    public void GeneratedBaseType_RuntimeTypeTransitionDisposesDistinctDestination()
    {
        DeltaParityBase oldValue = new DeltaParityDerivedA { baseValue = 1, a = "old" };
        DeltaParityBase newValue = new DeltaParityDerivedB { baseValue = 2, b = new Vector3(3, 4, 5) };
        var receiverBaseline = ProjectThroughPacker(oldValue);
        var replaced = new DeltaParityUnregisteredDerived { baseValue = 99 };
        DeltaParityBase result = replaced;

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<DeltaParityBase>.Write(_packer, oldValue, newValue), Is.True);
        int writtenBits = _packer.positionInBits;

        _packer.ResetPositionAndMode(true);
        DeltaPacker<DeltaParityBase>.Read(_packer, receiverBaseline, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        Assert.That(replaced.disposed, Is.True);
        Assert.That(result, Is.TypeOf<DeltaParityDerivedB>());
        Assert.That(result.baseValue, Is.EqualTo(newValue.baseValue));
        Assert.That(((DeltaParityDerivedB)result).b, Is.EqualTo(((DeltaParityDerivedB)newValue).b));
    }

    [Test]
    public void GeneratedBaseType_NullTransitionDisposesDistinctDestination()
    {
        DeltaParityBase oldValue = new DeltaParityDerivedA { baseValue = 1, a = "old" };
        var receiverBaseline = ProjectThroughPacker(oldValue);
        var replaced = new DeltaParityUnregisteredDerived { baseValue = 99 };
        DeltaParityBase result = replaced;

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<DeltaParityBase>.Write(_packer, oldValue, null), Is.True);
        int writtenBits = _packer.positionInBits;

        _packer.ResetPositionAndMode(true);
        DeltaPacker<DeltaParityBase>.Read(_packer, receiverBaseline, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        Assert.That(replaced.disposed, Is.True);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GeneratedBaseType_UnknownRuntimeTypeHashSkipsPayloadAndKeepsBaseline()
    {
        const uint bogusHash = 0xDEADBEEFu;
        Assert.That(Hasher.TryGetType(bogusHash, out _), Is.False);

        object oldValue = new DeltaParityDerivedA { baseValue = 1, a = "old" };
        uint oldHash = Hasher.GetStableHashU32(typeof(DeltaParityDerivedA));

        const int payloadBits = 77;

        _packer.ResetPositionAndMode(false);
        _packer.WriteBit(true);
        DeltaPacker<bool>.Write(_packer, true, true);
        DeltaPacker<uint>.Write(_packer, oldHash, bogusHash);
        _packer.WriteBits(payloadBits, 32);
        for (int i = 0; i < payloadBits; i++)
            _packer.WriteBit(true);
        int sentinelPos = _packer.positionInBits;
        _packer.WriteBits(0xABCD, 16);

        _packer.ResetPositionAndMode(true);
        object result = null;
        PackDeltaObj.ReadDeltaObject(_packer, oldValue, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(sentinelPos));
        Assert.That(_packer.ReadBits(16), Is.EqualTo((ulong)0xABCD));
        Assert.That(result, Is.TypeOf<DeltaParityDerivedA>());
        Assert.That(result, Is.Not.SameAs(oldValue));
        var kept = (DeltaParityDerivedA)result;
        Assert.That(kept.baseValue, Is.EqualTo(1));
        Assert.That(kept.a, Is.EqualTo("old"));
    }

    [Test]
    public void CustomIPackedType_DeltaFallbackMatchesPackerSemantics()
    {
        var oldValue = new DeltaParityCustomPacked { serializedValue = 1, localOnlyValue = 100 };
        var newValue = new DeltaParityCustomPacked { serializedValue = 2, localOnlyValue = 200 };
        var expected = ProjectThroughPacker(newValue);

        Assert.That(Packer<DeltaParityCustomPacked>.HasPacker(), Is.True);
        Assert.That(expected, Is.Not.Null);

        var result = RoundTrip(oldValue, newValue, true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.serializedValue, Is.EqualTo(expected.serializedValue));
        Assert.That(result.localOnlyValue, Is.EqualTo(expected.localOnlyValue));
    }

    [Test]
    public void NullablePackerType_AllTransitionsRoundTripThroughDeltaPacker()
    {
        Assert.That(RoundTrip<int?>(null, 42, true), Is.EqualTo(42));
        Assert.That(RoundTrip<int?>(42, 43, true), Is.EqualTo(43));
        Assert.That(RoundTrip<int?>(42, null, true), Is.Null);
        Assert.That(RoundTrip<int?>(42, 42, false), Is.EqualTo(42));
        Assert.That(RoundTrip<int?>(null, null, false), Is.Null);
    }

    [Test]
    public void RuntimeTypedFallback_HasGenericPackerParity()
    {
        const string oldValue = "old";
        const string newValue = "new \u263A";
        string expected = ProjectThroughPacker(newValue);

        _packer.ResetPositionAndMode(false);
        bool changed = DeltaPacker.Write(_packer, typeof(string), oldValue, newValue);
        Assert.That(changed, Is.True);

        _packer.ResetPositionAndMode(true);
        object result = null;
        DeltaPacker.Read(_packer, typeof(string), oldValue, ref result);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void RuntimeTypedRegisteredWriter_DoesNotAllocatePerDispatch()
    {
        object oldValue = 123;
        object newValue = 124;

        for (int i = 0; i < 32; ++i)
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker.Write(_packer, typeof(int), oldValue, newValue), Is.True);
        }

        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; ++i)
        {
            _packer.ResetPositionAndMode(false);
            DeltaPacker.Write(_packer, typeof(int), oldValue, newValue);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void RuntimeTypedFallback_EqualDistinctQueueUsesRegisteredPackerEquality()
    {
        var oldValue = new Queue<int>(new[] { 1, 2, 3 });
        var equalValue = new Queue<int>(new[] { 1, 2, 3 });
        var expected = ProjectThroughPacker(oldValue);

        _packer.ResetPositionAndMode(false);
        bool changed = DeltaPacker.Write(_packer, typeof(Queue<int>), oldValue, equalValue);
        Assert.That(changed, Is.False);

        _packer.ResetPositionAndMode(true);
        object result = ProjectThroughPacker(oldValue);
        DeltaPacker.Read(_packer, typeof(Queue<int>), oldValue, ref result);

        Assert.That(result, Is.TypeOf<Queue<int>>());
        CollectionAssert.AreEqual(expected, (Queue<int>)result);
    }

    [Test]
    public void RuntimeTypedFallback_UnregisteredInterfaceUsesGenericPackerFallback()
    {
        IDeltaParityContract oldValue = new DeltaParityInterfaceValue { serializedValue = 1 };
        IDeltaParityContract newValue = new DeltaParityInterfaceValue { serializedValue = 2 };
        var expected = ProjectThroughPacker(newValue);

        Assert.That(Packer<IDeltaParityContract>.HasPacker(), Is.False);

        _packer.ResetPositionAndMode(false);
        bool changed = DeltaPacker.Write(_packer, typeof(IDeltaParityContract), oldValue, newValue);
        Assert.That(changed, Is.True);

        _packer.ResetPositionAndMode(true);
        object result = null;
        DeltaPacker.Read(_packer, typeof(IDeltaParityContract), oldValue, ref result);

        Assert.That(result, Is.TypeOf<DeltaParityInterfaceValue>());
        Assert.That(((IDeltaParityContract)result).value, Is.EqualTo(expected.value));
    }

    [Test]
    public void ArrayDelta_ChangedAndUnchangedContractsMatchOtherDeltaPackers()
    {
        var oldValue = new[] { 1, 2, 3 };
        var newValue = new[] { 1, 4, 3 };

        var result = RoundTrip(oldValue, newValue, true);
        CollectionAssert.AreEqual(ProjectThroughPacker(newValue), result);

        result = RoundTrip(oldValue, new[] { 1, 2, 3 }, false);
        CollectionAssert.AreEqual(ProjectThroughPacker(oldValue), result);
        Assert.That(result, Is.Not.SameAs(oldValue));
    }

    [Test]
    public void ArrayDelta_ChangedReadDoesNotMutateAliasedBaseline()
    {
        var oldValue = new[] { 1, 2, 3 };
        var newValue = new[] { 1, 4, 3 };
        var result = oldValue;

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<int[]>.Write(_packer, oldValue, newValue), Is.True);
        int writtenBits = _packer.positionInBits;

        _packer.ResetPositionAndMode(true);
        DeltaPacker<int[]>.Read(_packer, oldValue, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oldValue);
        CollectionAssert.AreEqual(newValue, result);
        Assert.That(result, Is.Not.SameAs(oldValue));

        result[0] = 99;
        Assert.That(oldValue[0], Is.EqualTo(1));
    }

    [Test]
    public void ListDelta_ChangedAndUnchangedContractsMatchOtherDeltaPackers()
    {
        var oldValue = new List<int> { 1, 2, 3 };
        var newValue = new List<int> { 1, 4, 3 };

        var result = RoundTrip(oldValue, newValue, true);
        CollectionAssert.AreEqual(ProjectThroughPacker(newValue), result);

        result = RoundTrip(oldValue, new List<int> { 1, 2, 3 }, false);
        CollectionAssert.AreEqual(ProjectThroughPacker(oldValue), result);
        Assert.That(result, Is.Not.SameAs(oldValue));
    }

    [Test]
    public void ListDelta_ChangedReadDoesNotMutateAliasedBaseline()
    {
        var oldValue = new List<int> { 1, 2, 3 };
        var newValue = new List<int> { 1, 4, 3 };
        var result = oldValue;

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<List<int>>.Write(_packer, oldValue, newValue), Is.True);
        int writtenBits = _packer.positionInBits;

        _packer.ResetPositionAndMode(true);
        DeltaPacker<List<int>>.Read(_packer, oldValue, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oldValue);
        CollectionAssert.AreEqual(newValue, result);
        Assert.That(result, Is.Not.SameAs(oldValue));

        result[0] = 99;
        Assert.That(oldValue[0], Is.EqualTo(1));
    }

    [Test]
    public void ArrayAndListDeltaHeaders_UseZeroForUnchangedAndOneForChanged()
    {
        var oldArray = new[] { 1, 2, 3 };
        CollectionAssert.AreEqual(oldArray, AssertDirectDeltaHeader(
            DeltaPacker<int[]>.DirectWrite, DeltaPacker<int[]>.DirectRead,
            oldArray, new[] { 1, 2, 3 }, false));
        CollectionAssert.AreEqual(new[] { 1, 4, 3 }, AssertDirectDeltaHeader(
            DeltaPacker<int[]>.DirectWrite, DeltaPacker<int[]>.DirectRead,
            oldArray, new[] { 1, 4, 3 }, true));

        var oldList = new List<int> { 1, 2, 3 };
        CollectionAssert.AreEqual(oldList, AssertDirectDeltaHeader(
            DeltaPacker<List<int>>.DirectWrite, DeltaPacker<List<int>>.DirectRead,
            oldList, new List<int> { 1, 2, 3 }, false));
        CollectionAssert.AreEqual(new[] { 1, 4, 3 }, AssertDirectDeltaHeader(
            DeltaPacker<List<int>>.DirectWrite, DeltaPacker<List<int>>.DirectRead,
            oldList, new List<int> { 1, 4, 3 }, true));

        AssertDeltaHeader(DeltaPacker<List<int>>.Write, oldList, new List<int> { 1, 2, 3 }, false);
        AssertDeltaHeader(DeltaPacker<List<int>>.Write, oldList, new List<int> { 1, 4, 3 }, true);
    }

    [Test]
    public void GeneratedUnmanagedArrayField_UsesRegisteredManagedPackerPaths()
    {
        var packWrite = Packer<int[]>.WriteFunc;
        var packRead = Packer<int[]>.ReadFunc;
        var deltaWrite = DeltaPacker<int[]>.WriteFunc;
        var deltaRead = DeltaPacker<int[]>.ReadFunc;
        int packWrites = 0;
        int packReads = 0;
        int deltaWrites = 0;
        int deltaReads = 0;

        try
        {
            Packer<int[]>.WriteFunc = (packer, value) =>
            {
                ++packWrites;
                packWrite(packer, value);
            };
            Packer<int[]>.ReadFunc = delegate(BitPacker packer, ref int[] value)
            {
                ++packReads;
                packRead(packer, ref value);
            };
            DeltaPacker<int[]>.WriteFunc = (packer, oldValue, newValue) =>
            {
                ++deltaWrites;
                return deltaWrite(packer, oldValue, newValue);
            };
            DeltaPacker<int[]>.ReadFunc = delegate(BitPacker packer, int[] oldValue, ref int[] value)
            {
                ++deltaReads;
                deltaRead(packer, oldValue, ref value);
            };

            var oldValue = new DeltaParityUnmanagedArrayField { values = new[] { 1, 2, 3 } };
            var newValue = new DeltaParityUnmanagedArrayField { values = new[] { 1, 4, 3 } };

            _packer.ResetPositionAndMode(false);
            Packer<DeltaParityUnmanagedArrayField>.Write(_packer, newValue);
            _packer.ResetPositionAndMode(true);
            DeltaParityUnmanagedArrayField packed = null;
            Packer<DeltaParityUnmanagedArrayField>.Read(_packer, ref packed);

            Assert.That(packWrites, Is.EqualTo(1));
            Assert.That(packReads, Is.EqualTo(1));
            CollectionAssert.AreEqual(newValue.values, packed.values);

            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DeltaParityUnmanagedArrayField>.Write(_packer, oldValue, newValue), Is.True);
            _packer.ResetPositionAndMode(true);
            var deltaResult = ProjectThroughPacker(oldValue);
            DeltaPacker<DeltaParityUnmanagedArrayField>.Read(_packer, oldValue, ref deltaResult);

            Assert.That(deltaWrites, Is.EqualTo(1));
            Assert.That(deltaReads, Is.EqualTo(1));
            CollectionAssert.AreEqual(newValue.values, deltaResult.values);
        }
        finally
        {
            Packer<int[]>.WriteFunc = packWrite;
            Packer<int[]>.ReadFunc = packRead;
            DeltaPacker<int[]>.WriteFunc = deltaWrite;
            DeltaPacker<int[]>.ReadFunc = deltaRead;
        }
    }

    [Test]
    public void HashSetWithoutSpecializedDelta_UsesPackerFallback()
    {
        var oldValue = new HashSet<int> { 1, 2, 3 };
        var newValue = new HashSet<int> { 2, 3, 4 };

        var result = RoundTrip(oldValue, newValue, true);
        Assert.That(result.SetEquals(ProjectThroughPacker(newValue)), Is.True);

        result = RoundTrip(oldValue, new HashSet<int> { 3, 2, 1 }, false);
        Assert.That(result.SetEquals(ProjectThroughPacker(oldValue)), Is.True);
        Assert.That(result, Is.Not.SameAs(oldValue));
    }

    [Test]
    public void DictionaryWithoutSpecializedDelta_UsesPackerFallback()
    {
        var oldValue = new Dictionary<int, string> { [1] = "one", [2] = "two" };
        var newValue = new Dictionary<int, string> { [1] = "one", [2] = "changed", [3] = "three" };

        var result = RoundTrip(oldValue, newValue, true);
        CollectionAssert.AreEquivalent(ProjectThroughPacker(newValue), result);

        result = RoundTrip(oldValue, new Dictionary<int, string>(oldValue), false);
        CollectionAssert.AreEquivalent(ProjectThroughPacker(oldValue), result);
        Assert.That(result, Is.Not.SameAs(oldValue));
    }

    [Test]
    public void DictionaryFallback_ChangedReadDoesNotMutateAliasedBaseline()
    {
        var oldValue = new Dictionary<int, string> { [1] = "one", [2] = "two" };
        var newValue = new Dictionary<int, string> { [1] = "one", [2] = "changed" };
        var result = oldValue;

        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<Dictionary<int, string>>.Write(_packer, oldValue, newValue), Is.True);
        int writtenBits = _packer.positionInBits;

        _packer.ResetPositionAndMode(true);
        DeltaPacker<Dictionary<int, string>>.Read(_packer, oldValue, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        CollectionAssert.AreEquivalent(
            new Dictionary<int, string> { [1] = "one", [2] = "two" }, oldValue);
        CollectionAssert.AreEquivalent(newValue, result);
        Assert.That(result, Is.Not.SameAs(oldValue));

        result[1] = "mutated";
        Assert.That(oldValue[1], Is.EqualTo("one"));
    }

    [Test]
    public void QueueWithoutSpecializedDelta_UsesPackerFallback()
    {
        var oldValue = new Queue<int>(new[] { 1, 2, 3 });
        var newValue = new Queue<int>(new[] { 1, 4, 3, 5 });

        var result = RoundTrip(oldValue, newValue, true);
        CollectionAssert.AreEqual(ProjectThroughPacker(newValue), result);

        result = RoundTrip(oldValue, new Queue<int>(new[] { 1, 2, 3 }), false);
        CollectionAssert.AreEqual(ProjectThroughPacker(oldValue), result);
        Assert.That(result, Is.Not.SameAs(oldValue));
    }

    [Test]
    public void StackWithoutSpecializedDelta_MatchesPackerProjection()
    {
        var oldValue = new Stack<int>(new[] { 1, 2, 3 });
        var newValue = new Stack<int>(new[] { 1, 4, 3, 5 });

        var result = RoundTrip(oldValue, newValue, true);
        CollectionAssert.AreEqual(ProjectThroughPacker(newValue), result);

        result = RoundTrip(oldValue, oldValue, false);
        CollectionAssert.AreEqual(ProjectThroughPacker(oldValue), result);
        Assert.That(result, Is.Not.SameAs(oldValue));
    }

    [Test]
    public void NativeArray_CreatedEmptyAndDefaultRemainDistinctThroughDeltaPacker()
    {
        var createdEmpty = new NativeArray<int>(0, Allocator.Persistent);
        try
        {
            var fromDefault = RoundTrip(default(NativeArray<int>), createdEmpty, true);
            Assert.That(fromDefault.IsCreated, Is.True);
            Assert.That(fromDefault.Length, Is.Zero);
            fromDefault.Dispose();

            var toDefault = RoundTrip(createdEmpty, default(NativeArray<int>), true);
            Assert.That(toDefault.IsCreated, Is.False);
        }
        finally
        {
            createdEmpty.Dispose();
        }
    }

    [Test]
    public void DisposableDictionaryDelta_UnchangedAliasedDestinationCopiesBaseline()
    {
        var oldValue = DisposableDictionary<int, string>.Create(
            new Dictionary<int, string> { [1] = "one", [2] = "two" });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableDictionary<int, string>>.Write(
                _packer, oldValue, oldValue), Is.False);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableDictionary<int, string>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(writtenBits, Is.EqualTo(1));
            Assert.That(oldValue.Count, Is.EqualTo(2));
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.dictionary, Is.Not.SameAs(oldValue.dictionary));

            result.Add(3, "three");
            Assert.That(oldValue.ContainsKey(3), Is.False);
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableDictionaryDelta_ChangedAliasedDestinationPreservesBaseline()
    {
        var oldValue = DisposableDictionary<int, string>.Create(
            new Dictionary<int, string> { [1] = "one", [2] = "two" });
        var newValue = DisposableDictionary<int, string>.Create(
            new Dictionary<int, string> { [1] = "one", [2] = "changed", [3] = "three" });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableDictionary<int, string>>.Write(
                _packer, oldValue, newValue), Is.True);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableDictionary<int, string>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(oldValue.Count, Is.EqualTo(2));
            Assert.That(oldValue[2], Is.EqualTo("two"));
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[2], Is.EqualTo("changed"));
            Assert.That(result.dictionary, Is.Not.SameAs(oldValue.dictionary));

            result[1] = "mutated";
            Assert.That(oldValue[1], Is.EqualTo("one"));
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!newValue.isDisposed) newValue.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableDictionaryDelta_DisposedAliasedDestinationPreservesBaseline()
    {
        var oldValue = DisposableDictionary<int, string>.Create(
            new Dictionary<int, string> { [1] = "one", [2] = "two" });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableDictionary<int, string>>.Write(
                _packer, oldValue, default), Is.True);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableDictionary<int, string>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(result.isDisposed, Is.True);
            Assert.That(oldValue.isDisposed, Is.False);
            Assert.That(oldValue.Count, Is.EqualTo(2));
            Assert.That(oldValue[2], Is.EqualTo("two"));
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableHashSetDelta_UnchangedAliasedDestinationCopiesBaseline()
    {
        var oldValue = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableHashSet<int>>.Write(_packer, oldValue, oldValue), Is.False);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableHashSet<int>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(writtenBits, Is.EqualTo(1));
            Assert.That(oldValue.SetEquals(new[] { 1, 2, 3 }), Is.True);
            Assert.That(result.SetEquals(oldValue), Is.True);
            Assert.That(result.set, Is.Not.SameAs(oldValue.set));

            result.Add(99);
            Assert.That(oldValue.Contains(99), Is.False);
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableHashSetDelta_ChangedAliasedDestinationPreservesBaseline()
    {
        var oldValue = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var newValue = DisposableHashSet<int>.Create(new[] { 2, 3, 4 });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableHashSet<int>>.Write(_packer, oldValue, newValue), Is.True);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableHashSet<int>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(oldValue.SetEquals(new[] { 1, 2, 3 }), Is.True);
            Assert.That(result.SetEquals(newValue), Is.True);
            Assert.That(result.set, Is.Not.SameAs(oldValue.set));

            result.Add(99);
            Assert.That(oldValue.Contains(99), Is.False);
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!newValue.isDisposed) newValue.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableHashSetDelta_DisposedAliasedDestinationPreservesBaseline()
    {
        var oldValue = DisposableHashSet<int>.Create(new[] { 1, 2, 3 });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableHashSet<int>>.Write(_packer, oldValue, default), Is.True);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableHashSet<int>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(result.isDisposed, Is.True);
            Assert.That(oldValue.isDisposed, Is.False);
            Assert.That(oldValue.SetEquals(new[] { 1, 2, 3 }), Is.True);
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableArrayDelta_UnchangedAliasedDestinationCopiesBaseline()
    {
        var oldValue = DisposableArray<int>.Create(new[] { 1, 2, 3 });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableArray<int>>.Write(_packer, oldValue, oldValue), Is.False);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableArray<int>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(writtenBits, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oldValue);
            CollectionAssert.AreEqual(oldValue, result);
            Assert.That(result.array, Is.Not.SameAs(oldValue.array));

            result[0] = 99;
            Assert.That(oldValue[0], Is.EqualTo(1));
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableArrayDelta_ChangedAliasedDestinationPreservesBaseline()
    {
        var oldValue = DisposableArray<int>.Create(new[] { 1, 2, 3 });
        var newValue = DisposableArray<int>.Create(new[] { 1, 4, 3 });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableArray<int>>.Write(_packer, oldValue, newValue), Is.True);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableArray<int>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oldValue);
            CollectionAssert.AreEqual(newValue, result);
            Assert.That(result.array, Is.Not.SameAs(oldValue.array));

            result[0] = 99;
            Assert.That(oldValue[0], Is.EqualTo(1));
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!newValue.isDisposed) newValue.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    [Test]
    public void DisposableArrayDelta_DisposedAliasedDestinationPreservesBaseline()
    {
        var oldValue = DisposableArray<int>.Create(new[] { 1, 2, 3 });
        var result = oldValue;
        try
        {
            _packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<DisposableArray<int>>.Write(_packer, oldValue, default), Is.True);
            int writtenBits = _packer.positionInBits;

            _packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableArray<int>>.Read(_packer, oldValue, ref result);

            Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
            Assert.That(result.isDisposed, Is.True);
            Assert.That(oldValue.isDisposed, Is.False);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, oldValue);
        }
        finally
        {
            if (!result.isDisposed) result.Dispose();
            if (!oldValue.isDisposed) oldValue.Dispose();
        }
    }

    private void AssertChangedRoundTrip<T>(T oldValue, T newValue)
    {
        T expected = ProjectThroughPacker(newValue);
        T result = RoundTrip(oldValue, newValue, true);
        Assert.That(result, Is.EqualTo(expected), typeof(T).FullName);
    }

    private void AssertUnchangedRoundTrip<T>(T value)
    {
        T result = RoundTrip(value, value, false);
        Assert.That(result, Is.EqualTo(value), typeof(T).FullName);
    }

    private T RoundTrip<T>(T oldValue, T newValue, bool expectedChanged)
    {
        _packer.ResetPositionAndMode(false);
        bool changed = DeltaPacker<T>.Write(_packer, oldValue, newValue);
        int writtenBits = _packer.positionInBits;
        Assert.That(changed, Is.EqualTo(expectedChanged), typeof(T).FullName);

#if !PURR_DELTA_CHECK
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBit(), Is.EqualTo(expectedChanged),
            $"{typeof(T).FullName} leading delta bit");

        if (!expectedChanged)
            Assert.That(writtenBits, Is.EqualTo(1),
                $"{typeof(T).FullName} unchanged delta should contain only its flag");
#endif

        _packer.ResetPositionAndMode(true);
        // Model the receiver's baseline as the value produced by the normal Packer path.
        T result = ProjectThroughPacker(oldValue);
        DeltaPacker<T>.Read(_packer, oldValue, ref result);
        return result;
    }

    private T RoundTripWithProjectedBaseline<T>(T oldValue, T newValue)
    {
        _packer.ResetPositionAndMode(false);
        Assert.That(DeltaPacker<T>.Write(_packer, oldValue, newValue), Is.True);
        int writtenBits = _packer.positionInBits;

        T receiverBaseline = ProjectThroughPacker(oldValue);
        T result = ProjectThroughPacker(oldValue);
        _packer.ResetPositionAndMode(true);
        DeltaPacker<T>.Read(_packer, receiverBaseline, ref result);

        Assert.That(_packer.positionInBits, Is.EqualTo(writtenBits));
        return result;
    }

    private void AssertDeltaHeader<T>(DeltaWriteFunc<T> writer, T oldValue, T newValue, bool expectedChanged)
    {
        _packer.ResetPositionAndMode(false);
        bool changed = writer(_packer, oldValue, newValue);
        int writtenBits = _packer.positionInBits;

        Assert.That(changed, Is.EqualTo(expectedChanged), typeof(T).FullName);

        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBit(), Is.EqualTo(expectedChanged), typeof(T).FullName);

        if (!expectedChanged)
            Assert.That(writtenBits, Is.EqualTo(1), $"{typeof(T).FullName} unchanged delta");
    }

    private T AssertDirectDeltaHeader<T>(DeltaWriteFunc<T> writer, DeltaReadFunc<T> reader,
        T oldValue, T newValue, bool expectedChanged)
    {
        AssertDeltaHeader(writer, oldValue, newValue, expectedChanged);

        _packer.ResetPositionAndMode(true);
        T result = ProjectThroughPacker(oldValue);
        reader(_packer, oldValue, ref result);
        return result;
    }

    private static T ProjectThroughPacker<T>(T value)
    {
        using var projectionPacker = BitPackerPool.Get();
        Packer<T>.Write(projectionPacker, value);
        projectionPacker.ResetPositionAndMode(true);

        T result = default;
        Packer<T>.Read(projectionPacker, ref result);
        return result;
    }
}
