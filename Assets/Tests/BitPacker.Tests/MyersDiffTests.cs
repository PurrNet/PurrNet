using System.Linq;
using NUnit.Framework;
using PurrNet.Packing;
using PurrNet.Pooling;

public class MyersDiffTests
{
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

    private static void DisposeOps<T>(DisposableList<DiffOp<T>> ops)
    {
        for (int i = 0; i < ops.Count; i++)
            ops[i].values.Dispose();
        ops.Dispose();
    }
}
