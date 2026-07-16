using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

[RegisterNetworkType(typeof(EqualityStructWithDontPack))]
[RegisterNetworkType(typeof(EqualityStructWithNestedDontPack))]
public sealed class PurrEqualityRegistrationAnchor
{
}

public struct EqualityStructWithDontPack
{
    public int packed;
    [DontPack] public int local;
}

public struct EqualityStructWithNestedDontPack
{
    public int outer;
    public EqualityStructWithDontPack nested;
}

public struct EqualityStructWithFloat
{
    public float value;
}

public struct EqualityStructPlainUnmanaged
{
    public int a;
    public ushort b;
}

public enum EqualityByteEnum : byte
{
    first,
    second
}

// these stay out of [RegisterNetworkType]/RPC usage on purpose: no codegen IPurrEquatable override,
// so they exercise the packed-range memcmp fallback the way precompiled-DLL types do
public struct EqualityUnregisteredWithDontPack
{
    public int packed;
    [DontPack] public int local;
}

public struct EqualityUnregisteredNestedDontPack
{
    public int outer;
    public EqualityUnregisteredWithDontPack nested;
}

public struct EqualityUnregisteredMergeLayout
{
    public int a;
    public int b;
    [DontPack] public int skipped;
    public int c;
}

public struct EqualityUnregisteredWithEnum
{
    public EqualityByteEnum mode;
    [DontPack] public int local;
}

public struct EqualityUnregisteredOverrideWins
{
    public int packed;
    [DontPack] public int local;
}

/// <summary>
/// Tests for Packer.AreEqual and PurrEquality (equality checker used by delta packing and elsewhere).
/// </summary>
public class PurrEqualityTests
{
    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
    }

    [Test]
    public void PackerAreEqual_Int()
    {
        Assert.IsTrue(Packer.AreEqual(0, 0));
        Assert.IsTrue(Packer.AreEqual(1, 1));
        Assert.IsTrue(Packer.AreEqual(-1, -1));
        Assert.IsFalse(Packer.AreEqual(0, 1));
        Assert.IsFalse(Packer.AreEqual(1, -1));
    }

    [Test]
    public void PackerAreEqual_String()
    {
        Assert.IsTrue(Packer.AreEqual("hello", "hello"));
        Assert.IsTrue(Packer.AreEqual("", ""));
        Assert.IsFalse(Packer.AreEqual("hello", "world"));
        Assert.IsFalse(Packer.AreEqual("a", "A"));
    }

    [Test]
    public void PackerAreEqual_StringNull()
    {
        Assert.IsTrue(Packer.AreEqual<string>(null, null));
        Assert.IsFalse(Packer.AreEqual<string>(null, "x"));
        Assert.IsFalse(Packer.AreEqual<string>("x", null));
    }

    [Test]
    public void PackerAreEqual_Bool()
    {
        Assert.IsTrue(Packer.AreEqual(true, true));
        Assert.IsTrue(Packer.AreEqual(false, false));
        Assert.IsFalse(Packer.AreEqual(true, false));
    }

    [Test]
    public void PurrEquality_Dictionary_SameAndDifferentValues()
    {
        PackCollections.RegisterDictionary<int, string>();
        var a = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } };
        var b = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } };

        Assert.IsTrue(PurrEquality<Dictionary<int, string>>.Equals(a, b));

        b[2] = "deux";
        Assert.IsFalse(PurrEquality<Dictionary<int, string>>.Equals(a, b));
    }

    [Test]
    public void PurrEquality_HashSet_SameAndDifferentValues()
    {
        PackCollections.RegisterHashSet<int>();
        var a = new HashSet<int> { 1, 2, 3 };
        var b = new HashSet<int> { 1, 2, 3 };

        Assert.IsTrue(PurrEquality<HashSet<int>>.Equals(a, b));

        b.Remove(2);
        b.Add(4);
        Assert.IsFalse(PurrEquality<HashSet<int>>.Equals(a, b));
    }

    [Test]
    public void PurrEquality_DictionaryAndHashSet_AreOrderIndependent()
    {
        PackCollections.RegisterDictionary<int, string>();
        PackCollections.RegisterHashSet<int>();

        var dictionaryA = new Dictionary<int, string>
        {
            [1] = "one",
            [2] = "two",
            [3] = "three"
        };
        var dictionaryB = new Dictionary<int, string>
        {
            [3] = "three",
            [2] = "two",
            [1] = "one"
        };
        var setA = new HashSet<int> { 1, 2, 3 };
        var setB = new HashSet<int> { 3, 2, 1 };

        Assert.IsTrue(PurrEquality<Dictionary<int, string>>.Equals(dictionaryA, dictionaryB));
        Assert.IsTrue(PurrEquality<HashSet<int>>.Equals(setA, setB));
    }

    [Test]
    public void PurrEquality_DictionaryAndHashSet_PreserveOneToOneFallbackMatching()
    {
        PackCollections.RegisterDictionary<int, string>();
        PackCollections.RegisterHashSet<int>();

        var comparer = new NeverEqualIntComparer();
        var dictionaryA = new Dictionary<int, string>(comparer)
        {
            { 7, "first" },
            { 7, "second" }
        };
        var dictionaryEquivalent = new Dictionary<int, string>(comparer)
        {
            { 7, "second" },
            { 7, "first" }
        };
        var dictionaryDifferent = new Dictionary<int, string>(comparer)
        {
            { 7, "first" },
            { 8, "second" }
        };
        var setA = new HashSet<int>(comparer) { 7, 7 };
        var setEquivalent = new HashSet<int>(comparer) { 7, 7 };
        var setDifferent = new HashSet<int>(comparer) { 7, 8 };

        Assert.IsTrue(PurrEquality<Dictionary<int, string>>.Equals(dictionaryA, dictionaryEquivalent));
        Assert.IsFalse(PurrEquality<Dictionary<int, string>>.Equals(dictionaryA, dictionaryDifferent));
        Assert.IsTrue(PurrEquality<HashSet<int>>.Equals(setA, setEquivalent));
        Assert.IsFalse(PurrEquality<HashSet<int>>.Equals(setA, setDifferent));
    }

    [Test]
    public void PurrEquality_DictionaryAndHashSet_FloatKeysRemainBitExact()
    {
        PackCollections.RegisterDictionary<float, int>();
        PackCollections.RegisterHashSet<float>();

        float negativeZero = System.BitConverter.Int32BitsToSingle(unchecked((int)0x80000000u));
        float nanA = System.BitConverter.Int32BitsToSingle(unchecked((int)0x7FC00001u));
        float nanB = System.BitConverter.Int32BitsToSingle(unchecked((int)0x7FC01234u));

        var positiveZeroDictionary = new Dictionary<float, int> { [0f] = 1 };
        var negativeZeroDictionary = new Dictionary<float, int> { [negativeZero] = 1 };
        var nanDictionaryA = new Dictionary<float, int> { [nanA] = 1 };
        var nanDictionaryB = new Dictionary<float, int> { [nanB] = 1 };

        Assert.IsFalse(PurrEquality<Dictionary<float, int>>.Equals(
            positiveZeroDictionary, negativeZeroDictionary));
        Assert.IsFalse(PurrEquality<Dictionary<float, int>>.Equals(nanDictionaryA, nanDictionaryB));
        Assert.IsFalse(PurrEquality<HashSet<float>>.Equals(
            new HashSet<float> { 0f }, new HashSet<float> { negativeZero }));
        Assert.IsFalse(PurrEquality<HashSet<float>>.Equals(
            new HashSet<float> { nanA }, new HashSet<float> { nanB }));
    }

    [Test]
    public void PurrEquality_DictionaryAndHashSet_CommonSteadyStatePathsAllocateNothing()
    {
        PackCollections.RegisterDictionary<int, int>();
        PackCollections.RegisterHashSet<int>();

        var dictionary = new Dictionary<int, int>(128);
        var dictionaryAligned = new Dictionary<int, int>(128);
        var dictionaryReversed = new Dictionary<int, int>(128);
        var set = new HashSet<int>();
        var setAligned = new HashSet<int>();
        var setReversed = new HashSet<int>();
        for (int i = 0; i < 128; i++)
        {
            dictionary.Add(i, i * 17);
            dictionaryAligned.Add(i, i * 17);
            set.Add(i);
            setAligned.Add(i);
        }
        for (int i = 127; i >= 0; i--)
        {
            dictionaryReversed.Add(i, i * 17);
            setReversed.Add(i);
        }

        Assert.IsTrue(PurrEquality<Dictionary<int, int>>.Equals(dictionary, dictionaryAligned));
        Assert.IsTrue(PurrEquality<Dictionary<int, int>>.Equals(dictionary, dictionaryReversed));
        Assert.IsTrue(PurrEquality<HashSet<int>>.Equals(set, setAligned));
        Assert.IsTrue(PurrEquality<HashSet<int>>.Equals(set, setReversed));

        System.GC.GetAllocatedBytesForCurrentThread();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        bool allEqual = true;
        for (int i = 0; i < 256; i++)
        {
            allEqual &= PurrEquality<Dictionary<int, int>>.Equals(dictionary, dictionaryAligned);
            allEqual &= PurrEquality<Dictionary<int, int>>.Equals(dictionary, dictionaryReversed);
            allEqual &= PurrEquality<HashSet<int>>.Equals(set, setAligned);
            allEqual &= PurrEquality<HashSet<int>>.Equals(set, setReversed);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(allEqual);
        Assert.AreEqual(0, allocated);
    }

    [Test]
    public void PurrEquality_UnmanagedStruct_IgnoresDontPackFields()
    {
        var baseline = new EqualityStructWithDontPack { packed = 5, local = 1 };
        var sameWire = new EqualityStructWithDontPack { packed = 5, local = 2 };
        var differentWire = new EqualityStructWithDontPack { packed = 6, local = 1 };

        Assert.IsTrue(PurrEquality<EqualityStructWithDontPack>.Equals(baseline, sameWire));
        Assert.IsFalse(PurrEquality<EqualityStructWithDontPack>.Equals(baseline, differentWire));
        Assert.IsTrue(Packer.AreEqual(baseline, sameWire));
        Assert.IsFalse(Packer.AreEqual(baseline, differentWire));
    }

    [Test]
    public void PurrEquality_UnmanagedStruct_IgnoresNestedDontPackFields()
    {
        var baseline = new EqualityStructWithNestedDontPack
        {
            outer = 1,
            nested = new EqualityStructWithDontPack { packed = 5, local = 1 }
        };
        var sameWire = new EqualityStructWithNestedDontPack
        {
            outer = 1,
            nested = new EqualityStructWithDontPack { packed = 5, local = 2 }
        };
        var differentWire = new EqualityStructWithNestedDontPack
        {
            outer = 1,
            nested = new EqualityStructWithDontPack { packed = 6, local = 1 }
        };

        Assert.IsTrue(PurrEquality<EqualityStructWithNestedDontPack>.Equals(baseline, sameWire));
        Assert.IsFalse(PurrEquality<EqualityStructWithNestedDontPack>.Equals(baseline, differentWire));
    }

    [Test]
    public void PurrEquality_UnmanagedStructWithoutDontPack_StaysBitExact()
    {
        float negativeZero = System.BitConverter.Int32BitsToSingle(unchecked((int)0x80000000u));
        var positiveZero = new EqualityStructWithFloat { value = 0f };
        var negativeZeroStruct = new EqualityStructWithFloat { value = negativeZero };

        Assert.IsFalse(PurrEquality<EqualityStructWithFloat>.Equals(positiveZero, negativeZeroStruct));
        Assert.IsTrue(PurrEquality<EqualityStructWithFloat>.Equals(negativeZeroStruct, negativeZeroStruct));
    }

    [Test]
    public void DefaultComparerLookup_FastPathEligibility()
    {
        Assert.IsTrue(DefaultComparerLookup<int>.CanUse());
        Assert.IsTrue(DefaultComparerLookup<EqualityByteEnum>.CanUse());
        Assert.IsTrue(DefaultComparerLookup<System.Guid>.CanUse());
        Assert.IsTrue(DefaultComparerLookup<EqualityStructPlainUnmanaged>.CanUse());

        Assert.IsFalse(DefaultComparerLookup<float>.CanUse());
        Assert.IsFalse(DefaultComparerLookup<double>.CanUse());
        Assert.IsFalse(DefaultComparerLookup<decimal>.CanUse());
        Assert.IsFalse(DefaultComparerLookup<EqualityStructWithFloat>.CanUse());
        Assert.IsFalse(DefaultComparerLookup<PlayerID>.CanUse());
        Assert.IsFalse(DefaultComparerLookup<Vector3Int>.CanUse());
        Assert.IsFalse(DefaultComparerLookup<EqualityUnregisteredWithDontPack>.CanUse());
    }

    [Test]
    public void PurrEquality_DictionaryAndHashSet_PlainUnmanagedStructKeys_AreOrderIndependent()
    {
        PackCollections.RegisterDictionary<EqualityStructPlainUnmanaged, int>();
        PackCollections.RegisterHashSet<EqualityStructPlainUnmanaged>();

        var keyA = new EqualityStructPlainUnmanaged { a = 1, b = 2 };
        var keyB = new EqualityStructPlainUnmanaged { a = 3, b = 4 };
        var keyC = new EqualityStructPlainUnmanaged { a = 5, b = 6 };

        var dictionaryA = new Dictionary<EqualityStructPlainUnmanaged, int>
        {
            [keyA] = 1,
            [keyB] = 2,
            [keyC] = 3
        };
        var dictionaryB = new Dictionary<EqualityStructPlainUnmanaged, int>
        {
            [keyC] = 3,
            [keyB] = 2,
            [keyA] = 1
        };

        Assert.IsTrue(PurrEquality<Dictionary<EqualityStructPlainUnmanaged, int>>.Equals(dictionaryA, dictionaryB));

        dictionaryB[keyB] = 20;
        Assert.IsFalse(PurrEquality<Dictionary<EqualityStructPlainUnmanaged, int>>.Equals(dictionaryA, dictionaryB));

        dictionaryB[keyB] = 2;
        dictionaryB.Remove(keyC);
        dictionaryB[new EqualityStructPlainUnmanaged { a = 7, b = 8 }] = 3;
        Assert.IsFalse(PurrEquality<Dictionary<EqualityStructPlainUnmanaged, int>>.Equals(dictionaryA, dictionaryB));

        var setA = new HashSet<EqualityStructPlainUnmanaged> { keyA, keyB, keyC };
        var setB = new HashSet<EqualityStructPlainUnmanaged> { keyC, keyB, keyA };
        Assert.IsTrue(PurrEquality<HashSet<EqualityStructPlainUnmanaged>>.Equals(setA, setB));

        setB.Remove(keyC);
        setB.Add(new EqualityStructPlainUnmanaged { a = 7, b = 8 });
        Assert.IsFalse(PurrEquality<HashSet<EqualityStructPlainUnmanaged>>.Equals(setA, setB));
    }

    [Test]
    public void PurrEquality_Dictionary_GuidKeys_AreOrderIndependent()
    {
        PackCollections.RegisterDictionary<System.Guid, int>();

        var keyA = System.Guid.NewGuid();
        var keyB = System.Guid.NewGuid();
        var a = new Dictionary<System.Guid, int> { [keyA] = 1, [keyB] = 2 };
        var b = new Dictionary<System.Guid, int> { [keyB] = 2, [keyA] = 1 };

        Assert.IsTrue(PurrEquality<Dictionary<System.Guid, int>>.Equals(a, b));

        b[keyB] = 3;
        Assert.IsFalse(PurrEquality<Dictionary<System.Guid, int>>.Equals(a, b));
    }

    [Test]
    public void PurrEquality_Dictionary_PlayerIDKeys_KeepMemCmpSemantics()
    {
        PackCollections.RegisterDictionary<PlayerID, int>();

        var bot = new PlayerID(5, true);
        var human = new PlayerID(5, false);
        var botDictionary = new Dictionary<PlayerID, int> { [bot] = 1 };
        var botDictionaryCopy = new Dictionary<PlayerID, int> { [bot] = 1 };
        var humanDictionary = new Dictionary<PlayerID, int> { [human] = 1 };

        Assert.IsTrue(PurrEquality<Dictionary<PlayerID, int>>.Equals(botDictionary, botDictionaryCopy));
        Assert.IsFalse(PurrEquality<Dictionary<PlayerID, int>>.Equals(botDictionary, humanDictionary));
    }

    [Test]
    public void PurrEquality_Dictionary_FloatFieldStructKeys_RemainBitExact()
    {
        PackCollections.RegisterDictionary<EqualityStructWithFloat, int>();

        float negativeZero = System.BitConverter.Int32BitsToSingle(unchecked((int)0x80000000u));
        var positiveZeroDictionary = new Dictionary<EqualityStructWithFloat, int>
        {
            [new EqualityStructWithFloat { value = 0f }] = 1
        };
        var negativeZeroDictionary = new Dictionary<EqualityStructWithFloat, int>
        {
            [new EqualityStructWithFloat { value = negativeZero }] = 1
        };

        Assert.IsFalse(PurrEquality<Dictionary<EqualityStructWithFloat, int>>.Equals(
            positiveZeroDictionary, negativeZeroDictionary));
    }

    [Test]
    public void PurrEquality_UnregisteredStruct_PackedRanges_IgnoreDontPackFields()
    {
        Assert.IsFalse(PurrEquality<EqualityUnregisteredWithDontPack>.memCmpComparable);

        var ranges = PurrEquality<EqualityUnregisteredWithDontPack>.packedMemCmpRanges;
        Assert.IsNotNull(ranges);

        int covered = 0;
        for (int i = 0; i < ranges.Length; i++)
            covered += ranges[i].size;
        Assert.AreEqual(4, covered);

        var baseline = new EqualityUnregisteredWithDontPack { packed = 5, local = 1 };
        var sameWire = new EqualityUnregisteredWithDontPack { packed = 5, local = 2 };
        var differentWire = new EqualityUnregisteredWithDontPack { packed = 6, local = 1 };

        Assert.IsTrue(PurrEquality<EqualityUnregisteredWithDontPack>.PackedMemEquals(ref baseline, ref sameWire));
        Assert.IsFalse(PurrEquality<EqualityUnregisteredWithDontPack>.PackedMemEquals(ref baseline, ref differentWire));
        Assert.IsTrue(PurrEquality<EqualityUnregisteredWithDontPack>.Equals(baseline, sameWire));
        Assert.IsFalse(PurrEquality<EqualityUnregisteredWithDontPack>.Equals(baseline, differentWire));
    }

    [Test]
    public void PurrEquality_UnregisteredStruct_PackedRanges_RecurseIntoNestedStructs()
    {
        Assert.IsFalse(PurrEquality<EqualityUnregisteredNestedDontPack>.memCmpComparable);

        var ranges = PurrEquality<EqualityUnregisteredNestedDontPack>.packedMemCmpRanges;
        Assert.IsNotNull(ranges);

        int covered = 0;
        for (int i = 0; i < ranges.Length; i++)
            covered += ranges[i].size;
        Assert.AreEqual(8, covered);

        var baseline = new EqualityUnregisteredNestedDontPack
        {
            outer = 1,
            nested = new EqualityUnregisteredWithDontPack { packed = 5, local = 1 }
        };
        var sameWire = new EqualityUnregisteredNestedDontPack
        {
            outer = 1,
            nested = new EqualityUnregisteredWithDontPack { packed = 5, local = 2 }
        };
        var differentWire = new EqualityUnregisteredNestedDontPack
        {
            outer = 1,
            nested = new EqualityUnregisteredWithDontPack { packed = 6, local = 1 }
        };

        Assert.IsTrue(PurrEquality<EqualityUnregisteredNestedDontPack>.PackedMemEquals(ref baseline, ref sameWire));
        Assert.IsFalse(PurrEquality<EqualityUnregisteredNestedDontPack>.PackedMemEquals(ref baseline, ref differentWire));
        Assert.IsTrue(PurrEquality<EqualityUnregisteredNestedDontPack>.Equals(baseline, sameWire));
        Assert.IsFalse(PurrEquality<EqualityUnregisteredNestedDontPack>.Equals(baseline, differentWire));
    }

    [Test]
    public void PurrEquality_PackedRanges_MergeAdjacentAndHandleEnumLeaves()
    {
        var merged = PurrEquality.CollectMemCmpRanges(typeof(EqualityUnregisteredMergeLayout));
        Assert.AreEqual(2, merged.Length);
        Assert.AreEqual(0, merged[0].offset);
        Assert.AreEqual(8, merged[0].size);
        Assert.AreEqual(12, merged[1].offset);
        Assert.AreEqual(4, merged[1].size);

        var enumRanges = PurrEquality.CollectMemCmpRanges(typeof(EqualityUnregisteredWithEnum));
        Assert.AreEqual(1, enumRanges.Length);
        Assert.AreEqual(1, enumRanges[0].size);

        var baseline = new EqualityUnregisteredWithEnum { mode = EqualityByteEnum.second, local = 1 };
        var sameWire = new EqualityUnregisteredWithEnum { mode = EqualityByteEnum.second, local = 2 };
        var differentWire = new EqualityUnregisteredWithEnum { mode = EqualityByteEnum.first, local = 1 };

        Assert.IsTrue(PurrEquality<EqualityUnregisteredWithEnum>.Equals(baseline, sameWire));
        Assert.IsFalse(PurrEquality<EqualityUnregisteredWithEnum>.Equals(baseline, differentWire));
    }

    [Test]
    public void PurrEquality_LateOverride_WinsOverPackedRanges()
    {
        var a = new EqualityUnregisteredOverrideWins { packed = 1, local = 1 };
        var b = new EqualityUnregisteredOverrideWins { packed = 1, local = 2 };

        Assert.IsTrue(PurrEquality<EqualityUnregisteredOverrideWins>.Equals(a, b));

        PurrEquality<EqualityUnregisteredOverrideWins>.OverrideDefault(
            new NeverEqualComparer<EqualityUnregisteredOverrideWins>());

        Assert.IsFalse(PurrEquality<EqualityUnregisteredOverrideWins>.Equals(a, a));
    }

    [Test]
    public void PurrEquality_Default_IsNonNull_AfterUse()
    {
        _ = PurrEquality<int>.Default;
        Assert.IsNotNull(PurrEquality<int>.Default);
        _ = PurrEquality<string>.Default;
        Assert.IsNotNull(PurrEquality<string>.Default);
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_SameContent()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_DifferentContent()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 4 });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_DifferentCount()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_Empty()
    {
        var a = DisposableList<int>.Create();
        var b = DisposableList<int>.Create();
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_SameContent()
    {
        var a = DisposableList<string>.Create(new[] { "hello", "world" });
        var b = DisposableList<string>.Create(new[] { "hello", "world" });
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_DifferentContent()
    {
        var a = DisposableList<string>.Create(new[] { "hello", "world" });
        var b = DisposableList<string>.Create(new[] { "hello", "beautiful", "world" });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_Empty()
    {
        var a = DisposableList<string>.Create();
        var b = DisposableList<string>.Create();
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_WithNulls()
    {
        var a = DisposableList<string>.Create(new[] { "a", null, "b" });
        var b = DisposableList<string>.Create(new[] { "a", null, "b" });
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_OneNullElementMismatch()
    {
        var a = DisposableList<string>.Create(new[] { "a", null, "b" });
        var b = DisposableList<string>.Create(new[] { "a", "x", "b" });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableList_DefaultBoth()
    {
        var a = default(DisposableList<int>);
        var b = default(DisposableList<int>);
        Assert.IsTrue(Packer.AreEqual(a, b));
    }

    [Test]
    public void PackerAreEqual_DisposableList_DefaultVsAllocated()
    {
        var a = default(DisposableList<int>);
        var b = DisposableList<int>.Create();
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableList_DisposedVsDisposed()
    {
        var a = DisposableList<int>.Create(new[] { 1 });
        var b = DisposableList<int>.Create(new[] { 1 });
        a.Dispose();
        b.Dispose();
        Assert.IsTrue(Packer.AreEqual(a, b));
    }

    [Test]
    public void PurrEquality_DisposableListInt_EqualsDirect()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            Assert.IsTrue(PurrEquality<DisposableList<int>>.Equals(a, b));
            b[1] = 99;
            Assert.IsFalse(PurrEquality<DisposableList<int>>.Equals(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PurrEquality_DisposableListString_EqualsDirect()
    {
        var a = DisposableList<string>.Create(new[] { "hello", "world" });
        var b = DisposableList<string>.Create(new[] { "hello", "world" });
        try
        {
            Assert.IsTrue(PurrEquality<DisposableList<string>>.Equals(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    private sealed class NeverEqualIntComparer : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => false;

        public int GetHashCode(int obj) => 0;
    }

    private sealed class NeverEqualComparer<T> : IEqualityComparer<T>
    {
        public bool Equals(T x, T y) => false;

        public int GetHashCode(T obj) => 0;
    }
}
