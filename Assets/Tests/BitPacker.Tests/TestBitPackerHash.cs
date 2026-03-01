using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public class TestBitPackerHash
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
    public void Hash_Empty_IsDeterministic()
    {
        packer.ResetPositionAndMode(false);

        uint h1 = packer.GetDeterministicHash32();
        uint h2 = packer.GetDeterministicHash32();

        Assert.That(h2, Is.EqualTo(h1));
    }

    [Test]
    public void Hash_SameBytes_SameHash()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        uint h1 = packer.GetDeterministicHash32();

        using var other = BitPackerPool.Get();
        other.ResetPositionAndMode(false);
        Packer<string>.Write(other, "test");
        uint h2 = other.GetDeterministicHash32();

        Assert.That(h2, Is.EqualTo(h1));
    }

    [Test]
    public void Hash_DifferentContent_DifferentHash()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        uint h1 = packer.GetDeterministicHash32();

        using var other = BitPackerPool.Get();
        other.ResetPositionAndMode(false);
        Packer<string>.Write(other, "test2");
        uint h2 = other.GetDeterministicHash32();

        Assert.That(h2, Is.Not.EqualTo(h1));
    }

    [Test]
    public void Hash_DifferentBitLength_DifferentHash_EvenIfSamePrefix()
    {
        packer.ResetPositionAndMode(false);

        // Write 1 bit: 1
        packer.WriteBit(true);
        uint h1 = packer.GetDeterministicHash32();

        using var other = BitPackerPool.Get();
        other.ResetPositionAndMode(false);

        // Write 2 bits: 01 (same low bit = 1? no, low bit is 1 in first case; make 2-bit end collide-ish)
        // Let's instead do: 1 bit '1' vs 2 bits '1' then '0' -> low bits in first byte can match for some masks,
        // but bit-length must differ => hash must differ.
        other.WriteBit(true);
        other.WriteBit(false);

        uint h2 = other.GetDeterministicHash32();

        Assert.That(h2, Is.Not.EqualTo(h1));
    }

    [Test]
    public void Hash_NonByteAligned_IgnoresGarbageBitsAfterTail()
    {
        packer.ResetPositionAndMode(false);

        // 3 bits written => tail is partial.
        packer.WriteBit(true);  // bit0 = 1
        packer.WriteBit(false); // bit1 = 0
        packer.WriteBit(true);  // bit2 = 1

        uint h1 = packer.GetDeterministicHash32();

        // Mutate bits that are OUTSIDE the written region (same byte, higher bits)
        // by writing at absolute bit positions without changing positionInBits.
        // Written bits are 0..2; mutate bits 3..7.
        packer.WriteAt(3, true);
        packer.WriteAt(4, true);
        packer.WriteAt(5, false);
        packer.WriteAt(6, true);
        packer.WriteAt(7, true);

        uint h2 = packer.GetDeterministicHash32();

        Assert.That(h2, Is.EqualTo(h1));
    }

    [Test]
    public void Hash_ChangesWhenWrittenBitChanges_NonByteAligned()
    {
        packer.ResetPositionAndMode(false);

        packer.WriteBit(true);
        packer.WriteBit(false);
        packer.WriteBit(true);

        uint h1 = packer.GetDeterministicHash32();

        // Flip one written bit (bit1)
        packer.WriteAt(1, true);

        uint h2 = packer.GetDeterministicHash32();

        Assert.That(h2, Is.Not.EqualTo(h1));
    }

    [Test]
    public void Hash_Duplicate_SameHashAndDoesNotDependOnCapacity()
    {
        packer.ResetPositionAndMode(false);

        // Force some growth / capacity differences
        for (int i = 0; i < 500; i++)
            packer.WriteBit((i & 1) == 0);

        uint h1 = packer.GetDeterministicHash32();

        using var dup = packer.Duplicate();
        dup.SetBitPosition(packer.positionInBits);
        // Make capacity different again
        dup.EnsureBitsExist(dup.positionInBits + 10000);

        uint h2 = dup.GetDeterministicHash32();

        Assert.That(h2, Is.EqualTo(h1));
    }

    [Test]
    public void Hash_SameWrittenBits_DifferentTrailingBytes_NoEffect()
    {
        packer.ResetPositionAndMode(false);

        // Write exactly 9 bits so we have 1 full byte + 1 tail bit
        for (int i = 0; i < 9; i++)
            packer.WriteBit(i % 3 == 0);

        uint h1 = packer.GetDeterministicHash32();

        // Corrupt bytes AFTER the written region (byte index >= positionInBytes)
        // positionInBytes here is 2.
        int start = packer.positionInBytes;
        packer.buffer[start + 0] ^= 0xFF;
        packer.buffer[start + 1] ^= 0xAA;

        uint h2 = packer.GetDeterministicHash32();

        Assert.That(h2, Is.EqualTo(h1));
    }

    [Test]
    public void Hash_MatchesAfterCopyingBits_NonByteAligned()
    {
        packer.ResetPositionAndMode(false);

        // Create non-byte-aligned content
        packer.WriteBit(true);
        packer.WriteBits(0b101101u, 6); // total 7 bits
        packer.WriteBits(0xABCDu, 16);  // total 23 bits

        uint h1 = packer.GetDeterministicHash32();

        var bitData = new BitData(packer);

        using var copy = BitPackerPool.Get();
        copy.ResetPositionAndMode(false);
        copy.WriteBitDataWithoutConsumingIt(bitData);

        uint h2 = copy.GetDeterministicHash32();

        Assert.That(copy.positionInBits, Is.EqualTo(packer.positionInBits));
        Assert.That(h2, Is.EqualTo(h1));
    }

    [Test]
    public void Hash_DoesNotModifyPosition()
    {
        packer.ResetPositionAndMode(false);

        Packer<string>.Write(packer, "test");
        packer.WriteBits(0b10101u, 5);

        int posBefore = packer.positionInBits;
        _ = packer.GetDeterministicHash32();
        int posAfter = packer.positionInBits;

        Assert.That(posAfter, Is.EqualTo(posBefore));
    }
}
