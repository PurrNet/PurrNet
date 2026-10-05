using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using Unity.Collections;

/// <summary>
/// Tests for Packer.AreEqual and PurrEquality (equality checker used by delta packing and elsewhere).
/// </summary>
public class PurrEqualityTests
{
    public struct CustomUnmanagedEquatableValue : IPurrEquatable<CustomUnmanagedEquatableValue>
    {
        public int key;
        public int ignored;

        public bool PurrEquals(CustomUnmanagedEquatableValue other) => key == other.key;
    }

    public struct CustomEquatableValue : IPurrEquatable<CustomEquatableValue>
    {
        public int key;
        // Keep a reference field so equality uses the comparer rather than MemEquals.
        public string ignored;

        public bool PurrEquals(CustomEquatableValue other) => key == other.key;
    }

    public sealed class CustomEquatableReference : IPurrEquatable<CustomEquatableReference>
    {
        public int key;

        public bool PurrEquals(CustomEquatableReference other) => key == other.key;
    }

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
    }

    [TestCase(0, int.MinValue, false, TestName = "CollectionEquality_DistinguishesSignedZero")]
    [TestCase(0x7fc00001, 0x7fc00002, false, TestName = "CollectionEquality_DistinguishesNaNPayloads")]
    [TestCase(0x7fc00001, 0x7fc00001, true, TestName = "CollectionEquality_IdenticalNaNPayloadsAreEqual")]
    public void CollectionEquality_ComparesValueBits(int leftBits, int rightBits, bool expected)
    {
        AssertCollectionEquality(BitConverter.Int32BitsToSingle(leftBits),
            BitConverter.Int32BitsToSingle(rightBits), expected);
    }

    [Test]
    public void CollectionEquality_UnmanagedValuesBypassCustomComparer()
    {
        var previous = PurrEquality<CustomUnmanagedEquatableValue>.Default;
        try
        {
            PurrEquality.Override<CustomUnmanagedEquatableValue>();
            var left = new CustomUnmanagedEquatableValue { key = 7, ignored = 1 };
            var right = new CustomUnmanagedEquatableValue { key = 7, ignored = 2 };

            Assert.IsTrue(PurrEquality<CustomUnmanagedEquatableValue>.Default.Equals(left, right));
            Assert.IsFalse(Packer.AreEqualRef(ref left, ref right));
            AssertCollectionEquality(left, right, false);

            using var actual = DisposableList<CustomUnmanagedEquatableValue>.Create(new[] { left });
            using var operations = MyersDiff.Diff(new[] { left }, new[] { right });
            try
            {
                MyersDiff.Apply(actual, operations);
                Assert.AreEqual(1, actual.Count);
                Assert.AreEqual(right.ignored, actual[0].ignored);
            }
            finally
            {
                for (int i = 0; i < operations.Count; i++)
                    operations[i].Dispose();
            }
        }
        finally
        {
            PurrEquality<CustomUnmanagedEquatableValue>.OverrideDefault(previous);
        }
    }

    [Test]
    public void CollectionEquality_ReferenceFieldsPreserveCustomComparer()
    {
        var previous = PurrEquality<CustomEquatableValue>.Default;
        try
        {
            PurrEquality.Override<CustomEquatableValue>();
            var left = new CustomEquatableValue { key = 7, ignored = "left" };
            var right = new CustomEquatableValue { key = 7, ignored = "right" };

            AssertCollectionEquality(left, right, true);
        }
        finally
        {
            PurrEquality<CustomEquatableValue>.OverrideDefault(previous);
        }
    }

    [Test]
    public void ArrayEquality_SupportsCovariantReferenceArrays()
    {
        PackCollections.RegisterArray<object>();
        object[] left = new string[] { "same", null };
        object[] right = new string[] { "same", null };

        Assert.IsTrue(Packer.AreEqual(left, right));
        right[0] = "different";
        Assert.IsFalse(Packer.AreEqual(left, right));
    }

    [Test]
    public void ArrayEquality_UsesCurrentElementComparer()
    {
        var previous = PurrEquality<CustomEquatableReference>.Default;
        try
        {
            PurrEquality<CustomEquatableReference>.OverrideDefault(EqualityComparer<CustomEquatableReference>.Default);
            PackCollections.RegisterArray<CustomEquatableReference>();
            var left = new[] { new CustomEquatableReference { key = 7 } };
            var right = new[] { new CustomEquatableReference { key = 7 } };

            Assert.IsFalse(Packer.AreEqual(left, right));
            PurrEquality.Override<CustomEquatableReference>();
            Assert.IsTrue(Packer.AreEqual(left, right));
            right[0].key = 8;
            Assert.IsFalse(Packer.AreEqual(left, right));
        }
        finally
        {
            PurrEquality<CustomEquatableReference>.OverrideDefault(previous);
        }
    }

    [Test]
    public void NullableEquality_UsesCurrentElementComparer()
    {
        var previous = PurrEquality<CustomEquatableValue>.Default;
        try
        {
            PurrEquality<CustomEquatableValue>.OverrideDefault(EqualityComparer<CustomEquatableValue>.Default);
            PackCollections.RegisterNullable<CustomEquatableValue>();
            CustomEquatableValue? left = new CustomEquatableValue { key = 7, ignored = "left" };
            CustomEquatableValue? right = new CustomEquatableValue { key = 7, ignored = "right" };

            Assert.IsFalse(Packer.AreEqual(left, right));
            PurrEquality.Override<CustomEquatableValue>();
            Assert.IsTrue(Packer.AreEqual(left, right));
            Assert.IsFalse(Packer.AreEqual(left, (CustomEquatableValue?)null));
            Assert.IsTrue(Packer.AreEqual<CustomEquatableValue?>(null, null));
        }
        finally
        {
            PurrEquality<CustomEquatableValue>.OverrideDefault(previous);
        }
    }

    [TestCase(0, int.MinValue)]
    [TestCase(0x7fc00001, 0x7fc00002)]
    public void DisposableListDelta_PreservesChangedFloatBits(int oldBits, int newBits)
    {
        PackCollections.RegisterDisposableList<float>();
        using var packer = BitPackerPool.Get();
        using var old = DisposableList<float>.Create(new[] { BitConverter.Int32BitsToSingle(oldBits) });
        using var current = DisposableList<float>.Create(new[] { BitConverter.Int32BitsToSingle(newBits) });
        var result = default(DisposableList<float>);
        try
        {
            Assert.IsTrue(DeltaPacker<DisposableList<float>>.Write(packer, old, current));
            packer.ResetPositionAndMode(true);
            DeltaPacker<DisposableList<float>>.Read(packer, old, ref result);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(newBits, BitConverter.SingleToInt32Bits(result[0]));
            Assert.AreEqual(oldBits, BitConverter.SingleToInt32Bits(old[0]), "Baseline must remain unchanged");
        }
        finally
        {
            result.Dispose();
        }
    }

    [TestCase(0, int.MinValue)]
    [TestCase(0x7fc00001, 0x7fc00002)]
    public void NativeListDelta_PreservesChangedFloatBits(int oldBits, int newBits)
    {
        PackCollections.RegisterNativeList<float>();
        using var packer = BitPackerPool.Get();
        using var old = new NativeList<float>(1, Allocator.Persistent);
        using var current = new NativeList<float>(1, Allocator.Persistent);
        old.Add(BitConverter.Int32BitsToSingle(oldBits));
        current.Add(BitConverter.Int32BitsToSingle(newBits));
        var result = default(NativeList<float>);
        try
        {
            Assert.IsTrue(DeltaPacker<NativeList<float>>.Write(packer, old, current));
            packer.ResetPositionAndMode(true);
            DeltaPacker<NativeList<float>>.Read(packer, old, ref result);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(newBits, BitConverter.SingleToInt32Bits(result[0]));
            Assert.AreEqual(oldBits, BitConverter.SingleToInt32Bits(old[0]), "Baseline must remain unchanged");
        }
        finally
        {
            if (result.IsCreated)
                result.Dispose();
        }
    }

    private static void AssertCollectionEquality<T>(T left, T right, bool expected)
    {
        PackCollections.RegisterArray<T>();
        PackCollections.RegisterList<T>();
        PackCollections.RegisterDictionary<int, T>();
        PackCollections.RegisterDictionary<T, int>();
        PackCollections.RegisterHashSet<T>();
        PackCollections.RegisterQueue<T>();
        PackCollections.RegisterStack<T>();

        Assert.AreEqual(expected, Packer.AreEqual(new[] { left }, new[] { right }), "Array");
        Assert.AreEqual(expected, Packer.AreEqual(new List<T> { left }, new List<T> { right }), "List");
        Assert.AreEqual(expected, Packer.AreEqual(new Dictionary<int, T> { { 1, left } },
            new Dictionary<int, T> { { 1, right } }), "Dictionary values");
        Assert.AreEqual(expected, Packer.AreEqual(new Dictionary<T, int> { { left, 1 } },
            new Dictionary<T, int> { { right, 1 } }), "Dictionary keys");
        Assert.AreEqual(expected, Packer.AreEqual(new HashSet<T> { left }, new HashSet<T> { right }), "HashSet");
        Assert.AreEqual(expected, Packer.AreEqual(new Queue<T>(new[] { left }), new Queue<T>(new[] { right })), "Queue");
        Assert.AreEqual(expected, Packer.AreEqual(new Stack<T>(new[] { left }), new Stack<T>(new[] { right })), "Stack");

        using var leftList = DisposableList<T>.Create(new[] { left });
        using var rightList = DisposableList<T>.Create(new[] { right });
        using var leftArray = DisposableArray<T>.Create(1);
        using var rightArray = DisposableArray<T>.Create(1);
        leftArray.array[0] = left;
        rightArray.array[0] = right;
        Assert.AreEqual(expected, leftList.Equals(rightList), "DisposableList");
        Assert.AreEqual(expected, leftArray.Equals(rightArray), "DisposableArray");
    }

    [Test]
    public void PurrEquality_CustomValue_PreservesPurrEqualsWithReferenceFields()
    {
        var previous = PurrEquality<CustomEquatableValue>.Default;
        try
        {
            PurrEquality.Override<CustomEquatableValue>();
            var value = new CustomEquatableValue { key = 7, ignored = "left" };

            Assert.IsTrue(PurrEquality<CustomEquatableValue>.Equals(value,
                new CustomEquatableValue { key = 7, ignored = "right" }));
            Assert.IsTrue(PurrEquality<CustomEquatableValue>.Equals(value,
                new CustomEquatableValue { key = 7, ignored = null }));
            Assert.IsFalse(PurrEquality<CustomEquatableValue>.Equals(value,
                new CustomEquatableValue { key = 8, ignored = "left" }));
        }
        finally
        {
            PurrEquality<CustomEquatableValue>.OverrideDefault(previous);
        }
    }

    [Test]
    public void PurrEquality_CustomReference_PreservesEqualityAndNullGuards()
    {
        var previous = PurrEquality<CustomEquatableReference>.Default;
        try
        {
            PurrEquality.Override<CustomEquatableReference>();
            var value = new CustomEquatableReference { key = 7 };

            Assert.IsTrue(PurrEquality<CustomEquatableReference>.Equals(null, null));
            Assert.IsFalse(PurrEquality<CustomEquatableReference>.Equals(null, value));
            Assert.IsFalse(PurrEquality<CustomEquatableReference>.Equals(value, null));
            Assert.IsTrue(PurrEquality<CustomEquatableReference>.Equals(value,
                new CustomEquatableReference { key = 7 }));
            Assert.IsFalse(PurrEquality<CustomEquatableReference>.Equals(value,
                new CustomEquatableReference { key = 8 }));
        }
        finally
        {
            PurrEquality<CustomEquatableReference>.OverrideDefault(previous);
        }
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
}
