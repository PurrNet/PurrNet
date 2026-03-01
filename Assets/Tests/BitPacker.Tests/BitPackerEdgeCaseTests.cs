using System;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Transports;

/// <summary>
/// Edge-case tests for BitPacker ReadBits/WriteBits and string packing.
/// These exercises the fix for ReadBitsWithoutChecks reading 8 bytes (ulong) even when
/// EnsureBitsExist only required fewer bytes, which could cause out-of-bounds read/crash.
/// </summary>
public class BitPackerEdgeCaseTests
{
    private BitPacker _packer;

    [SetUp]
    public void Setup()
    {
        NetworkManager.LoadOrGenerateHashes();
        _packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        _packer?.Dispose();
    }

    /// <summary>
    /// Reading 56 bits with only 7 bytes in buffer: the ulong read needs 8 bytes.
    /// Without the read-path fix this can read 1 byte OOB. With fix we throw.
    /// </summary>
    [Test]
    public void ReadBits_56Bits_With7ByteBuffer_Throws()
    {
        var buf = new byte[7];
        _packer.MakeWrapper(new ByteData(buf, 0, 7));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(56));
    }

    /// <summary>
    /// Reading 8 bits with only 1 byte in buffer: we still do an 8-byte ulong read.
    /// Should throw (need 8 bytes), not read OOB.
    /// </summary>
    [Test]
    public void ReadBits_8Bits_With1ByteBuffer_Throws()
    {
        var buf = new byte[1];
        _packer.MakeWrapper(new ByteData(buf, 0, 1));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(8));
    }

    /// <summary>
    /// Reading 32 bits (e.g. string length) with only 4 bytes: ulong read needs 8 bytes.
    /// This is the kind of underflow that could crash when reading a string from a truncated packet.
    /// </summary>
    [Test]
    public void ReadBits_32Bits_With4ByteBuffer_Throws()
    {
        var buf = new byte[4];
        _packer.MakeWrapper(new ByteData(buf, 0, 4));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(32));
    }

    /// <summary>
    /// Reading 65 bits (overflow) with only 8 bytes: we need a 9th byte for the overflow.
    /// Should throw.
    /// </summary>
    [Test]
    public void ReadBits_65Bits_With8ByteBuffer_Throws()
    {
        var buf = new byte[8];
        _packer.MakeWrapper(new ByteData(buf, 0, 8));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(65));
    }

    /// <summary>
    /// Multiple ReadBits(8) in a row (like string chars). ReadBitsWithoutChecks always reads
    /// 8 bytes (ulong) per call, so we need a buffer large enough: for 3x ReadBits(8) at
    /// positions 0,8,16 we need at least 10 bytes (bytePos+8 for last read).
    /// </summary>
    [Test]
    public void ReadBits_Multiple8BitReads_ExactBuffer_Succeeds()
    {
        _packer.ResetPositionAndMode(false);
        _packer.WriteBits(0x41, 8); // 'A'
        _packer.WriteBits(0x42, 8); // 'B'
        _packer.WriteBits(0x43, 8); // 'C'
        int usedBytes = _packer.positionInBytes;
        Assert.GreaterOrEqual(usedBytes, 3);

        var buf = _packer.buffer;
        // Need at least 10 bytes for 3x ReadBits(8): each read needs bytePos+8 bytes
        int readBufferSize = 10;
        var readBuf = new byte[readBufferSize];
        for (int i = 0; i < usedBytes; i++)
            readBuf[i] = buf[i];

        using var readPacker = BitPackerPool.Get();
        readPacker.MakeWrapper(new ByteData(readBuf, 0, readBuf.Length));
        readPacker.ResetPositionAndMode(true);

        Assert.That(readPacker.ReadBits(8), Is.EqualTo(0x41UL));
        Assert.That(readPacker.ReadBits(8), Is.EqualTo(0x42UL));
        Assert.That(readPacker.ReadBits(8), Is.EqualTo(0x43UL));
    }

    /// <summary>
    /// String roundtrip: write then read. Uses bool + int + many ReadBits(8) for chars.
    /// </summary>
    [Test]
    public void String_WriteThenRead_Roundtrips()
    {
        NetworkManager.CallAllRegisters();
        _packer.ResetPositionAndMode(false);
        Packer<string>.Write(_packer, "test");
        _packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(_packer);
        Assert.That(read, Is.EqualTo("test"));
    }

    /// <summary>
    /// String roundtrip with a buffer that has enough bytes for the read path. Reading uses
    /// ReadBits (ulong) which needs bytePos+8 bytes per call; for "test" (1+32+4*8 bits) we
    /// need at least 15 bytes to read the chars without OOB, so we use written size + margin.
    /// </summary>
    [Test]
    public void String_WriteThenRead_ExactBuffer_Roundtrips()
    {
        NetworkManager.CallAllRegisters();
        _packer.ResetPositionAndMode(false);
        Packer<string>.Write(_packer, "test");
        int usedBytes = _packer.positionInBytes;
        var buf = _packer.buffer;
        // Read path needs extra bytes for ulong reads (e.g. last char at byte 7 needs 15 bytes)
        int readBufferSize = usedBytes + 8;
        var readBuf = new byte[readBufferSize];
        for (int i = 0; i < usedBytes; i++)
            readBuf[i] = buf[i];

        using var readPacker = BitPackerPool.Get();
        readPacker.MakeWrapper(new ByteData(readBuf, 0, readBuf.Length));
        readPacker.ResetPositionAndMode(true);
        var read = Packer<string>.Read(readPacker);
        Assert.That(read, Is.EqualTo("test"));
    }

    [Test]
    public void String_Empty_Roundtrips()
    {
        NetworkManager.CallAllRegisters();
        _packer.ResetPositionAndMode(false);
        Packer<string>.Write(_packer, "");
        _packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(_packer);
        Assert.That(read, Is.EqualTo(""));
    }

    [Test]
    public void String_Null_Roundtrips()
    {
        NetworkManager.CallAllRegisters();
        _packer.ResetPositionAndMode(false);
        Packer<string>.Write(_packer, null);
        _packer.ResetPositionAndMode(true);
        var read = Packer<string>.Read(_packer);
        Assert.That(read, Is.Null);
    }

    /// <summary>
    /// Truncated buffer: we wrote a string but only give half the bytes on read.
    /// Should throw, not crash.
    /// </summary>
    [Test]
    public void String_TruncatedBuffer_Throws()
    {
        NetworkManager.CallAllRegisters();
        _packer.ResetPositionAndMode(false);
        Packer<string>.Write(_packer, "test");
        int usedBytes = _packer.positionInBytes;
        var buf = _packer.buffer;
        var truncated = new byte[Math.Max(1, usedBytes / 2)];
        for (int i = 0; i < truncated.Length; i++)
            truncated[i] = buf[i];

        using var readPacker = BitPackerPool.Get();
        readPacker.MakeWrapper(new ByteData(truncated, 0, truncated.Length));
        readPacker.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => Packer<string>.Read(readPacker));
    }
}
