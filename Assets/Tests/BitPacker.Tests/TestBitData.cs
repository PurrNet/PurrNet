using System;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public class TestBitData
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        NetworkManager.LoadOrGenerateHashes();
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

    [Test]
    public void TestAlignedCopyPreservesPartialByteTail()
    {
        using var source = BitPackerPool.Get();
        using var destination = BitPackerPool.Get();
        const ulong value = 0b1_0110_1101_0011;

        source.WriteBits(value, 13);
        int sourcePosition = source.positionInBits;

        destination.WriteBits(0xAB, 8);
        destination.WriteBits(0xFFFF, 16);
        destination.SetBitPosition(8);
        destination.WriteBitDataWithoutConsumingIt(new BitData(source, 0, 13));

        Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
        Assert.That(destination.positionInBits, Is.EqualTo(21));

        destination.ResetPositionAndMode(true);
        Assert.That(destination.ReadBits(8), Is.EqualTo(0xAB));
        Assert.That(destination.ReadBits(13), Is.EqualTo(value));
        Assert.That(destination.ReadBits(3), Is.EqualTo(0b111));
    }

    [Test]
    public void TestUnalignedBitDataCopyPreservesSourcePosition()
    {
        using var source = BitPackerPool.Get();
        using var destination = BitPackerPool.Get();
        const ulong first = 0xFEDCBA9876543210;
        const ulong second = 0b1_1010_0110_1101;

        source.WriteBits(0b10101, 5);
        int origin = source.positionInBits;
        source.WriteBits(first, 64);
        source.WriteBits(second, 13);
        int sourcePosition = source.positionInBits;

        destination.WriteBits(0b101, 3);
        destination.WriteBitDataWithoutConsumingIt(new BitData(source, origin, 77));

        Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
        destination.ResetPositionAndMode(true);
        Assert.That(destination.ReadBits(3), Is.EqualTo(0b101));
        Assert.That(destination.ReadBits(64), Is.EqualTo(first));
        Assert.That(destination.ReadBits(13), Is.EqualTo(second));
    }

    [Test]
    public void TestUnalignedBitPackerCopyPreservesSourcePosition()
    {
        using var source = BitPackerPool.Get();
        using var destination = BitPackerPool.Get();
        const ulong value = 0x0123456789ABCDEF;

        source.WriteBits(value, 64);
        int sourcePosition = source.positionInBits;
        destination.WriteBits(0b11, 2);
        destination.WriteBitsWithoutConsumingIt(source, 64);

        Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
        destination.ResetPositionAndMode(true);
        Assert.That(destination.ReadBits(2), Is.EqualTo(0b11));
        Assert.That(destination.ReadBits(64), Is.EqualTo(value));
    }

    [Test]
    public void TestLargeByteAlignedSourceCopyIntoUnalignedDestination()
    {
        using var source = BitPackerPool.Get();
        using var destination = BitPackerPool.Get();
        const ulong first = 0x0123456789ABCDEF;
        const ulong second = 0xFEDCBA9876543210;
        const ulong third = 0xA55AA55AA55AA55A;

        source.WriteBits(first, 64);
        source.WriteBits(second, 64);
        source.WriteBits(third, 64);
        source.WriteBits(0b1_1011, 5);
        int sourcePosition = source.positionInBits;

        destination.WriteBits(0b101, 3);
        destination.WriteBitsWithoutConsumingIt(source, sourcePosition);

        Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
        destination.ResetPositionAndMode(true);
        Assert.That(destination.ReadBits(3), Is.EqualTo(0b101));
        Assert.That(destination.ReadBits(64), Is.EqualTo(first));
        Assert.That(destination.ReadBits(64), Is.EqualTo(second));
        Assert.That(destination.ReadBits(64), Is.EqualTo(third));
        Assert.That(destination.ReadBits(5), Is.EqualTo(0b1_1011));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestCopyGrowsSmallWriteModeSourceForDeclaredRange(bool unaligned)
    {
        using var source = BitPackerPool.Get(new byte[1]);
        using var destination = BitPackerPool.Get();
        source.ResetMode(false);
        source.SetBitPosition(3);

        int bitOrigin = unaligned ? 1 : 0;
        if (unaligned)
            destination.WriteBit(true);

        destination.WriteBitDataWithoutConsumingIt(new BitData(source, bitOrigin, 200));

        Assert.That(source.positionInBits, Is.EqualTo(3));
        Assert.That(source.buffer.Length * 8, Is.GreaterThanOrEqualTo(bitOrigin + 200));
        Assert.That(destination.positionInBits, Is.EqualTo(200 + (unaligned ? 1 : 0)));

        destination.ResetPositionAndMode(true);
        if (unaligned)
            Assert.That(destination.ReadBit(), Is.True);
        for (int i = 0; i < 200; i++)
            Assert.That(destination.ReadBit(), Is.False);
    }

    [Test]
    public void TestCopyRejectsOutOfRangeReadModeSourceWithoutMovingIt()
    {
        using var source = BitPackerPool.Get(new byte[1]);
        using var destination = BitPackerPool.Get();
        source.SetBitPosition(3);
        destination.WriteBit(true);

        Assert.Throws<IndexOutOfRangeException>(() =>
            destination.WriteBitDataWithoutConsumingIt(new BitData(source, 1, 200)));
        Assert.That(source.positionInBits, Is.EqualTo(3));
    }

    [Test]
    public void TestCopyAcrossAlignmentAndChunkBoundaries()
    {
        using var source = BitPackerPool.Get();
        using var destination = BitPackerPool.Get();
        var lengths = new[] { 1, 5, 8, 9, 63, 64, 65, 79, 80, 81, 127, 128, 129, 197 };

        for (int i = 0; i < 32; i++)
            source.WriteBits((ulong)(byte)(i * 37 + 11), 8);
        int sourcePosition = source.positionInBits;

        for (int sourceOrigin = 0; sourceOrigin < 8; sourceOrigin++)
        {
            for (int destinationOffset = 0; destinationOffset < 8; destinationOffset++)
            {
                for (int lengthIdx = 0; lengthIdx < lengths.Length; lengthIdx++)
                {
                    int length = lengths[lengthIdx];
                    destination.ResetPositionAndMode(false);

                    for (int i = 0; i < destinationOffset + length + 8; i++)
                        destination.WriteBit(true);
                    destination.SetBitPosition(0);
                    for (int i = 0; i < destinationOffset; i++)
                        destination.WriteBit((i & 1) == 0);
                    destination.SetBitPosition(destinationOffset);

                    destination.WriteBitDataWithoutConsumingIt(new BitData(source, sourceOrigin, length));

                    Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
                    Assert.That(destination.positionInBits, Is.EqualTo(destinationOffset + length));
                    destination.ResetPositionAndMode(true);
                    for (int i = 0; i < destinationOffset; i++)
                        Assert.That(destination.ReadBit(), Is.EqualTo((i & 1) == 0));

                    source.SetBitPosition(sourceOrigin);
                    for (int i = 0; i < length; i++)
                    {
                        Assert.That(destination.ReadBit(), Is.EqualTo(source.ReadBit()),
                            $"Mismatch at source origin {sourceOrigin}, destination offset {destinationOffset}, " +
                            $"length {length}, copied bit {i}.");
                    }
                    source.SetBitPosition(sourcePosition);

                    for (int i = 0; i < 8; i++)
                        Assert.That(destination.ReadBit(), Is.True,
                            $"Copy overwrote trailing sentinel bit {i} at source origin {sourceOrigin}, " +
                            $"destination offset {destinationOffset}, length {length}.");
                }
            }
        }
    }
}
