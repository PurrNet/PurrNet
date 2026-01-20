using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using JetBrains.Annotations;
using K4os.Compression.LZ4;
using PurrNet.Modules;
using PurrNet.Transports;

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

        public int positionInBytes
        {
            get
            {
                int pos = _positionInBits / 8;
                int len = pos + (_positionInBits % 8 == 0 ? 0 : 1);
                return len;
            }
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureBitsExist(int bits)
        {
            int targetPos = _positionInBits + bits;
            if (targetPos > _buffer.Length << 3)
            {
                if (_isReading)
                    throw new IndexOutOfRangeException($"Not enough bits in the buffer. | {targetPos} > {_buffer.Length << 3}");
                int newSize = Math.Max(_buffer.Length * 2, (targetPos + 7) / 8);
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
            {
                if (typeof(T).GetConstructor(Type.EmptyTypes) != null)
                     value = Activator.CreateInstance<T>();
                else value = (T)FormatterServices.GetUninitializedObject(typeof(T));
            }

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

        [UsedImplicitly]
        public bool ReadBit()
        {
            var byteIdx = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;
            var currentByte = _buffer[byteIdx];
            var result = (currentByte & (1 << bitOffset)) != 0;
            _positionInBits++;
            return result;
        }

        public unsafe void WriteBitsWithoutChecks(ulong data, byte bits)
        {
            if (bits > 64)
                throw new ArgumentOutOfRangeException(nameof(bits));

            int bytePos = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;

            fixed (byte* b = &_buffer[bytePos])
            {
                if (bitOffset == 0)
                {
                    // Fast path: byte-aligned writes
                    switch (bits)
                    {
                        case 8:
                            *b = (byte)data;
                            break;
                        case 16:
#if PURR_ENDIAN
                            if (!BitConverter.IsLittleEndian)
                                data = BinaryPrimitives.ReverseEndianness((ushort)data);
#endif
                            *(ushort*)b = (ushort)data;
                            break;
                        case 32:
#if PURR_ENDIAN
                            if (!BitConverter.IsLittleEndian)
                                data = BinaryPrimitives.ReverseEndianness((uint)data);
#endif
                            *(uint*)b = (uint)data;
                            break;
                        case 64:
#if PURR_ENDIAN
                            if (!BitConverter.IsLittleEndian)
                                data = BinaryPrimitives.ReverseEndianness(data);
#endif
                            *(ulong*)b = data;
                            break;
                        default:
                            if (bits <= 8)
                            {
                                byte mask = (byte)((1 << bits) - 1);
                                *b = (byte)((*b & ~mask) | ((byte)data & mask));
                            }
                            else
                            {
                                // Write full bytes + remainder (always little-endian order)
                                int fullBytes = bits >> 3;
                                int remainderBits = bits & 7;

                                for (int i = 0; i < fullBytes; i++)
                                    b[i] = (byte)(data >> (i * 8));

                                if (remainderBits > 0)
                                {
                                    byte mask = (byte)((1 << remainderBits) - 1);
                                    byte value = (byte)(data >> (fullBytes * 8));
                                    b[fullBytes] = (byte)((b[fullBytes] & ~mask) | (value & mask));
                                }
                            }
                            break;
                    }
                }
                else
                {
                    // Unaligned write - shift data and write as 64-bit + remainder
                    ulong shifted = data << bitOffset;
                    int totalBits = bits + bitOffset;
                    int bytesToWrite = (totalBits + 7) >> 3;

                    ulong existing = 0;
                    if (bytesToWrite <= 8)
                        existing = *(ulong*)b & ((1UL << bitOffset) - 1);

                    ulong mask = ((1UL << bits) - 1) << bitOffset;
                    ulong combined = (existing) | (shifted & mask);

#if PURR_ENDIAN
                    if (!BitConverter.IsLittleEndian)
                        combined = BinaryPrimitives.ReverseEndianness(combined);
#endif

                    if (totalBits <= 64)
                    {
                        // Preserve bits beyond our write
                        int preserveBits = 64 - totalBits;
                        if (preserveBits > 0)
                        {
                            ulong preserveMask = ~((1UL << totalBits) - 1);
                            combined |= *(ulong*)b & preserveMask;
                        }
                        *(ulong*)b = combined;
                    }
                    else
                    {
                        // Fallback to original method for edge cases
                        goto SlowPath;
                    }
                }
            }

            _positionInBits += bits;
            return;

        SlowPath:
            // Original implementation as fallback
            fixed (byte* b = &_buffer[bytePos])
            {
                byte* ptr = b;
                int bitsLeft = bits;

                while (bitsLeft > 0)
                {
                    int bitsToWrite = Math.Min(bitsLeft, 8 - bitOffset);
                    byte mask = (byte)((1 << bitsToWrite) - 1);
                    byte value = (byte)((data >> (bits - bitsLeft)) & mask);

                    *ptr = (byte)((*ptr & ~(mask << bitOffset)) | (value << bitOffset));

                    bitsLeft -= bitsToWrite;
                    bitOffset = 0;
                    ptr++;
                }
            }
            _positionInBits += bits;
        }

        public unsafe ulong ReadBits(byte bits)
        {
            if (bits > 64)
                throw new ArgumentOutOfRangeException(nameof(bits));

            int bytePos = _positionInBits >> 3;
            int bitOffset = _positionInBits & 7;

            ulong result;

            fixed (byte* b = &_buffer[bytePos])
            {
                if (bitOffset == 0)
                {
                    // Fast path: byte-aligned reads
                    switch (bits)
                    {
                        case 8:
                            result = *b;
                            break;
                        case 16:
                            result = *(ushort*)b;
#if PURR_ENDIAN
                            if (!BitConverter.IsLittleEndian)
                                result = BinaryPrimitives.ReverseEndianness((ushort)result);
#endif
                            break;
                        case 32:
                            result = *(uint*)b;
#if PURR_ENDIAN
                            if (!BitConverter.IsLittleEndian)
                                result = BinaryPrimitives.ReverseEndianness((uint)result);
#endif
                            break;
                        case 64:
                            result = *(ulong*)b;
#if PURR_ENDIAN
                            if (!BitConverter.IsLittleEndian)
                                result = BinaryPrimitives.ReverseEndianness(result);
#endif
                            break;
                        default:
                            if (bits <= 8)
                            {
                                ulong mask = (1UL << bits) - 1;
                                result = *b & mask;
                            }
                            else
                            {
                                // Read full bytes + remainder (always little-endian order)
                                int fullBytes = bits >> 3;
                                int remainderBits = bits & 7;

                                result = 0;
                                for (int i = 0; i < fullBytes; i++)
                                    result |= (ulong)b[i] << (i * 8);

                                if (remainderBits > 0)
                                {
                                    ulong mask = (1UL << remainderBits) - 1;
                                    result |= (b[fullBytes] & mask) << (fullBytes * 8);
                                }
                            }
                            break;
                    }
                }
                else
                {
                    // Unaligned read - read as 64-bit and extract bits
                    int totalBits = bits + bitOffset;

                    if (totalBits <= 64)
                    {
                        ulong data = *(ulong*)b;
#if PURR_ENDIAN
                        if (!BitConverter.IsLittleEndian)
                            data = BinaryPrimitives.ReverseEndianness(data);
#endif
                        ulong mask = (1UL << bits) - 1;
                        result = (data >> bitOffset) & mask;
                    }
                    else
                    {
                        goto SlowPath;
                    }
                }
            }

            _positionInBits += bits;
            return result;

        SlowPath:
            // Fallback: manual bit-by-bit read (already little-endian safe)
            result = 0;
            int bitsLeft = bits;

            while (bitsLeft > 0)
            {
                bytePos = _positionInBits >> 3;
                bitOffset = _positionInBits & 7;
                int bitsToRead = Math.Min(bitsLeft, 8 - bitOffset);

                byte mask = (byte)((1 << bitsToRead) - 1);
                byte value = (byte)((_buffer[bytePos] >> bitOffset) & mask);

                result |= (ulong)value << (bits - bitsLeft);

                bitsLeft -= bitsToRead;
                _positionInBits += bitsToRead;
            }

            return result;
        }

        public void ReadBytes(Span<byte> destination)
        {
            int count = destination.Length;
            int fullChunks = count / 8;
            int excess = count % 8;
            int index = 0;

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                ulong longValue = ReadBits(64);

                // Write back as little-endian
                BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(index, 8), longValue);
                index += 8;
            }

            // Process remaining excess bytes
            for (int i = 0; i < excess; i++)
            {
                destination[index++] = (byte)ReadBits(8);
            }
        }

        public void WriteBytes(ByteData byteData)
        {
            WriteBytes(byteData.span);
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            EnsureBitsExist(bytes.Length * 8);

            int count = bytes.Length;
            int fullChunks = count / 8;
            int excess = count % 8;
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
            {
                WriteBitsWithoutChecks(bytes[index++], 8);
            }
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
                int bytePos = positionInBits / 8;
                int bitOffset = positionInBits % 8;
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
            return newPacker;
        }

        public bool Equals(BitPacker packerB)
        {
            if (ReferenceEquals(this, packerB))
                return true;

            if (packerB == null)
                return false;

            if (this.positionInBits != packerB.positionInBits)
                return false;

            int bits = this.positionInBits;

            this.ResetPositionAndMode(true);
            packerB.ResetPositionAndMode(true);

            while (bits >= 64)
            {
                ulong aBits = this.ReadBits(64);
                ulong bBits = packerB.ReadBits(64);

                if (aBits != bBits)
                {
                    this.SetBitPosition(bits);
                    packerB.SetBitPosition(bits);
                    return false;
                }

                bits -= 64;
            }

            if (bits > 0)
            {
                var remainingBits = (byte)bits;
                ulong aBits = this.ReadBits(remainingBits);
                ulong bBits = packerB.ReadBits(remainingBits);
                if (aBits != bBits)
                {
                    this.SetBitPosition(bits);
                    packerB.SetBitPosition(bits);
                    return false;
                }
            }

            this.SetBitPosition(bits);
            packerB.SetBitPosition(bits);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetDeterministicHash32()
        {
            // FNV-1a 32-bit
            const uint offset = 2166136261u;
            const uint prime = 16777619u;

            int bits = _positionInBits;
            int fullBytes = bits >> 3;
            int tailBits = bits & 7;

            uint hash = offset;

            // Hash full bytes
            for (int i = 0; i < fullBytes; i++)
            {
                hash ^= _buffer[i];
                hash *= prime;
            }

            // Hash partial tail byte (only the written low bits)
            if (tailBits != 0)
            {
                byte mask = (byte)((1 << tailBits) - 1); // keeps low tailBits
                byte tail = (byte)(_buffer[fullBytes] & mask);

                hash ^= tail;
                hash *= prime;

                // Optional: include bit-count so different bit-lengths can't collide
                // when tail byte value matches (e.g. 1-bit '1' vs 2-bit '01' cases).
                hash ^= (byte)tailBits;
                hash *= prime;
            }
            else
            {
                // Still mix in length in bits to distinguish e.g. [0x00] vs empty.
                hash ^= 0u;
                hash *= prime;
            }

            // Mix in exact bit length (strongly recommended)
            unchecked
            {
                hash ^= (uint)bits;
                hash *= prime;
            }

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetDeterministicHash64()
        {
            // FNV-1a 64-bit
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            int bits = _positionInBits;
            int fullBytes = bits >> 3;
            int tailBits = bits & 7;

            ulong hash = offset;

            for (int i = 0; i < fullBytes; i++)
            {
                hash ^= _buffer[i];
                hash *= prime;
            }

            if (tailBits != 0)
            {
                byte mask = (byte)((1 << tailBits) - 1);
                byte tail = (byte)(_buffer[fullBytes] & mask);

                hash ^= tail;
                hash *= prime;

                hash ^= (byte)tailBits;
                hash *= prime;
            }

            unchecked
            {
                hash ^= (ulong)(uint)bits;
                hash *= prime;
            }

            return hash;
        }
    }
}
