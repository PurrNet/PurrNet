using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;

public class StringDeltaPackerTests
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

    // --- Single string delta packing (DeltaPacker<string> uses fallback: changed bit + full value) ---

    [Test]
    public void SameString_NoChange()
    {
        const string s = "hello";
        bool hasChanged = DeltaPacker<string>.Write(packer, s, s);
        Assert.IsFalse(hasChanged);
    }

    [Test]
    public void DifferentString_Change_Roundtrip()
    {
        var old = "hello";
        var current = "world";

        bool hasChanged = DeltaPacker<string>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = "unused";
        DeltaPacker<string>.Read(packer, old, ref result);
        Assert.AreEqual(current, result);
    }

    [Test]
    public void EmptyToEmpty_NoChange()
    {
        bool hasChanged = DeltaPacker<string>.Write(packer, "", "");
        Assert.IsFalse(hasChanged);
    }

    [Test]
    public void EmptyToNonEmpty()
    {
        var old = "";
        var current = "foo";

        bool hasChanged = DeltaPacker<string>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = "x";
        DeltaPacker<string>.Read(packer, old, ref result);
        Assert.AreEqual(current, result);
    }

    [Test]
    public void NonEmptyToEmpty()
    {
        var old = "foo";
        var current = "";

        bool hasChanged = DeltaPacker<string>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = "junk";
        DeltaPacker<string>.Read(packer, old, ref result);
        Assert.AreEqual("", result);
    }

    [Test]
    public void NullToNull_NoChange()
    {
        bool hasChanged = DeltaPacker<string>.Write(packer, null, null);
        Assert.IsFalse(hasChanged);
    }

    [Test]
    public void NullToValue()
    {
        string old = null;
        var current = "hello";

        bool hasChanged = DeltaPacker<string>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        string result = null;
        DeltaPacker<string>.Read(packer, old, ref result);
        Assert.AreEqual(current, result);
    }

    [Test]
    public void ValueToNull()
    {
        var old = "hello";
        string current = null;

        bool hasChanged = DeltaPacker<string>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = "junk";
        DeltaPacker<string>.Read(packer, old, ref result);
        Assert.IsNull(result);
    }

    [Test]
    public void LongString_Roundtrip()
    {
        var old = new string('a', 1000);
        var current = new string('b', 1000);

        bool hasChanged = DeltaPacker<string>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = "";
        DeltaPacker<string>.Read(packer, old, ref result);
        Assert.AreEqual(current, result);
    }

    [Test]
    public void SequentialStringChanges()
    {
        string[] versions = { "a", "ab", "abc", "abcd", "abc" };

        string old = versions[0];

        for (int i = 1; i < versions.Length; i++)
        {
            var current = versions[i];

            packer.ResetPositionAndMode(false);
            DeltaPacker<string>.Write(packer, old, current);

            packer.ResetPositionAndMode(true);

            var result = "";
            DeltaPacker<string>.Read(packer, old, ref result);

            Assert.AreEqual(current, result, $"Step {i}");
            old = result;
        }
    }

    // --- DisposableList<string> delta packing (Myers-based list diff) ---

    [Test]
    public void ListOfStrings_SameList_NoChange()
    {
        var old = DisposableList<string>.Create(new[] { "hello", "world" });
        var current = DisposableList<string>.Create(new[] { "hello", "world" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsFalse(hasChanged);

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void ListOfStrings_InsertMiddle()
    {
        var old = DisposableList<string>.Create(new[] { "hello", "world" });
        var current = DisposableList<string>.Create(new[] { "hello", "beautiful", "world" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("hello", result[0]);
        Assert.AreEqual("beautiful", result[1]);
        Assert.AreEqual("world", result[2]);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ListOfStrings_ReplaceElement()
    {
        var old = DisposableList<string>.Create(new[] { "foo", "bar", "baz" });
        var current = DisposableList<string>.Create(new[] { "foo", "quux", "baz" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("quux", result[1]);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ListOfStrings_EmptyToItems()
    {
        var old = DisposableList<string>.Create();
        var current = DisposableList<string>.Create(new[] { "a", "b", "c" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ListOfStrings_ItemsToEmpty()
    {
        var old = DisposableList<string>.Create(new[] { "x", "y" });
        var current = DisposableList<string>.Create();

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = DisposableList<string>.Create(new[] { "junk" });
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(0, result.Count);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ListOfStrings_EqualityByDelta()
    {
        var old = DisposableList<string>.Create(new[] { "one", "two" });
        var current = DisposableList<string>.Create(new[] { "one", "two", "three" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        old.Dispose();
        current.Dispose();
    }

    [Test]
    public void ListOfStrings_Append()
    {
        var old = DisposableList<string>.Create(new[] { "one", "two" });
        var current = DisposableList<string>.Create(new[] { "one", "two", "three" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("three", result[2]);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ListOfStrings_DeleteMiddle()
    {
        var old = DisposableList<string>.Create(new[] { "a", "b", "c" });
        var current = DisposableList<string>.Create(new[] { "a", "c" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(2, result.Count);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }

    [Test]
    public void ListOfStrings_EmptyElements()
    {
        var old = DisposableList<string>.Create(new[] { "a", "", "c" });
        var current = DisposableList<string>.Create(new[] { "a", "", "c", "d" });

        bool hasChanged = DeltaPacker<DisposableList<string>>.Write(packer, old, current);
        Assert.IsTrue(hasChanged);

        packer.ResetPositionAndMode(true);

        var result = default(DisposableList<string>);
        DeltaPacker<DisposableList<string>>.Read(packer, old, ref result);

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual("", result[1]);
        Assert.AreEqual("d", result[3]);
        CollectionAssert.AreEqual(current, result);

        old.Dispose();
        current.Dispose();
        result.Dispose();
    }
}
