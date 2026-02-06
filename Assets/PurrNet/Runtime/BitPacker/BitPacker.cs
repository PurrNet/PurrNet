using System;
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

        public int positionInBits
        {
            get => _positionInBits;
        }

        public int positionInBytes => (_positionInBits + 7) >> 3;

        public int length
        {
            get
            {
                if (isWrapper)
                    return _buffer.Length;
                return positionInBytes;
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
            EnsureBitsExist(count * 8);
            _positionInBits += count * 8;
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
            EnsureBitsExist(sizeHint * 8);
            return new Memory<byte>(_buffer, positionInBytes, sizeHint);
        }

        public ArraySegment<byte> AsSegment()
        {
            return new ArraySegment<byte>(_buffer, 0, length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureBitsExist(sizeHint * 8);
            return new Span<byte>(_buffer, positionInBytes, sizeHint);
        }

        public BitPacker(int initialSize = 1024)
        {
            _buffer = new byte[initialSize];
        }

        public void MakeWrapper(ByteData data)
        {
            _buffer = data.data;
            _positionInBits = data.offset * 8;
            isWrapper = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            BitPackerPool.Free(this);
        }

        public ByteData ToByteData()
        {
            return new ByteData(_buffer, 0, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetPosition()
        {
            _positionInBits = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetMode(bool readMode)
        {
            _isReading = readMode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBitPosition(int bitPosition)
        {
            _positionInBits = bitPosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBytes(int skip)
        {
            _positionInBits += skip * 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBytes(uint skip)
        {
            _positionInBits += (int)skip * 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetPositionAndMode(bool readMode)
        {
            _positionInBits = 0;
            _isReading = readMode;
        }

        public void EnsurePadding()
        {
            int requiredBytes = positionInBytes + 8;
            if (requiredBytes > _buffer.Length)
                Array.Resize(ref _buffer, requiredBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureBitsExist(int bits)
        {
            int targetPos = _positionInBits + bits;
            int requiredBytes = _isReading ? (targetPos + 7) >> 3 : ((targetPos + 7) >> 3) + 8;

            if (requiredBytes > _buffer.Length)
            {
                if (_isReading)
                    throw new IndexOutOfRangeException($"Not enough bits in the buffer. | {targetPos} > {_buffer.Length << 3}");
                int newSize = Math.Max(_buffer.Length * 2, ((targetPos + 7) >> 3) + 8);
                Array.Resize(ref _buffer, newSize);
            }
        }

        private void EnsureBitsExist(int positionInBits, int bits)
        {
            int targetPos = positionInBits + bits;
            var bufferBitSize = _buffer.Length * 8;

            if (targetPos > bufferBitSize)
            {
                if (_isReading)
                    throw new IndexOutOfRangeException("Not enough bits in the buffer. | " + targetPos + " > " +
                                                       bufferBitSize);
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }
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

        public void WriteBitDataWithoutConsumingIt(BitData data)
        {
            int toRead = (int)data.bitLength.value;
            if (toRead == 0) return;

            var other = data.packer;
            var beforeBitPosition = other._positionInBits;

            other._positionInBits = data.bitOrigin;
            EnsureBitsExist(toRead);

            int chunks = toRead / 64;
            byte excess = (byte)(toRead % 64);

            for (int i = 0; i < chunks; i++)
                WriteBitsWithoutChecks(other.ReadBits(64), 64);

            if (excess != 0)
                WriteBitsWithoutChecks(other.ReadBits(excess), excess);

            other.SetBitPosition(beforeBitPosition);
        }

        public void WriteBitsWithoutConsumingIt(BitPacker packer, int bits)
        {
            EnsureBitsExist(bits);

            var beforeBitPosition = packer._positionInBits;
            packer.SetBitPosition(0);

            int chunks = bits / 64;
            byte excess = (byte)(bits % 64);

            for (int i = 0; i < chunks; i++)
                WriteBitsWithoutChecks(packer.ReadBits(64), 64);
            if (excess != 0)
                WriteBitsWithoutChecks(packer.ReadBits(excess), excess);

            packer.SetBitPosition(beforeBitPosition);
        }

        public void WriteBits(BitPacker packer, int bits)
        {
            EnsureBitsExist(bits);

            int chunks = bits / 64;
            byte excess = (byte)(bits % 64);

            for (int i = 0; i < chunks; i++)
                WriteBitsWithoutChecks(packer.ReadBits(64), 64);
            if (excess != 0)
                WriteBitsWithoutChecks(packer.ReadBits(excess), excess);
        }

        public void WriteBits(ulong data, byte bits)
        {
            EnsureBitsExist(bits);
            WriteBitsWithoutChecks(data, bits);
        }

        public bool WriteBit(bool data)
        {
            EnsureBitsExist(1);
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
            fixed (byte* b = &_buffer[_positionInBits >> 3])
            {
                bool result = (*b & (1 << (_positionInBits & 7))) != 0;
                _positionInBits++;
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteBitsWithoutChecks(ulong data, byte bits)
        {
            int bytePos = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;

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
            EnsureBitsExist(bits);
            return ReadBitsWithoutChecks(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ulong ReadBitsWithoutChecks(byte bits)
        {
            int bytePos = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;

            fixed (byte* b = &_buffer[bytePos])
            {
                ulong raw = *(ulong*)b;
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

                _positionInBits += bits;

                ulong mask = bits == 64 ? ~0UL : (1UL << bits) - 1;
                return raw & mask;
            }
        }

        public void ReadBytes(Span<byte> destination)
        {
            int count = destination.Length;
            EnsureBitsExist(count << 3);

            if ((_positionInBits & 7) == 0)
            {
                _buffer.AsSpan(_positionInBits >> 3, count).CopyTo(destination);
                _positionInBits += count << 3;
                return;
            }

            int fullChunks = count >> 3;
            int excess = count & 7;
            int index = 0;

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                ulong longValue = ReadBitsWithoutChecks(64);

                // Write back as little-endian
                BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(index, 8), longValue);
                index += 8;
            }

            // Process remaining excess bytes
            for (int i = 0; i < excess; i++)
            {
                destination[index++] = (byte)ReadBitsWithoutChecks(8);
            }
        }

        public void WriteBytes(ByteData byteData)
        {
            WriteBytes(byteData.span);
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            int count = bytes.Length;
            EnsureBitsExist(count << 3);

            if ((_positionInBits & 7) == 0)
            {
                bytes.CopyTo(_buffer.AsSpan(_positionInBits >> 3, count));
                _positionInBits += count << 3;
                return;
            }

            int fullChunks = count >> 3;
            int excess = count & 7;
            int index = 0;

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                ulong longValue = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(index, 8));
                WriteBitsWithoutChecks(longValue, 64);
                index += 8;
            }

            // Process remaining excess bytes
            for (int i = 0; i < excess; i++)
                WriteBitsWithoutChecks(bytes[index++], 8);
        }

        public void SkipBits(int skip)
        {
            _positionInBits += skip;
        }

        public void WriteString(Encoding encoding, string value)
        {
            // Null flag
            WriteBits(value != null ? 1UL : 0UL, 1);
            if (value == null)
                return;

            // Encode string into a temporary buffer
            int byteCount = encoding.GetByteCount(value);
            EnsureBitsExist(1 + 31 + byteCount * 8);

            // Write length (31 bits)
            WriteBits((ulong)byteCount, 31);

            // Encode directly into buffer
            var temp = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
            encoding.GetBytes(value, temp);
            WriteBytes(temp);
        }

        public string ReadString(Encoding encoding)
        {
            // Null flag
            if (ReadBits(1) == 0)
                return null;

            // Length
            int len = (int)ReadBits(31);

            // Read bytes
            var temp = len <= 256 ? stackalloc byte[len] : new byte[len];
            ReadBytes(temp);
            return encoding.GetString(temp);
        }

        public char ReadChar()
        {
            return (char)ReadBits(8);
        }

        [UsedByIL]
        public void ResetFlagAtAndMovePosition(int positionInBits)
        {
            var byteIdx = positionInBits >> 3;
            int bitOffset = positionInBits & 7;

            ref var currentByte = ref _buffer[byteIdx];
            currentByte &= (byte)~(1 << bitOffset);

            _positionInBits = positionInBits + 1;
        }

        [UsedByIL]
        public void WriteAt(int positionInBits, bool data)
        {
            var byteIdx = positionInBits >> 3;
            int bitOffset = positionInBits & 7;

            ref var currentByte = ref _buffer[byteIdx];

            if (data)
                currentByte |= (byte)(1 << bitOffset);
            else currentByte &= (byte)~(1 << bitOffset);
        }

        public void WriteBitsAt(int positionInBits, ulong data, byte bits)
        {
            EnsureBitsExist(positionInBits, bits);
            WriteBitsAtWithoutChecks(positionInBits, data, bits);
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
            newPacker.EnsureBitsExist(len * 8);
            Array.Copy(_buffer, newPacker.buffer, len);
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
