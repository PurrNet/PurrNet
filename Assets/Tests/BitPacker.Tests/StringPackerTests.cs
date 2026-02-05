using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;

public class StringPackerTests
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

    [Test]
    public void String_Roundtrip()
    {
        const string value = "hello";
        packer.ResetPositionAndMode(false);
        Packer<string>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(packer);
        Assert.AreEqual(value, read);
    }

    [Test]
    public void String_Empty_Roundtrip()
    {
        packer.ResetPositionAndMode(false);
        Packer<string>.Write(packer, "");
        packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(packer);
        Assert.AreEqual("", read);
    }

    [Test]
    public void String_Null_Roundtrip()
    {
        packer.ResetPositionAndMode(false);
        Packer<string>.Write(packer, null);
        packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(packer);
        Assert.IsNull(read);
    }

    [Test]
    public void String_Long_Roundtrip()
    {
        var value = new string('x', 500);
        packer.ResetPositionAndMode(false);
        Packer<string>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(packer);
        Assert.AreEqual(value, read);
    }

    [Test]
    public void String_MultipleSequential()
    {
        var values = new[] { "a", "bb", "ccc", "" };

        packer.ResetPositionAndMode(false);
        foreach (var v in values)
            Packer<string>.Write(packer, v);
        packer.ResetPositionAndMode(true);

        foreach (var expected in values)
        {
            var read = Packer<string>.Read(packer);
            Assert.AreEqual(expected, read);
        }
    }

    [Test]
    public void ListOfStrings_Roundtrip()
    {
        var value = new System.Collections.Generic.List<string> { "hello", "world", "" };

        packer.ResetPositionAndMode(false);
        Packer<System.Collections.Generic.List<string>>.Write(packer, value);
        packer.ResetPositionAndMode(true);

        var read = default(System.Collections.Generic.List<string>);
        Packer<System.Collections.Generic.List<string>>.Read(packer, ref read);

        Assert.AreEqual(value.Count, read.Count);
        for (int i = 0; i < value.Count; i++)
            Assert.AreEqual(value[i], read[i]);
    }
}
