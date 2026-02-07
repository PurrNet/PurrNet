using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;

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
