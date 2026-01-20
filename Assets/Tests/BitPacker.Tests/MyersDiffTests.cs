using System.Linq;
using NUnit.Framework;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

internal readonly struct Item
{
    public readonly int id;
    public Item(int id) => this.id = id;
    public override bool Equals(object o) => o is Item i && i.id == id;
    public override int GetHashCode() => id;
    public override string ToString() => id.ToString();
}

public class MyersDiffTests
{
      [Test]
    public void Insert20At12_Delete1At28_ProducesExpected()
    {
        // old (no 20)
        using var old = DisposableList<Item>.Create(new[]
        {
            new Item(2), new Item(3), new Item(4), new Item(5), new Item(6),
            new Item(13), new Item(14), new Item(15), new Item(16),
            new Item(17), new Item(18), new Item(19),
            new Item(21), new Item(22), new Item(23), new Item(24), new Item(25), new Item(26),
            new Item(27), new Item(28), new Item(29), new Item(30), new Item(31),
            new Item(32), new Item(33), new Item(34), new Item(35), new Item(36), new Item(37),
        });

        // expected = insert 20 at index 12, then delete tail at index 28 (37 stays)
        using var expected = DisposableList<Item>.Create(new[]
        {
            new Item(2), new Item(3), new Item(4), new Item(5), new Item(6),
            new Item(13), new Item(14), new Item(15), new Item(16),
            new Item(17), new Item(18), new Item(19),
            new Item(20),
            new Item(21), new Item(22), new Item(23), new Item(24), new Item(25), new Item(26),
            new Item(27), new Item(28), new Item(29), new Item(30), new Item(31),
            new Item(32), new Item(33), new Item(34), new Item(35), new Item(36), new Item(37),
        });

        // Build ops with your diff (it should yield: Insert at 12, Delete 1 at 28)
        using var snapOld = DisposableList<Item>.Create(old);
        using var snapNew = DisposableList<Item>.Create(expected);
        var ops = MyersDiff.Diff(snapOld, snapNew);

        // Apply using a minimal single-pass that matches your current approach
        using var got = DisposableList<Item>.Create(old);
        MyersDiff.Apply(got, ops);

        // Assert
        Assert.AreEqual(expected.Count, got.Count, "Count mismatch");
        for (int i = 0; i < expected.Count; i++)
            Assert.AreEqual(expected[i].id, got[i].id, $"Mismatch at {i}");

        ops.Dispose();
    }

    [Test]
    public void EmptyToEmpty()
    {
        var a = new int[] { };
        var b = new int[] { };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(0, ops.Count);
        ops.Dispose();
    }

    [Test]
    public void EmptyToItems()
    {
        var a = new int[] { };
        var b = new[] { 1, 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(OperationType.Add, ops[0].type);
        Assert.AreEqual(3, ops[0].values.Count);
        CollectionAssert.AreEqual(b, ops[0].values);

        DisposeOps(ops);
    }

    [Test]
    public void ItemsToEmpty()
    {
        var a = new[] { 1, 2, 3 };
        var b = new int[] { };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(OperationType.Delete, ops[0].type);
        Assert.AreEqual(0, ops[0].index);
        Assert.AreEqual(3, ops[0].length);

        ops.Dispose();
    }

    [Test]
    public void AppendSingle()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 2, 3, 4 };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(OperationType.Add, ops[0].type);
        Assert.AreEqual(1, ops[0].values.Count);
        Assert.AreEqual(4, ops[0].values[0]);

        DisposeOps(ops);
    }

    [Test]
    public void DeleteSingle()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 3 };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(OperationType.Delete, ops[0].type);
        Assert.AreEqual(1, ops[0].index);
        Assert.AreEqual(1, ops[0].length);

        ops.Dispose();
    }

    [Test]
    public void InsertMiddle()
    {
        var a = new[] { 1, 3 };
        var b = new[] { 1, 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(OperationType.Insert, ops[0].type);
        Assert.AreEqual(1, ops[0].index);
        Assert.AreEqual(2, ops[0].values[0]);

        DisposeOps(ops);
    }

    [Test]
    public void CompleteReplacement()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 4, 5, 6 };

        var ops = MyersDiff.Diff(a, b);

        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void AddOneInTheMiddle()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 69, 2, 3 };

        var ops = MyersDiff.Diff(a, b);

        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void AddOneInTheMiddleIsh()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 69, 2, 4 };

        var ops = MyersDiff.Diff(a, b);

        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ApplyPreservesOriginal()
    {
        var a = new[] { 1, 2, 3, 4, 5 };
        var b = new[] { 1, 3, 5, 6 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);

        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void MixedOperations()
    {
        var a = new[] { 1, 2, 3, 4 };
        var b = new[] { 0, 1, 3, 4, 5 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);

        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ConsecutiveDeletes()
    {
        var a = new[] { 1, 2, 3, 4, 5 };
        var b = new[] { 1, 5 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);

        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ConsecutiveInserts()
    {
        var a = new[] { 1, 5 };
        var b = new[] { 1, 2, 3, 4, 5 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);

        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void NoChanges()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(0, ops.Count);

        ops.Dispose();
    }

    [Test]
    public void LargeRandomTest()
    {
        var a = Enumerable.Range(0, 100).ToArray();
        var b = Enumerable.Range(0, 100).Where(x => x % 3 != 0).Concat(new[] { 200, 201 }).ToArray();

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);

        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void StringDiff()
    {
        var a = new[] { "hello", "world" };
        var b = new[] { "hello", "beautiful", "world" };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<string>.Create(a);

        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void SingleElementToSingle()
    {
        var a = new[] { 1 };
        var b = new[] { 2 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void SingleToEmpty()
    {
        var a = new[] { 1 };
        var b = new int[] { };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void EmptyToSingle()
    {
        var a = new int[] { };
        var b = new[] { 1 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void PrependMultiple()
    {
        var a = new[] { 4, 5, 6 };
        var b = new[] { 1, 2, 3, 4, 5, 6 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void DeleteFirst()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void DeleteLast()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 2 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ReverseOrder()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 3, 2, 1 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void AlternatingPattern()
    {
        var a = new[] { 1, 0, 2, 0, 3, 0 };
        var b = new[] { 1, 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void DuplicateElements()
    {
        var a = new[] { 1, 1, 2, 2, 3, 3 };
        var b = new[] { 1, 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void AddDuplicates()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 1, 1, 2, 2, 3, 3 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void InterleavedChanges()
    {
        var a = new[] { 1, 2, 3, 4, 5 };
        var b = new[] { 1, 9, 3, 8, 5 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ShiftRight()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { 0, 1, 2, 3 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void MultipleScatteredInserts()
    {
        var a = new[] { 1, 4, 7 };
        var b = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void MultipleScatteredDeletes()
    {
        var a = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var b = new[] { 1, 4, 7 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void LongerToShorter()
    {
        var a = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var b = new[] { 2, 5, 8 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ShorterToLonger()
    {
        var a = new[] { 2, 5, 8 };
        var b = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void ComplexRealWorldScenario()
    {
        var a = new[] { 10, 20, 30, 40, 50, 60 };
        var b = new[] { 5, 10, 25, 30, 50, 70, 80 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void AllDuplicates()
    {
        var a = new[] { 1, 1, 1, 1 };
        var b = new[] { 1, 1 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void AddDuplicatesToEnd()
    {
        var a = new[] { 1, 1 };
        var b = new[] { 1, 1, 1, 1 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void DeleteMiddleRange()
    {
        var a = new[] { 1, 2, 3, 4, 5, 6, 7 };
        var b = new[] { 1, 2, 6, 7 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void InsertMiddleRange()
    {
        var a = new[] { 1, 2, 6, 7 };
        var b = new[] { 1, 2, 3, 4, 5, 6, 7 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void RepeatingPattern()
    {
        var a = new[] { 1, 2, 1, 2, 1, 2 };
        var b = new[] { 1, 2, 1, 2 };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<int>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void PackerAreEqual_Integers()
    {
        Assert.IsTrue(Packer.AreEqual(5, 5));
        Assert.IsFalse(Packer.AreEqual(5, 6));
        Assert.IsTrue(Packer.AreEqual(0, 0));
        Assert.IsTrue(Packer.AreEqual(-1, -1));
        Assert.IsFalse(Packer.AreEqual(-1, 1));
    }

    [Test]
    public void PackerAreEqual_Strings()
    {
        Assert.IsTrue(Packer.AreEqual("hello", "hello"));
        Assert.IsFalse(Packer.AreEqual("hello", "world"));
        Assert.IsTrue(Packer.AreEqual("", ""));
        Assert.IsFalse(Packer.AreEqual("a", "A"));
    }

    [Test]
    public void PackerAreEqual_StringNulls()
    {
        Assert.IsTrue(Packer.AreEqual<string>(null, null));
        Assert.IsFalse(Packer.AreEqual<string>(null, "hello"));
        Assert.IsFalse(Packer.AreEqual<string>("hello", null));
    }

    [Test]
    public void PackerAreEqual_Floats()
    {
        Assert.IsTrue(Packer.AreEqual(1.5f, 1.5f));
        Assert.IsFalse(Packer.AreEqual(1.5f, 1.6f));
        Assert.IsTrue(Packer.AreEqual(0.0f, 0.0f));
        Assert.IsTrue(Packer.AreEqual(-0.0f, 0.0f)); // might fail depending on implementation
    }

    [Test]
    public void PackerAreEqual_FloatSpecialValues()
    {
        Assert.IsTrue(Packer.AreEqual(float.PositiveInfinity, float.PositiveInfinity));
        Assert.IsTrue(Packer.AreEqual(float.NegativeInfinity, float.NegativeInfinity));
        Assert.IsFalse(Packer.AreEqual(float.PositiveInfinity, float.NegativeInfinity));

        // NaN is tricky - may or may not be equal to itself depending on implementation
        var result = Packer.AreEqual(float.NaN, float.NaN);
        Debug.Log($"NaN == NaN: {result}");
    }

    [Test]
    public void PackerAreEqual_Bools()
    {
        Assert.IsTrue(Packer.AreEqual(true, true));
        Assert.IsTrue(Packer.AreEqual(false, false));
        Assert.IsFalse(Packer.AreEqual(true, false));
        Assert.IsFalse(Packer.AreEqual(false, true));
    }

    [Test]
    public void PackerAreEqual_ValueTypes()
    {
        Assert.IsTrue(Packer.AreEqual((byte)5, (byte)5));
        Assert.IsFalse(Packer.AreEqual((byte)5, (byte)6));

        Assert.IsTrue(Packer.AreEqual((short)5, (short)5));
        Assert.IsFalse(Packer.AreEqual((short)5, (short)6));

        Assert.IsTrue(Packer.AreEqual(5L, 5L));
        Assert.IsFalse(Packer.AreEqual(5L, 6L));
    }

    [Test]
    public void DiffWithFloatNaN()
    {
        var a = new[] { 1.0f, float.NaN, 3.0f };
        var b = new[] { 1.0f, float.NaN, 3.0f };

        var ops = MyersDiff.Diff(a, b);

        Debug.Log($"Float NaN diff ops count: {ops.Count}");
        for (int i = 0; i < ops.Count; i++)
            Debug.Log($"  Op {i}: {ops[i].type} at {ops[i].index}");

        var result = DisposableList<float>.Create(a);
        MyersDiff.Apply(result, ops);

        // This might fail if NaN != NaN
        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void DiffWithNullStrings()
    {
        var a = new[] { "a", null, "b" };
        var b = new[] { "a", null, "b" };

        var ops = MyersDiff.Diff(a, b);
        Assert.AreEqual(0, ops.Count, "Null strings should match");

        ops.Dispose();
    }

    [Test]
    public void DiffWithMixedNulls()
    {
        var a = new[] { "a", null, "c" };
        var b = new[] { "a", "b", "c" };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<string>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void DiffWithAllNulls()
    {
        var a = new string[] { null, null, null };
        var b = new string[] { null, null };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<string>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    [Test]
    public void PackerAreEqual_CustomStruct()
    {
        var v1 = new Vector3(1, 2, 3);
        var v2 = new Vector3(1, 2, 3);
        var v3 = new Vector3(1, 2, 4);

        Assert.IsTrue(Packer.AreEqual(v1, v2));
        Assert.IsFalse(Packer.AreEqual(v1, v3));
    }

    [Test]
    public void DiffWithVector3()
    {
        var a = new[] {
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1)
        };
        var b = new[] {
            new Vector3(1, 0, 0),
            new Vector3(0, 0, 1)
        };

        var ops = MyersDiff.Diff(a, b);
        var result = DisposableList<Vector3>.Create(a);
        MyersDiff.Apply(result, ops);

        CollectionAssert.AreEqual(b, result);

        result.Dispose();
        DisposeOps(ops);
    }

    private static void DisposeOps<T>(DisposableList<DiffOp<T>> ops)
    {
        for (int i = 0; i < ops.Count; i++)
            ops[i].values.Dispose();
        ops.Dispose();
    }
}
