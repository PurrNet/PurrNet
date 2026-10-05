using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
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

    [Test]
    public void Wrapper_LogicalSliceSurvivesReadResetsAndViews()
    {
        var buffer = new byte[32];
        Array.Fill(buffer, (byte)0xFF);
        buffer[2] = 0xA5;
        buffer[3] = 0x5A;
        _packer.MakeWrapper(new ByteData(buffer, 2, 2));

        Assert.That(_packer.isReading, Is.True);
        Assert.That(_packer.positionInBits, Is.EqualTo(16));
        Assert.That(_packer.length, Is.EqualTo(2));
        Assert.That(_packer.remainingBits, Is.EqualTo(16));
        Assert.That(_packer.remainingBytes, Is.EqualTo(2));
        Assert.That(_packer.ReadBit(), Is.True);
        Assert.That(_packer.remainingBits, Is.EqualTo(15));
        Assert.That(_packer.remainingBytes, Is.EqualTo(1));

        _packer.ResetPosition();
        Assert.That(_packer.ReadBits(8), Is.EqualTo(0xA5UL));
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBits(16), Is.EqualTo(0x5AA5UL));
        Assert.That(_packer.remainingBits, Is.Zero);
        Assert.That(_packer.length, Is.EqualTo(2));

        var bytes = _packer.ToByteData();
        Assert.That(bytes.offset, Is.EqualTo(2));
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0x5A }, bytes.span.ToArray());
        Assert.That(_packer.AsSegment().Offset, Is.EqualTo(2));
        Assert.That(_packer.AsSegment().Count, Is.EqualTo(2));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
        Assert.That(_packer.positionInBits, Is.EqualTo(32));
    }

    [Test]
    public void Wrapper_AllPublicReadsRejectStaleTailWithoutMovingCursor()
    {
        var buffer = new byte[64];
        Array.Fill(buffer, (byte)0xFF);
        _packer.MakeWrapper(new ByteData(buffer, 3, 1));
        _packer.ReadBits(7);
        int before = _packer.positionInBits;

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(2));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBitsWithoutChecks(2));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBytes(new byte[1]));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.EnsureBitsExist(2));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.AdvanceBits(2));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.GetMemory(1));
        Assert.Throws<IndexOutOfRangeException>(() => { _packer.GetSpan(1); });
        Assert.That(_packer.positionInBits, Is.EqualTo(before));
        Assert.That(_packer.ReadBit(), Is.True);
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
    }

    [Test]
    public void Wrapper_PaddingAllocationDoesNotExtendReadableData()
    {
        _packer.MakeWrapper(new ByteData(new byte[1], 0, 1));
        _packer.AdvanceBytes(1);
        _packer.EnsurePadding();

        Assert.That(_packer.buffer.Length, Is.GreaterThan(1));
        Assert.That(_packer.remainingBits, Is.Zero);
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(1));
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.remainingBits, Is.EqualTo(8));
    }

    [Test]
    public void Wrapper_EmptySliceAllowsZeroReadsOnly()
    {
        _packer.MakeWrapper(new ByteData(new byte[16], 5, 0));
        Assert.That(_packer.ReadBits(0), Is.Zero);
        Assert.That(_packer.ReadBitsWithoutChecks(0), Is.Zero);
        _packer.ReadBytes(Span<byte>.Empty);
        Assert.That(_packer.GetMemory(0).Length, Is.Zero);
        Assert.That(_packer.positionInBits, Is.EqualTo(40));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());

        _packer.MakeWrapper(default);
        Assert.That(_packer.ReadBits(0), Is.Zero);
        Assert.That(_packer.buffer, Is.Empty);
    }

    [Test]
    public void Wrapper_RejectsInvalidSlicesBeforeReplacingData()
    {
        _packer.MakeWrapper(new ByteData(new byte[8], 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => _packer.MakeWrapper(new ByteData(new byte[8], -1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _packer.MakeWrapper(new ByteData(new byte[8], 9, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _packer.MakeWrapper(new ByteData(new byte[8], 1, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _packer.MakeWrapper(new ByteData(new byte[8], 1, int.MaxValue)));
        Assert.That(_packer.positionInBits, Is.EqualTo(8));
        Assert.That(_packer.remainingBits, Is.EqualTo(16));
    }

    [Test]
    public void Wrapper_SeekAndSkipValidateSliceAndArithmetic()
    {
        _packer.MakeWrapper(new ByteData(new byte[32], 2, 2));
        int before = _packer.positionInBits;
        Assert.Throws<IndexOutOfRangeException>(() => _packer.SetBitPosition(0));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.SetBitPosition(33));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.SkipBits(int.MaxValue));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.SkipBits(int.MinValue));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.SkipBytes(int.MaxValue));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.SkipBytes(uint.MaxValue));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.EnsureBitsExist(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => _packer.AdvanceBytes(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => _packer.AdvanceBits(-1));
        Assert.That(_packer.positionInBits, Is.EqualTo(before));
        _packer.SkipBytes(2);
        _packer.SkipBytes(-1);
        Assert.That(_packer.remainingBits, Is.EqualTo(8));
    }

    [Test]
    public void Writer_ResetDropsPriorMessageAndKeepsFinalBytePadding()
    {
        _packer.WriteBytes(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        _packer.ResetPositionAndMode(false);
        _packer.WriteBits(5, 3);
        _packer.ResetPositionAndMode(true);

        Assert.That(_packer.length, Is.EqualTo(1));
        Assert.That(_packer.remainingBits, Is.EqualTo(8));
        Assert.That(_packer.ReadBits(3), Is.EqualTo(5UL));
        Assert.That(_packer.remainingBits, Is.EqualTo(5));
        _packer.ReadBits(5);
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
    }

    [Test]
    public void Writer_BufferReservationRequiresAdvanceToBecomeReadable()
    {
        var span = _packer.GetSpan(2);
        span[0] = 0xA5;
        span[1] = 0x5A;
        Assert.That(_packer.remainingBits, Is.Zero);
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
        _packer.AdvanceBytes(2);
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBits(16), Is.EqualTo(0x5AA5UL));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(1));
    }

    [Test]
    public void Writer_ReadScopePreservesHighWaterAndModeChangesDoNotExpandIt()
    {
        _packer.WriteBits(0xA5, 8);
        using (new BitData(_packer).AutoScope())
        {
            Assert.That(_packer.isWriting, Is.True);
            Assert.That(_packer.ReadBits(8), Is.EqualTo(0xA5UL));
            Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
        }
        _packer.SetBitPosition(0);
        _packer.WriteBits(5, 3);
        _packer.ResetMode(true);
        Assert.That(_packer.remainingBits, Is.EqualTo(5));
        _packer.SkipBits(5);
        _packer.ResetMode(false);
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
    }

    [Test]
    public void Pool_ReusedWriterDoesNotExposePreviousMessage()
    {
        var previous = BitPackerPool.Get();
        previous.WriteBytes(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        previous.ResetPositionAndMode(true);
        previous.Dispose();

        using var next = BitPackerPool.Get(true);
        Assert.That(next, Is.SameAs(previous));
        Assert.That(next.remainingBits, Is.Zero);
        Assert.Throws<IndexOutOfRangeException>(() => next.ReadBit());
        next.ResetPositionAndMode(false);
        next.WriteBits(0xA5, 8);
        next.ResetPositionAndMode(true);
        Assert.That(next.ReadBits(8), Is.EqualTo(0xA5UL));
        Assert.Throws<IndexOutOfRangeException>(() => next.ReadBits(8));
    }

    [Test]
    public void Pool_ReusedWrapperDoesNotExposeLongerMessageTail()
    {
        var buffer = new byte[32];
        Array.Fill(buffer, (byte)0xFF);
        var previous = BitPackerPool.Get(new ByteData(buffer, 2, 20));
        previous.Dispose();

        using var next = BitPackerPool.Get(new ByteData(buffer, 2, 1));
        Assert.That(next, Is.SameAs(previous));
        Assert.That(next.ReadBits(8), Is.EqualTo(0xFFUL));
        Assert.Throws<IndexOutOfRangeException>(() => next.ReadBitsWithoutChecks(8));
    }

    [Test]
    public void Duplicate_CopiesLogicalSliceAndPreservesBoundWithZeroCursor()
    {
        _packer.MakeWrapper(new ByteData(new byte[] { 0xFF, 0xA5, 0x5A, 0xFF }, 1, 2));
        _packer.ReadBits(8);
        using var duplicate = _packer.Duplicate();

        Assert.That(duplicate.positionInBits, Is.Zero);
        Assert.That(duplicate.remainingBits, Is.EqualTo(16));
        Assert.That(duplicate.ReadBits(16), Is.EqualTo(0x5AA5UL));
        Assert.Throws<IndexOutOfRangeException>(() => duplicate.ReadBits(1));
        duplicate.ResetPositionAndMode(true);
        Assert.That(duplicate.remainingBits, Is.EqualTo(16));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void Copy_RejectsReadModeSourceBeyondLogicalEnd(int destinationOffset)
    {
        using var source = BitPackerPool.Get(new ByteData(new byte[64], 2, 1));
        _packer.AdvanceBits(destinationOffset);
        int before = _packer.positionInBits;

        Assert.Throws<IndexOutOfRangeException>(() =>
            _packer.WriteBitDataWithoutConsumingIt(new BitData(source, 16, 9)));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.WriteBits(source, 9));
        Assert.That(_packer.positionInBits, Is.EqualTo(before));
        Assert.That(source.positionInBits, Is.EqualTo(16));
    }

    [Test]
    public void Copy_ConvenienceMethodUsesWrapperLogicalOrigin()
    {
        using var source = BitPackerPool.Get(new ByteData(new byte[] { 0xFF, 0xA5, 0x5A, 0xFF }, 1, 2));
        source.ReadBit();
        int sourcePosition = source.positionInBits;
        _packer.WriteBit(true);
        _packer.WriteBitsWithoutConsumingIt(source, 16);

        Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBit(), Is.True);
        Assert.That(_packer.ReadBits(16), Is.EqualTo(0x5AA5UL));
    }

    [Test]
    public void Wrapper_UnalignedReadBytesRejectsTruncatedLogicalPayload()
    {
        _packer.MakeWrapper(new ByteData(new byte[64], 2, 9));
        _packer.ReadBit();
        var bytes = new byte[8];
        _packer.ReadBytes(bytes);
        Assert.That(_packer.remainingBits, Is.EqualTo(7));
        int before = _packer.positionInBits;
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBytes(new byte[1]));
        Assert.That(_packer.positionInBits, Is.EqualTo(before));
    }

    [Test]
    public void BufferWriter_AdvanceCommitsBoundsAndRejectsOverflow()
    {
        var writer = new BitPackerWrapper(_packer);
        writer.GetSpan(1)[0] = 0xA5;
        writer.Advance(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(int.MaxValue));
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBits(8), Is.EqualTo(0xA5UL));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBit());
    }

    [Test]
    public void Wrapper_InBoundsWritesWorkWithoutPhysicalPadding()
    {
        var buffer = new byte[1];
        _packer.MakeWrapper(new ByteData(buffer, 0, 1));
        _packer.WriteBitsWithoutChecks(5, 3);
        _packer.WriteBits(0x1F, 5);
        Assert.That(buffer[0], Is.EqualTo(0xFD));
        Assert.Throws<IndexOutOfRangeException>(() => _packer.WriteBit(true));
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBits(8), Is.EqualTo(0xFDUL));
    }

    [Test]
    public void Wrapper_UnalignedCopyWorksWithoutPhysicalPadding()
    {
        var bytes = new byte[] { 0xA5, 1, 2, 3, 4, 5, 6, 7, 8, 0x5A };
        using var source = BitPackerPool.Get();
        source.WriteBytes(bytes);
        _packer.MakeWrapper(new ByteData(new byte[11], 0, 11));
        _packer.AdvanceBit();
        _packer.WriteBitsWithoutConsumingIt(source, 80);

        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBit(), Is.False);
        var actual = new byte[10];
        _packer.ReadBytes(actual);
        CollectionAssert.AreEqual(bytes, actual);
        Assert.That(_packer.remainingBits, Is.EqualTo(7));
    }

    /// <summary>
    /// Reading 56 bits with only 7 bytes in buffer: the ulong read needs 8 bytes.
    /// Without the read-path fix this can read 1 byte OOB. With fix we throw.
    /// </summary>
    [Test]
    public void ReadBits_57Bits_With7ByteBuffer_Throws()
    {
        var buf = new byte[7];
        _packer.MakeWrapper(new ByteData(buf, 0, 7));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(57));
    }

    /// <summary>
    /// Reading 8 bits with only 1 byte in buffer: we still do an 8-byte ulong read.
    /// Should throw (need 8 bytes), not read OOB.
    /// </summary>
    [Test]
    public void ReadBits_9Bits_With1ByteBuffer_Throws()
    {
        var buf = new byte[1];
        _packer.MakeWrapper(new ByteData(buf, 0, 1));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(9));
    }

    /// <summary>
    /// Reading 32 bits (e.g. string length) with only 4 bytes: ulong read needs 8 bytes.
    /// This is the kind of underflow that could crash when reading a string from a truncated packet.
    /// </summary>
    [Test]
    public void ReadBits_33Bits_With4ByteBuffer_Throws()
    {
        var buf = new byte[4];
        _packer.MakeWrapper(new ByteData(buf, 0, 4));
        _packer.ResetPositionAndMode(true);

        Assert.Throws<IndexOutOfRangeException>(() => _packer.ReadBits(33));
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

    [Test]
    public void FragmentationLayer_Unfragmented_Roundtrips()
    {
        using var layer = new FragmentationLayer();
        var payload = new byte[] { 10, 20, 30, 40, 50 };
        var source = new ByteData(payload, 1, 3);
        var fragments = new List<byte[]>();

        layer.Send(source, 16, fragment => Capture(fragment, fragments));

        Assert.AreEqual(1, fragments.Count);
        Assert.AreEqual(0, fragments[0][0]);

        Assert.IsTrue(layer.Receive(new ByteData(fragments[0], 0, fragments[0].Length), out var assembled));
        AssertByteDataEquals(source, assembled);
    }

    [Test]
    public void FragmentationLayer_Unfragmented_DoesNotAliasReturnedBitPackerBuffer()
    {
        using var layer = new FragmentationLayer();
        var expected = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var sourcePacker = BitPackerPool.Get();
        sourcePacker.WriteBytes(expected);
        var source = sourcePacker.ToByteData();
        sourcePacker.Dispose();

        var fragments = new List<byte[]>();
        layer.Send(source, 16, fragment => Capture(fragment, fragments));

        Assert.AreEqual(1, fragments.Count);
        Assert.AreEqual(0, fragments[0][0]);

        var assembled = new ByteData(fragments[0], 1, expected.Length);
        AssertByteDataEquals(new ByteData(expected, 0, expected.Length), assembled);
    }

    [Test]
    public void FragmentationLayer_Fragmented_DoesNotAliasReturnedBitPackerBuffer()
    {
        using var layer = new FragmentationLayer();
        var expected = new byte[37];
        for (int i = 0; i < expected.Length; i++)
            expected[i] = (byte)(i * 7 + 3);

        var sourcePacker = BitPackerPool.Get();
        sourcePacker.WriteBytes(expected);
        var source = sourcePacker.ToByteData();
        sourcePacker.Dispose();

        var fragments = new List<byte[]>();
        layer.Send(source, 24, fragment => Capture(fragment, fragments));

        Assert.Greater(fragments.Count, 1);

        ByteData assembled = default;
        for (int i = 0; i < fragments.Count; i++)
        {
            var completed = layer.Receive(new ByteData(fragments[i], 0, fragments[i].Length), out assembled);
            Assert.AreEqual(i == fragments.Count - 1, completed);
        }

        AssertByteDataEquals(new ByteData(expected, 0, expected.Length), assembled);
    }

    [Test]
    public void FragmentationLayer_FragmentedOutOfOrder_Roundtrips()
    {
        using var layer = new FragmentationLayer();
        var payload = new byte[43];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i * 3 + 7);

        var source = new ByteData(payload, 3, 37);
        var fragments = new List<byte[]>();

        layer.Send(source, 24, fragment => Capture(fragment, fragments));

        Assert.Greater(fragments.Count, 1);

        ByteData assembled = default;
        for (int i = fragments.Count - 1; i >= 0; i--)
        {
            var completed = layer.Receive(new ByteData(fragments[i], 0, fragments[i].Length), out assembled);
            Assert.AreEqual(i == 0, completed);
        }

        AssertByteDataEquals(source, assembled);
    }

    [Test]
    public void FragmentationLayer_MissingFragment_DoesNotDeliverAndCleansUp()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payload = new byte[64];
        var fragments = new List<byte[]>();

        sender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, fragments));
        Assert.Greater(fragments.Count, 2);

        for (int i = 0; i < fragments.Count; i++)
        {
            if (i == 1)
                continue;

            Assert.IsFalse(receiver.Receive(new ByteData(fragments[i], 0, fragments[i].Length), out _));
        }

        Assert.AreEqual(1, receiver.pendingCount);
        Assert.AreEqual(payload.Length, receiver.pendingBytes);
        receiver.CleanupStale(0);
        Assert.AreEqual(0, receiver.pendingCount);
        Assert.AreEqual(0, receiver.pendingBytes);
    }

    [Test]
    public void FragmentationLayer_DuplicateFragment_IsIgnored()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payload = new byte[37];
        var fragments = new List<byte[]>();
        sender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, fragments));

        var first = new ByteData(fragments[0], 0, fragments[0].Length);
        Assert.IsFalse(receiver.Receive(first, out _));
        Assert.IsFalse(receiver.Receive(first, out _));

        ByteData assembled = default;
        for (int i = 1; i < fragments.Count; i++)
            Assert.AreEqual(i == fragments.Count - 1,
                receiver.Receive(new ByteData(fragments[i], 0, fragments[i].Length), out assembled));

        AssertByteDataEquals(new ByteData(payload, 0, payload.Length), assembled);
    }

    [Test]
    public void FragmentationLayer_SeparatesIdenticalMessageIdsBySender()
    {
        using var senderA = new FragmentationLayer();
        using var senderB = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payloadA = new byte[31];
        var payloadB = new byte[31];
        for (int i = 0; i < payloadA.Length; i++)
        {
            payloadA[i] = (byte)i;
            payloadB[i] = (byte)(255 - i);
        }

        var fragmentsA = new List<byte[]>();
        var fragmentsB = new List<byte[]>();
        senderA.Send(new ByteData(payloadA, 0, payloadA.Length), 24, f => Capture(f, fragmentsA));
        senderB.Send(new ByteData(payloadB, 0, payloadB.Length), 24, f => Capture(f, fragmentsB));

        ByteData assembledA = default;
        ByteData assembledB = default;
        for (int i = 0; i < fragmentsA.Count; i++)
        {
            bool completedA = receiver.Receive(10, 0, false,
                new ByteData(fragmentsA[i], 0, fragmentsA[i].Length), out var currentA);
            if (completedA) assembledA = new ByteData((byte[])currentA.span.ToArray(), 0, currentA.length);

            bool completedB = receiver.Receive(20, 0, false,
                new ByteData(fragmentsB[i], 0, fragmentsB[i].Length), out var currentB);
            if (completedB) assembledB = new ByteData((byte[])currentB.span.ToArray(), 0, currentB.length);
        }

        AssertByteDataEquals(new ByteData(payloadA, 0, payloadA.Length), assembledA);
        AssertByteDataEquals(new ByteData(payloadB, 0, payloadB.Length), assembledB);
    }

    [Test]
    public void FragmentationLayer_SequencedNewerMessageInvalidatesOlderAssembly()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var oldPayload = new byte[35];
        var newPayload = new byte[35];
        for (int i = 0; i < newPayload.Length; i++)
            newPayload[i] = (byte)(i + 100);

        var oldFragments = new List<byte[]>();
        var newFragments = new List<byte[]>();
        sender.Send(new ByteData(oldPayload, 0, oldPayload.Length), 24, f => Capture(f, oldFragments));
        sender.Send(new ByteData(newPayload, 0, newPayload.Length), 24, f => Capture(f, newFragments));

        Assert.IsFalse(receiver.Receive(7, 1, true,
            new ByteData(oldFragments[0], 0, oldFragments[0].Length), out _));
        Assert.IsFalse(receiver.Receive(7, 1, true,
            new ByteData(newFragments[0], 0, newFragments[0].Length), out _));

        for (int i = 1; i < oldFragments.Count; i++)
            Assert.IsFalse(receiver.Receive(7, 1, true,
                new ByteData(oldFragments[i], 0, oldFragments[i].Length), out _));

        ByteData assembled = default;
        for (int i = 1; i < newFragments.Count; i++)
            Assert.AreEqual(i == newFragments.Count - 1,
                receiver.Receive(7, 1, true,
                    new ByteData(newFragments[i], 0, newFragments[i].Length), out assembled));

        AssertByteDataEquals(new ByteData(newPayload, 0, newPayload.Length), assembled);
        Assert.AreEqual(0, receiver.pendingCount);
    }

    [Test]
    public void FragmentationLayer_PerSenderPendingMessagesAreBoundedAndReleasedOnDisconnect()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payload = new byte[37];
        var fragments = new List<byte[]>();

        for (int message = 0; message < 20; message++)
        {
            fragments.Clear();
            sender.Send(new ByteData(payload, 0, payload.Length), 24, f => Capture(f, fragments));
            Assert.IsFalse(receiver.Receive(99, 0, false,
                new ByteData(fragments[0], 0, fragments[0].Length), out _));
        }

        Assert.AreEqual(16, receiver.pendingCount);
        Assert.AreEqual(16 * payload.Length, receiver.pendingBytes);

        receiver.RemoveSender(99);
        Assert.AreEqual(0, receiver.pendingCount);
        Assert.AreEqual(0, receiver.pendingBytes);
    }

    [Test]
    public void FragmentationLayer_GlobalPendingMessageCountIsBoundedAcrossSenders()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payload = new byte[37];
        var fragments = new List<byte[]>();

        for (int senderId = 0; senderId < 140; senderId++)
        {
            fragments.Clear();
            sender.Send(new ByteData(payload, 0, payload.Length), 24, f => Capture(f, fragments));
            Assert.IsFalse(receiver.Receive(senderId, 0, false,
                new ByteData(fragments[0], 0, fragments[0].Length), out _));
        }

        Assert.AreEqual(128, receiver.pendingCount);
        Assert.AreEqual(128 * payload.Length, receiver.pendingBytes);
    }

    [Test]
    public void FragmentationLayer_MalformedLengthIsRejectedBeforeAllocating()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payload = new byte[37];
        var fragments = new List<byte[]>();
        sender.Send(new ByteData(payload, 0, payload.Length), 24, f => Capture(f, fragments));

        byte[] malformed = fragments[0];
        int invalidLength = FragmentationLayer.MAX_MESSAGE_SIZE + 1;
        malformed[5] = (byte)invalidLength;
        malformed[6] = (byte)(invalidLength >> 8);
        malformed[7] = (byte)(invalidLength >> 16);
        malformed[8] = (byte)(invalidLength >> 24);

        Assert.IsFalse(receiver.Receive(new ByteData(malformed, 0, malformed.Length), out _));
        Assert.AreEqual(0, receiver.pendingCount);
        Assert.AreEqual(0, receiver.pendingBytes);
    }

    [Test]
    public void FragmentationLayer_MaximumFragmentCountBoundaryRoundtrips()
    {
        const int mtu = 24;
        int maxLength = FragmentationLayer.GetMaxMessageSize(mtu);
        var payload = new byte[maxLength];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i * 13 + 5);

        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var fragments = new List<byte[]>();
        sender.Send(new ByteData(payload, 0, payload.Length), mtu, f => Capture(f, fragments));

        Assert.AreEqual(FragmentationLayer.MAX_FRAGMENTS, fragments.Count);
        ByteData assembled = default;
        for (int i = fragments.Count - 1; i >= 0; i--)
            Assert.AreEqual(i == 0,
                receiver.Receive(new ByteData(fragments[i], 0, fragments[i].Length), out assembled));

        AssertByteDataEquals(new ByteData(payload, 0, payload.Length), assembled);

        var tooLarge = new byte[maxLength + 1];
        Assert.Throws<ArgumentException>(() =>
            sender.Send(new ByteData(tooLarge, 0, tooLarge.Length), mtu, _ => { }));
    }

    [Test]
    public void FragmentationLayer_ConcurrentUnorderedMessagesFromOneSenderRemainIndependent()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payloadA = new byte[41];
        var payloadB = new byte[41];
        for (int i = 0; i < payloadA.Length; i++)
        {
            payloadA[i] = (byte)(i + 1);
            payloadB[i] = (byte)(200 - i);
        }

        var fragmentsA = new List<byte[]>();
        var fragmentsB = new List<byte[]>();
        sender.Send(new ByteData(payloadA, 0, payloadA.Length), 24, f => Capture(f, fragmentsA));
        sender.Send(new ByteData(payloadB, 0, payloadB.Length), 24, f => Capture(f, fragmentsB));

        byte[] assembledA = null;
        byte[] assembledB = null;
        for (int i = fragmentsA.Count - 1; i >= 0; i--)
        {
            if (receiver.Receive(3, 0, false,
                    WithOffset(fragmentsA[i], 3), out var currentA))
                assembledA = currentA.span.ToArray();

            if (receiver.Receive(3, 0, false,
                    WithOffset(fragmentsB[i], 5), out var currentB))
                assembledB = currentB.span.ToArray();
        }

        CollectionAssert.AreEqual(payloadA, assembledA);
        CollectionAssert.AreEqual(payloadB, assembledB);
    }

    [Test]
    public void FragmentationLayer_SequencedMessageIdWrapTreatsZeroAsNewer()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var nextId = typeof(FragmentationLayer).GetField("_nextMessageId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(nextId);
        nextId.SetValue(sender, uint.MaxValue);

        var oldPayload = new byte[35];
        var newPayload = new byte[35];
        for (int i = 0; i < newPayload.Length; i++)
            newPayload[i] = (byte)(i + 50);

        var oldFragments = new List<byte[]>();
        var newFragments = new List<byte[]>();
        sender.Send(new ByteData(oldPayload, 0, oldPayload.Length), 24, f => Capture(f, oldFragments));
        sender.Send(new ByteData(newPayload, 0, newPayload.Length), 24, f => Capture(f, newFragments));

        Assert.IsFalse(receiver.Receive(1, 1, true,
            new ByteData(oldFragments[0], 0, oldFragments[0].Length), out _));

        ByteData assembled = default;
        for (int i = 0; i < newFragments.Count; i++)
            Assert.AreEqual(i == newFragments.Count - 1,
                receiver.Receive(1, 1, true,
                    new ByteData(newFragments[i], 0, newFragments[i].Length), out assembled));

        for (int i = 1; i < oldFragments.Count; i++)
            Assert.IsFalse(receiver.Receive(1, 1, true,
                new ByteData(oldFragments[i], 0, oldFragments[i].Length), out _));

        AssertByteDataEquals(new ByteData(newPayload, 0, newPayload.Length), assembled);
    }

    [Test]
    public void FragmentationLayer_SequencedWrap_NewSinglePacketInvalidatesOldFragments()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var nextId = typeof(FragmentationLayer).GetField("_nextMessageId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(nextId);
        nextId.SetValue(sender, uint.MaxValue);

        var oldPayload = new byte[35];
        var newPayload = new byte[] { 10, 20, 30, 40 };
        var oldFragments = new List<byte[]>();
        var newPackets = new List<byte[]>();
        sender.SendSequenced(new ByteData(oldPayload, 0, oldPayload.Length), 24,
            packet => Capture(packet, oldFragments));
        sender.SendSequenced(new ByteData(newPayload, 0, newPayload.Length), 24,
            packet => Capture(packet, newPackets));

        Assert.Greater(oldFragments.Count, 1);
        Assert.AreEqual(1, newPackets.Count);
        Assert.IsFalse(receiver.Receive(1, 1, true,
            new ByteData(oldFragments[0], 0, oldFragments[0].Length), out _));
        Assert.IsTrue(receiver.Receive(1, 1, true,
            new ByteData(newPackets[0], 0, newPackets[0].Length), out var assembled));

        for (int i = 1; i < oldFragments.Count; i++)
            Assert.IsFalse(receiver.Receive(1, 1, true,
                new ByteData(oldFragments[i], 0, oldFragments[i].Length), out _));

        AssertByteDataEquals(new ByteData(newPayload, 0, newPayload.Length), assembled);
    }

    [Test]
    public void ValidatedSyncVar_ServerValidation_RemovesLastMatchingSubscriber()
    {
        var syncVar = new ValidatedSyncVar<int>(0);
        int firstCalls = 0;
        int secondCalls = 0;

        ValidatedSyncVar<int>.ServerValidationHandler first = (_, _) =>
        {
            firstCalls++;
            return true;
        };
        ValidatedSyncVar<int>.ServerValidationHandler second = (_, _) =>
        {
            secondCalls++;
            return true;
        };

        syncVar.serverValidation += first;
        syncVar.serverValidation += second;
        syncVar.serverValidation += first;
        syncVar.serverValidation -= first;

        Assert.IsTrue(RunServerValidators(syncVar, 0, 1));
        Assert.AreEqual(1, firstCalls);
        Assert.AreEqual(1, secondCalls);
    }

    [Test]
    public void ValidatedSyncVar_ServerValidation_ShortCircuitsAndPoolResetClears()
    {
        var syncVar = new ValidatedSyncVar<int>(0);
        int calls = 0;

        syncVar.serverValidation += (_, _) =>
        {
            calls++;
            return false;
        };
        syncVar.serverValidation += (_, _) =>
        {
            calls++;
            return true;
        };

        Assert.IsFalse(RunServerValidators(syncVar, 0, 1));
        Assert.AreEqual(1, calls);

        syncVar.OnPoolReset();
        calls = 0;

        Assert.IsTrue(RunServerValidators(syncVar, 0, 1));
        Assert.AreEqual(0, calls);
    }

    [Test]
    public void FragmentationLayer_GlobalPressure_EvictsOldestInsteadOfRejecting()
    {
        using var receiver = new FragmentationLayer();
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = info => drops.Add(info);

        var payload = new byte[64];
        for (int senderId = 0; senderId < 8; senderId++)
        {
            using var sender = new FragmentationLayer();
            for (int message = 0; message < 16; message++)
            {
                var fragments = new List<byte[]>();
                sender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, fragments));
                Assert.Greater(fragments.Count, 1);
                Assert.IsFalse(receiver.Receive(senderId, 0, false,
                    new ByteData(fragments[0], 0, fragments[0].Length), out _));
            }
        }

        Assert.AreEqual(128, receiver.pendingCount);
        Assert.AreEqual(0, drops.Count);

        using var freshSender = new FragmentationLayer();
        var freshFragments = new List<byte[]>();
        freshSender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, freshFragments));

        ByteData assembled = default;
        for (int i = 0; i < freshFragments.Count; i++)
        {
            Assert.AreEqual(i == freshFragments.Count - 1, receiver.Receive(99, 0, false,
                new ByteData(freshFragments[i], 0, freshFragments[i].Length), out assembled));
        }

        AssertByteDataEquals(new ByteData(payload, 0, payload.Length), assembled);
        Assert.AreEqual(1, drops.Count);
        Assert.AreEqual(FragmentDropReason.Evicted, drops[0].reason);
        Assert.AreEqual(127, receiver.pendingCount);
    }

    [Test]
    public void FragmentationLayer_Expiry_ReportsDropWithFirstWord()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = info => drops.Add(info);

        var payload = new byte[64];
        payload[0] = 0x78;
        payload[1] = 0x56;
        payload[2] = 0x34;
        payload[3] = 0x12;
        var fragments = new List<byte[]>();
        sender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, fragments));
        Assert.Greater(fragments.Count, 1);

        Assert.IsFalse(receiver.Receive(7, 0, false, new ByteData(fragments[0], 0, fragments[0].Length), out _));
        receiver.CleanupStale(0);

        Assert.AreEqual(0, receiver.pendingCount);
        Assert.AreEqual(1, drops.Count);
        Assert.AreEqual(FragmentDropReason.Expired, drops[0].reason);
        Assert.AreEqual(7, drops[0].senderId);
        Assert.AreEqual(payload.Length, drops[0].totalLength);
        Assert.IsTrue(drops[0].hasFirstWord);
        Assert.AreEqual(0x12345678u, drops[0].firstWord);
    }

    [Test]
    public void FragmentationLayer_PerSenderBudget_SequencedRejectionReportedOncePerMessage()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = info => drops.Add(info);

        var payload = new byte[64];
        for (int message = 0; message < 16; message++)
        {
            var fragments = new List<byte[]>();
            sender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, fragments));
            Assert.IsFalse(receiver.Receive(3, (byte)message, true,
                new ByteData(fragments[0], 0, fragments[0].Length), out _));
        }

        Assert.AreEqual(16, receiver.pendingCount);
        Assert.AreEqual(0, drops.Count);

        var rejectedFragments = new List<byte[]>();
        sender.Send(new ByteData(payload, 0, payload.Length), 24, fragment => Capture(fragment, rejectedFragments));
        Assert.Greater(rejectedFragments.Count, 1);

        for (int i = 0; i < rejectedFragments.Count; i++)
        {
            Assert.IsFalse(receiver.Receive(3, 99, true,
                new ByteData(rejectedFragments[i], 0, rejectedFragments[i].Length), out _));
        }

        Assert.AreEqual(16, receiver.pendingCount);
        Assert.AreEqual(1, drops.Count);
        Assert.AreEqual(FragmentDropReason.BudgetExceeded, drops[0].reason);
        Assert.IsTrue(drops[0].hasFirstWord);
    }

    [Test]
    public void MyersDeltaList_ChangedAfterUnchanged_PreservesIndependentBaselineCopy()
    {
        var old = DisposableList<int>.Create();
        old.Add(1);
        old.Add(2);
        old.Add(3);

        var value = DisposableList<int>.Create();
        value.Add(1);
        value.Add(2);
        value.Add(3);

        _packer.ResetPositionAndMode(false);
        MyersPackDisposableLists.WriteDisposableDeltaList(_packer, old, value);
        _packer.ResetPositionAndMode(true);
        MyersPackDisposableLists.ReadDisposableDeltaList(_packer, old, ref value);

        Assert.AreNotSame(old.rawList, value.rawList);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, old);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, value);

        var newValue = DisposableList<int>.Create();
        newValue.Add(9);

        _packer.ResetPositionAndMode(false);
        MyersPackDisposableLists.WriteDisposableDeltaList(_packer, old, newValue);
        _packer.ResetPositionAndMode(true);
        MyersPackDisposableLists.ReadDisposableDeltaList(_packer, old, ref value);

        Assert.AreNotSame(old.rawList, value.rawList);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, old);
        Assert.AreEqual(1, value.Count);
        Assert.AreEqual(9, value[0]);

        old.Dispose();
        value.Dispose();
        newValue.Dispose();
    }

    [Test]
    public void DisposableList_DuplicateIsIndependentAndMutationKeepsShallowAliasesValid()
    {
        var original = DisposableList<int>.Create();
        original.Add(1);
        original.Add(2);

        var snapshot = original.Duplicate();
        var alias = original;

        original.Add(3);
        alias.Add(4);

        Assert.IsFalse(alias.isDisposed);
        Assert.AreNotSame(original.rawList, snapshot.rawList);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, original);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, alias);
        Assert.AreEqual(2, snapshot.Count);
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot);

        original.Dispose();
        snapshot.Dispose();
    }

    private static void Capture(ByteData data, List<byte[]> target)
    {
        var copy = new byte[data.length];
        Buffer.BlockCopy(data.data, data.offset, copy, 0, data.length);
        target.Add(copy);
    }

    private static void AssertByteDataEquals(ByteData expected, ByteData actual)
    {
        Assert.AreEqual(expected.length, actual.length);
        for (int i = 0; i < expected.length; i++)
            Assert.AreEqual(expected.data[expected.offset + i], actual.data[actual.offset + i], $"Byte {i}");
    }

    private static ByteData WithOffset(byte[] source, int offset)
    {
        var wrapped = new byte[source.Length + offset + 2];
        Buffer.BlockCopy(source, 0, wrapped, offset, source.Length);
        return new ByteData(wrapped, offset, source.Length);
    }

    private static bool RunServerValidators<T>(ValidatedSyncVar<T> syncVar, T oldValue, T newValue)
    {
        var method = typeof(ValidatedSyncVar<T>).GetMethod("RunServerValidators",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        return (bool)method.Invoke(syncVar, new object[] { oldValue, newValue });
    }
}
