using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public class TestBitData
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        NetworkManager.CalculateHashes();
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void TestSimpleReadback()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        var bitForTest = new BitData(packer);

        using (bitForTest.AutoScope())
        {
            var read = Packer<string>.Read(packer);
            Assert.That(read, Is.EqualTo("test"));
        }
    }

    [Test]
    public void TestSimpleCopy()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        var bitForTest = new BitData(packer);

        using var copy = BitPackerPool.Get();
        copy.WriteBitDataWithoutConsumingIt(bitForTest);

        int originalLen = bitForTest.bitLength;
        int myLen = copy.positionInBits;

        Assert.That(myLen, Is.EqualTo(originalLen));

        copy.ResetPositionAndMode(true);

        var read = Packer<string>.Read(copy);
        Assert.That(read, Is.EqualTo("test"));
    }

    [Test]
    public void TestPackingBitData()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        int actualStart = packer.positionInBits;
        Packer<string>.Write(packer, "actual");
        int actualEnd = packer.positionInBits;
        var bitForTest = new BitData(packer, actualStart, actualEnd - actualStart);

        using var copy = BitPackerPool.Get();
        Packer<BitData>.Write(copy, bitForTest);
        copy.ResetPositionAndMode(true);
        var read = Packer<BitData>.Read(copy);

        using (read.AutoScope())
        {
            var result = Packer<string>.Read(read.packer);
            Assert.That(result, Is.EqualTo("actual"));
        }
    }

    [Test]
    public void TestPackingBitPacker()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        Packer<string>.Write(packer, "actual");

        using var copy = BitPackerPool.Get();
        Packer<BitPacker>.Write(copy, packer);
        copy.ResetPositionAndMode(true);
        var readPacker = Packer<BitPacker>.Read(copy);

        var resultTest = Packer<string>.Read(readPacker);
        var resultActual = Packer<string>.Read(readPacker);
        Assert.That(resultTest, Is.EqualTo("test"));
        Assert.That(resultActual, Is.EqualTo("actual"));
    }
}
