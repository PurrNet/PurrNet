using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using JetBrains.Annotations;
using K4os.Compression.LZ4;
using PurrNet.Modules;
using PurrNet.Transports;
#if PURR_ENDIAN
using System.Runtime.Serialization;
#endif

namespace PurrNet.Packing
{
    [UsedImplicitly]
    public sealed partial class BitPacker : IDisposable, IDuplicate<BitPacker>, IEquatable<BitPacker>
    {
        private byte[] _buffer;
        private bool _isReading;
        public byte[] buffer => _buffer;

        public bool isWrapper { get; private set; }

        private int _positionInBits;
        private int _readStartInBits;
        private int _readEndInBits;
        private const int MAX_BIT_POSITION = int.MaxValue & ~7;

        public int positionInBits
        {
            get => _positionInBits;
        }

        public int positionInBytes => BytesForBits(_positionInBits);

        /// <summary>
        /// Bits left in the logical data, including padding in its final byte.
        /// Buffer capacity and the cursor's absolute byte offset are excluded.
        /// </summary>
        public int remainingBits => Math.Max(0, _readEndInBits - _positionInBits);

        /// <summary>Complete bytes readable from the current, possibly unaligned, cursor.</summary>
        public int remainingBytes => remainingBits >> 3;

        public int length
        {
            get
            {
                if (isWrapper || _isReading)
                    return BytesForBits(Math.Max(_readEndInBits, _positionInBits) - _readStartInBits);
                return positionInBytes - (_readStartInBits >> 3);
            }
        }

        public bool isReading => _isReading;

        public bool isWriting => !_isReading;

        [UsedImplicitly, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AdvanceBit()
        {
            EnsureBitsExist(1);
            ++_positionInBits;
        }

        /// <summary>
        /// Pickles the current buffer into the provided BitPacker.
        /// </summary>
        public void PickleInto(BitPacker packer, LZ4Level level = LZ4Level.L00_FAST)
        {
            LZ4Pickler.Pickle(ToByteData().span, new BitPackerWrapper(packer), level);
        }

        /// <summary>
        /// Unpickles the provided ByteData into the current BitPacker.
        /// </summary>
        public void UnpickleFrom(ByteData data)
        {
            LZ4Pickler.Unpickle(data.span, new BitPackerWrapper(this));
        }

        /// <summary>
        /// Unpickles the provided BitPacker into the current BitPacker.
        /// </summary>
        public void UnpickleFrom(BitPacker data)
        {
            LZ4Pickler.Unpickle(data.ToByteData().span, new BitPackerWrapper(this));
        }

        /// <summary>
        /// Pickles the current buffer into a new BitPacker.
        /// Don't forget to dispose of the returned BitPacker.
        /// </summary>
        public BitPacker Pickle(LZ4Level level = LZ4Level.L00_FAST)
        {
            var packer = BitPackerPool.Get();
            packer.EnsureBitsExist(_positionInBits);
            PickleInto(packer, level);
            return packer;
        }

        public void AdvanceBytes(int count)
        {
            AdvanceBits(BitsForBytes(count));
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AdvanceBits(int bitCount)
        {
            EnsureBitsExist(bitCount);
            var old = _positionInBits;
            _positionInBits += bitCount;
            return old;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AdvanceOneBitAndClear()
        {
            var old = _positionInBits;
            WriteBit(false);
            return old;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AdvanceOneBitAndSet()
        {
            var old = _positionInBits;
            WriteBit(true);
            return old;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureBitsExist(positionInBytes << 3, BitsForBytes(sizeHint));
            return new Memory<byte>(_buffer, positionInBytes, sizeHint);
        }

        public ArraySegment<byte> AsSegment()
        {
            return new ArraySegment<byte>(_buffer, _readStartInBits >> 3, length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureBitsExist(positionInBytes << 3, BitsForBytes(sizeHint));
            return new Span<byte>(_buffer, positionInBytes, sizeHint);
        }

        public BitPacker(int initialSize = 1024)
        {
            _buffer = new byte[initialSize];
        }

        public void MakeWrapper(ByteData data)
        {
            var source = data.data ?? Array.Empty<byte>();
            if (data.offset < 0 || data.offset > source.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Invalid byte offset.");
            if (data.length < 0 || data.length > source.Length - data.offset ||
                (long)data.offset + data.length > (MAX_BIT_POSITION >> 3))
                throw new ArgumentOutOfRangeException(nameof(data), "Invalid byte length.");

            _buffer = source;
            _readStartInBits = data.offset << 3;
            _readEndInBits = (data.offset + data.length) << 3;
            _positionInBits = _readStartInBits;
            _isReading = true;
            isWrapper = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            BitPackerPool.Free(this);
        }

        public ByteData ToByteData()
        {
            return new ByteData(_buffer, _readStartInBits >> 3, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetPosition()
        {
            _positionInBits = _readStartInBits;
            if (!_isReading)
                _readEndInBits = _readStartInBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetMode(bool readMode)
        {
            RecordWrittenPosition();
            _isReading = readMode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBitPosition(int bitPosition)
        {
            EnsureBitsExist(bitPosition, 0);
            RecordWrittenPosition();
            _positionInBits = bitPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBytes(int skip)
        {
            SetPositionAfterSkip((long)skip * 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBytes(uint skip)
        {
            SetPositionAfterSkip((long)skip * 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetPositionAndMode(bool readMode)
        {
            ResetMode(readMode);
            ResetPosition();
        }

        internal void ResetForPool()
        {
            _positionInBits = 0;
            _readStartInBits = 0;
            _readEndInBits = 0;
            _isReading = false;
            if (isWrapper)
                _buffer = Array.Empty<byte>();
        }

        public void EnsurePadding()
        {
            int requiredBytes = positionInBytes + 8;
            if (requiredBytes > _buffer.Length)
                Array.Resize(ref _buffer, requiredBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureBitExists()
        {
            if ((_positionInBits & 7) == 0)
                EnsureBitsExist(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureBitsExist(int bits)
        {
            EnsureBitsExist(_positionInBits, bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureBitsExist(int positionInBits, int bits)
        {
            if (_isReading)
            {
                EnsureReadableBits(positionInBits, bits);
                return;
            }

            if (bits < 0)
                throw new ArgumentOutOfRangeException(nameof(bits));
            if (positionInBits < _readStartInBits || (long)positionInBits + bits > MAX_BIT_POSITION)
                throw new ArgumentOutOfRangeException(nameof(positionInBits));

            int requiredBytes = BytesForBits(positionInBits + bits) + 8;
            if (requiredBytes > _buffer.Length)
            {
                int newSize = (int)Math.Max(Math.Min((long)_buffer.Length * 2, int.MaxValue), requiredBytes);
                Array.Resize(ref _buffer, newSize);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureReadableBits(int positionInBits, int bits)
        {
            if (bits < 0)
                throw new ArgumentOutOfRangeException(nameof(bits));
            if (positionInBits < _readStartInBits || positionInBits > _readEndInBits ||
                bits > _readEndInBits - positionInBits)
                throw new IndexOutOfRangeException("Not enough bits in the logical data.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordWrittenPosition()
        {
            RecordWrittenEnd(_positionInBits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordWrittenEnd(int bitPosition)
        {
            if (!_isReading)
                _readEndInBits = Math.Max(_readEndInBits, BytesForBits(bitPosition) << 3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BytesForBits(int bits) => (bits >> 3) + ((bits & 7) == 0 ? 0 : 1);

        private static int BitsForBytes(int bytes)
        {
            if ((uint)bytes > (MAX_BIT_POSITION >> 3))
                throw new ArgumentOutOfRangeException(nameof(bytes));
            return bytes << 3;
        }

        private void SetPositionAfterSkip(long bits)
        {
            long position = _positionInBits + bits;
            if (position < _readStartInBits || position > MAX_BIT_POSITION)
                throw new IndexOutOfRangeException("Bit position is outside the logical data.");
            SetBitPosition((int)position);
        }

        [UsedByIL]
        public bool HandleNullScenarios<T>(T oldValue, T newValue, ref bool areEqual)
        {
            if (oldValue == null)
            {
                if (newValue == null)
                {
                    areEqual = true;
                    return false;
                }

                areEqual = false;
                Packer<T>.Write(this, newValue);
                return false;
            }

            if (newValue == null)
            {
                areEqual = false;
                Packer<T>.Write(this, default);
                return false;
            }

            return true;
        }

        [UsedByIL]
        public bool WriteIsNull<T>(T value) where T : class
        {
            if (value == null)
            {
                WriteBit(true);
                return false;
            }

            WriteBit(false);
            return true;
        }

        [UsedByIL]
        public bool ReadIsNull<T>(ref T value)
        {
            if (ReadBit())
            {
                value = default;
                return false;
            }

            if (value != null)
                return true;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                value = FactoryCache<T>.Create();

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBitDataWithoutConsumingIt(BitData data)
        {
            CopyBitsWithoutConsuming(data.packer, (int)data.bitOrigin.value, (int)data.bitLength.value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBitsWithoutConsumingIt(BitPacker packer, int bits)
        {
            CopyBitsWithoutConsuming(packer, packer._readStartInBits, bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteBitsWithoutConsumingItUnchecked(BitPacker packer, int bits)
        {
            if (bits == 0)
                return;

            EnsureBitsExist(bits);
            packer.EnsureBitsExist(packer._readStartInBits, bits);
            CopyBitsFromValidatedSource(packer, packer._readStartInBits, bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyBitsWithoutConsuming(BitPacker other, int bitOrigin, int bits)
        {
            if (bits == 0)
                return;

            EnsureBitsExist(bits);
            other.EnsureBitsExist(bitOrigin, bits);
            CopyBitsFromValidatedSource(other, bitOrigin, bits);
        }

        private unsafe void CopyBitsFromValidatedSource(BitPacker other, int bitOrigin, int bits)
        {
            if (((_positionInBits | bitOrigin) & 7) == 0)
            {
                int fullBytes = bits >> 3;
                if (fullBytes > 0)
                {
                    other._buffer.AsSpan(bitOrigin >> 3, fullBytes)
                        .CopyTo(_buffer.AsSpan(_positionInBits >> 3, fullBytes));
                    _positionInBits += fullBytes << 3;
                }

                byte remainingBits = (byte)(bits & 7);
                if (remainingBits != 0)
                    WriteBitsCore(other._buffer[(bitOrigin >> 3) + fullBytes], remainingBits);
                return;
            }

            bool hasIndependentByteAlignedSource = (bitOrigin & 7) == 0 && !ReferenceEquals(_buffer, other._buffer);
            if (hasIndependentByteAlignedSource)
            {
                if (bits >= 80 && !_isReading)
                {
                    CopyByteAlignedSourceToUnalignedDestination(other, bitOrigin >> 3, bits);
                    return;
                }

                int sourcePosition = other._positionInBits;
                other._positionInBits = bitOrigin;
                int chunks = bits >> 6;
                byte excess = (byte)(bits & 63);
                for (int i = 0; i < chunks; i++)
                    WriteBitsCore(other.ReadBitsCore(64), 64);
                if (excess != 0)
                    WriteBitsCore(other.ReadBitsCore(excess), excess);
                other._positionInBits = sourcePosition;
                return;
            }

            int beforeBitPosition = other._positionInBits;
            other._positionInBits = bitOrigin;

            try
            {
                int chunks = bits >> 6;
                byte excess = (byte)(bits & 63);

                for (int i = 0; i < chunks; i++)
                    WriteBitsCore(other.ReadBitsCore(64), 64);
                if (excess != 0)
                    WriteBitsCore(other.ReadBitsCore(excess), excess);
            }
            finally
            {
                other._positionInBits = beforeBitPosition;
            }
        }

        private unsafe void CopyByteAlignedSourceToUnalignedDestination(BitPacker other, int sourceByteOrigin,
            int bits)
        {
            int chunks = bits >> 6;
            byte excess = (byte)(bits & 63);
            int destinationBitOffset = _positionInBits & 7;
            ulong preservedLowBits = (1UL << destinationBitOffset) - 1;
            byte overflowMask = (byte)((1 << destinationBitOffset) - 1);

            fixed (byte* source = &other._buffer[sourceByteOrigin])
            fixed (byte* destination = &_buffer[_positionInBits >> 3])
            {
                for (int i = 0; i < chunks; i++)
                {
                    byte* destinationChunk = destination + (i << 3);
                    ulong value = *(ulong*)(source + (i << 3));
                    ulong existing = *(ulong*)destinationChunk;
#if PURR_ENDIAN
                    if (!BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                        existing = BinaryPrimitives.ReverseEndianness(existing);
                    }
#endif
                    ulong result = (existing & preservedLowBits) | (value << destinationBitOffset);
#if PURR_ENDIAN
                    if (!BitConverter.IsLittleEndian)
                        result = BinaryPrimitives.ReverseEndianness(result);
#endif
                    *(ulong*)destinationChunk = result;

                    byte highData = (byte)(value >> (64 - destinationBitOffset));
                    byte* overflowByte = destinationChunk + 8;
                    *overflowByte = (byte)((*overflowByte & ~overflowMask) | (highData & overflowMask));
                }

                if (excess != 0)
                {
                    ulong value = 0;
                    byte* remainingSource = source + (chunks << 3);
                    int bytesToRead = (excess + 7) >> 3;
                    for (int i = 0; i < bytesToRead; i++)
                        value |= (ulong)remainingSource[i] << (i << 3);

                    byte* destinationChunk = destination + (chunks << 3);
                    ulong dataMask = (1UL << excess) - 1;
                    ulong writeMask = dataMask << destinationBitOffset;
                    ulong existing = *(ulong*)destinationChunk;
#if PURR_ENDIAN
                    if (!BitConverter.IsLittleEndian)
                        existing = BinaryPrimitives.ReverseEndianness(existing);
#endif
                    ulong result = (existing & ~writeMask) | ((value & dataMask) << destinationBitOffset);
#if PURR_ENDIAN
                    if (!BitConverter.IsLittleEndian)
                        result = BinaryPrimitives.ReverseEndianness(result);
#endif
                    *(ulong*)destinationChunk = result;

                    int overflow = excess + destinationBitOffset - 64;
                    if (overflow > 0)
                    {
                        byte highMask = (byte)((1 << overflow) - 1);
                        byte highData = (byte)(value >> (64 - destinationBitOffset));
                        byte* overflowByte = destinationChunk + 8;
                        *overflowByte = (byte)((*overflowByte & ~highMask) | (highData & highMask));
                    }
                }
            }

            _positionInBits += bits;
        }

        public void WriteBits(BitPacker packer, int bits)
        {
            EnsureBitsExist(bits);
            packer.EnsureBitsExist(bits);

            int chunks = bits / 64;
            byte excess = (byte)(bits % 64);

            for (int i = 0; i < chunks; i++)
                WriteBitsCore(packer.ReadBitsCore(64), 64);
            if (excess != 0)
                WriteBitsCore(packer.ReadBitsCore(excess), excess);
        }

        public void WriteBits(ulong data, byte bits)
        {
            ValidateBitCount(bits);
            EnsureBitsExist(bits);
            if (bits != 0)
                WriteBitsCore(data, bits);
        }

        public bool WriteBit(bool data)
        {
            EnsureBitExists();
            var byteIdx = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;

            var currentByte = _buffer[byteIdx];

            if (data)
                 currentByte |= (byte)(1 << bitOffset);
            else currentByte &= (byte)~(1 << bitOffset);

            _buffer[byteIdx] = currentByte;
            _positionInBits++;
            return data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool ReadBit()
        {
            EnsureReadableBits(_positionInBits, 1);
            fixed (byte* b = &_buffer[_positionInBits >> 3])
            {
                bool result = (*b & (1 << (_positionInBits & 7))) != 0;
                _positionInBits++;
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBitsWithoutChecks(ulong data, byte bits)
        {
            WriteBitsCore(data, bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void WriteBitsCore(ulong data, byte bits)
        {
            int bytePos = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;

            if (_buffer.Length - bytePos < 9)
            {
                WriteBitsAtWithoutChecks(_positionInBits, data, bits);
                _positionInBits += bits;
                return;
            }

            fixed (byte* b = &_buffer[bytePos])
            {
                ulong dataMask = bits == 64 ? ~0UL : (1UL << bits) - 1;
                ulong maskedData = data & dataMask;
                ulong shifted = maskedData << bitOffset;
                ulong writeMask = dataMask << bitOffset;
                ulong existing = *(ulong*)b;
#if PURR_ENDIAN
                if (!BitConverter.IsLittleEndian)
                    existing = BinaryPrimitives.ReverseEndianness(existing);
#endif

                ulong result = (existing & ~writeMask) | shifted;

#if PURR_ENDIAN
                if (!BitConverter.IsLittleEndian)
                    result = BinaryPrimitives.ReverseEndianness(result);
#endif
                *(ulong*)b = result;

                int overflow = bits + bitOffset - 64;
                int safeOverflow = overflow & ((overflow >> 31) ^ -1);

                byte* b8 = b + 8;
                byte highData = (byte)(maskedData >> ((64 - bitOffset) & 63));
                byte highMask = (byte)((1 << safeOverflow) - 1);
                *b8 = (byte)((*b8 & ~highMask) | (highData & highMask));
            }

            _positionInBits += bits;
        }

        public ulong ReadBits(byte bits)
        {
            EnsureReadableBits(_positionInBits, bits);
            ValidateBitCount(bits);
            return bits == 0 ? 0 : ReadBitsCore(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadBitsWithoutChecks(byte bits)
        {
            return ReadBits(bits);
        }

        private static void ValidateBitCount(byte bits)
        {
            if (bits > 64)
                throw new ArgumentOutOfRangeException(nameof(bits), "Cannot read or write more than 64 bits at a time.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ulong ReadBitsCore(byte bits)
        {
            int bytePos = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;
            int available = _buffer.Length - bytePos;

            fixed (byte* b = &_buffer[bytePos])
            {
                ulong raw;

                if (available >= 9)
                {
                    // Fast path: enough room for ulong read + overflow byte
                    raw = *(ulong*)b;
#if PURR_ENDIAN
                    if (!BitConverter.IsLittleEndian)
                        raw = BinaryPrimitives.ReverseEndianness(raw);
#endif
                    raw >>= bitOffset;

                    int overflow = bits + bitOffset - 64;
                    if (overflow > 0)
                    {
                        ulong highByte = (ulong)b[8] << (64 - bitOffset);
                        raw |= highByte;
                    }
                }
                else
                {
                    // Safe path: near end of buffer, read byte-by-byte
                    // Assembles in little-endian order, no endian swap needed
                    raw = 0;
                    int toCopy = available < 8 ? available : 8;
                    for (int i = 0; i < toCopy; i++)
                        raw |= (ulong)b[i] << (i * 8);

                    raw >>= bitOffset;
                }

                _positionInBits += bits;

                ulong mask = bits == 64 ? ~0UL : (1UL << bits) - 1;
                return raw & mask;
            }
        }

        public void ReadBytes(Span<byte> destination)
        {
            int count = destination.Length;
            int bits = BitsForBytes(count);
            EnsureReadableBits(_positionInBits, bits);

            if ((_positionInBits & 7) == 0)
            {
                _buffer.AsSpan(_positionInBits >> 3, count).CopyTo(destination);
                _positionInBits += bits;
                return;
            }

            int fullChunks = count >> 3;
            int excess = count & 7;
            int index = 0;

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                ulong longValue = ReadBitsCore(64);

                // Write back as little-endian
                BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(index, 8), longValue);
                index += 8;
            }

            // Process remaining excess bytes
            for (int i = 0; i < excess; i++)
            {
                destination[index++] = (byte)ReadBitsCore(8);
            }
        }

        public void WriteBytes(ByteData byteData)
        {
            WriteBytes(byteData.span);
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            int count = bytes.Length;
            int bits = BitsForBytes(count);
            EnsureBitsExist(bits);

            if ((_positionInBits & 7) == 0)
            {
                bytes.CopyTo(_buffer.AsSpan(_positionInBits >> 3, count));
                _positionInBits += bits;
                return;
            }

            int fullChunks = count >> 3;
            int excess = count & 7;
            int index = 0;

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                ulong longValue = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(index, 8));
                WriteBitsCore(longValue, 64);
                index += 8;
            }

            // Process remaining excess bytes
            for (int i = 0; i < excess; i++)
                WriteBitsCore(bytes[index++], 8);
        }

        public void SkipBits(int skip)
        {
            SetPositionAfterSkip(skip);
        }

        public void WriteString(Encoding encoding, string value)
        {
            // Null flag
            WriteBits(value != null ? 1UL : 0UL, 1);
            if (value == null)
                return;

            // Encode string into a temporary buffer
            int byteCount = encoding.GetByteCount(value);
            EnsureBitsExist(checked(32 + BitsForBytes(byteCount)));

            // Write length (31 bits)
            WriteBits((ulong)byteCount, 31);

            byte[] rented = null;
            Span<byte> temp = byteCount <= 256
                ? stackalloc byte[byteCount]
                : (rented = ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);

            try
            {
                encoding.GetBytes(value, temp);
                WriteBytes(temp);
            }
            finally
            {
                if (rented != null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public string ReadString(Encoding encoding)
        {
            // Null flag
            if (ReadBits(1) == 0)
                return null;

            // Length
            int len = (int)ReadBits(31);
            DeserializationLimits.ValidateByteLength(this, len);

            byte[] rented = null;
            Span<byte> temp = len <= 256
                ? stackalloc byte[len]
                : (rented = ArrayPool<byte>.Shared.Rent(len)).AsSpan(0, len);

            try
            {
                ReadBytes(temp);
                return encoding.GetString(temp);
            }
            finally
            {
                if (rented != null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public char ReadChar()
        {
            return (char)ReadBits(8);
        }

        [UsedByIL]
        public void ResetFlagAtAndMovePosition(int positionInBits)
        {
            EnsureBitsExist(positionInBits, 1);
            var byteIdx = positionInBits >> 3;
            int bitOffset = positionInBits & 7;

            ref var currentByte = ref _buffer[byteIdx];
            currentByte &= (byte)~(1 << bitOffset);

            RecordWrittenPosition();
            _positionInBits = positionInBits + 1;
        }

        [UsedByIL]
        public void WriteAt(int positionInBits, bool data)
        {
            EnsureBitsExist(positionInBits, 1);
            var byteIdx = positionInBits >> 3;
            int bitOffset = positionInBits & 7;

            ref var currentByte = ref _buffer[byteIdx];

            if (data)
                currentByte |= (byte)(1 << bitOffset);
            else currentByte &= (byte)~(1 << bitOffset);
            RecordWrittenEnd(positionInBits + 1);
        }

        public void WriteBitsAt(int positionInBits, ulong data, byte bits)
        {
            ValidateBitCount(bits);
            EnsureBitsExist(positionInBits, bits);
            WriteBitsAtWithoutChecks(positionInBits, data, bits);
            RecordWrittenEnd(positionInBits + bits);
        }

        void WriteBitsAtWithoutChecks(int positionInBits, ulong data, byte bits)
        {
            if (bits > 64)
                throw new ArgumentOutOfRangeException(nameof(bits), "Cannot write more than 64 bits at a time.");

            int bitsLeft = bits;

            while (bitsLeft > 0)
            {
                int bytePos = positionInBits >> 3;
                int bitOffset = positionInBits & 7;
                int bitsToWrite = Math.Min(bitsLeft, 8 - bitOffset);

                byte mask = (byte)((1 << bitsToWrite) - 1);
                byte value = (byte)((data >> (bits - bitsLeft)) & mask);

                _buffer[bytePos] &= (byte)~(mask << bitOffset); // Clear the bits to be written
                _buffer[bytePos] |= (byte)(value << bitOffset); // Set the bits

                bitsLeft -= bitsToWrite;
                positionInBits += bitsToWrite;
            }
        }

        public BitPacker Duplicate()
        {
            var newPacker = BitPackerPool.Get();
            int len = length;
            int bits = BitsForBytes(len);
            newPacker.EnsureBitsExist(bits);
            Array.Copy(_buffer, _readStartInBits >> 3, newPacker.buffer, 0, len);
            newPacker._readEndInBits = bits;
            // newPacker._positionInBits = _positionInBits; // this is intentionally not copied
            return newPacker;
        }

        public bool Equals(BitPacker other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            if (_positionInBits != other._positionInBits) return false;

            int fullBytes = _positionInBits >> 3;
            int tailBits = _positionInBits & 7;

            // Compare full bytes
            if (!_buffer.AsSpan(0, fullBytes).SequenceEqual(other._buffer.AsSpan(0, fullBytes)))
                return false;

            // Compare tail bits
            if (tailBits != 0)
            {
                byte mask = (byte)((1 << tailBits) - 1);
                if ((_buffer[fullBytes] & mask) != (other._buffer[fullBytes] & mask))
                    return false;
            }

            return true;
        }

        public uint GetDeterministicHash32()
        {
            var hash64 = GetDeterministicHash64();
            return (uint)(hash64 ^ (hash64 >> 32));
        }

        public unsafe ulong GetDeterministicHash64()
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            int bits = _positionInBits;
            int fullBytes = bits >> 3;

            ulong hash = offset;

            fixed (byte* ptr = _buffer)
            {
                int i = 0;
                // Process 8 bytes at a time
                for (; i + 8 <= fullBytes; i += 8)
                {
                    ulong chunk = *(ulong*)(ptr + i);
                    hash ^= chunk;
                    hash *= prime;
                }
                // Remaining bytes
                for (; i < fullBytes; i++)
                {
                    hash ^= ptr[i];
                    hash *= prime;
                }
            }

            int tailBits = bits & 7;
            if (tailBits != 0)
            {
                byte mask = (byte)((1 << tailBits) - 1);
                hash ^= (byte)(_buffer[fullBytes] & mask);
                hash *= prime;
                hash ^= (byte)tailBits;
                hash *= prime;
            }

            hash ^= (uint)bits;
            hash *= prime;

            return hash;
        }
    }
}
