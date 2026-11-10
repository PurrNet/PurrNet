using System;
using System.Globalization;
using System.Text;

namespace PurrNet.Recording
{
    /// <summary>
    /// Growable byte buffer with the little-endian and LEB128 writers the format uses.
    /// </summary>
    public sealed class RecordingBuffer
    {
        public byte[] data;
        public int length;
        /// <summary>When set, growing takes the larger array from here and leaves the old one for the next buffer.</summary>
        readonly ByteShelf _shelf;

        public RecordingBuffer(int capacity = 256)
        {
            data = new byte[Math.Max(16, capacity)];
        }

        /// <summary>A buffer whose arrays come from (and go back to) <paramref name="shelf"/>, so growing makes no garbage.</summary>
        public RecordingBuffer(ByteShelf shelf, int capacity)
        {
            _shelf = shelf;
            data = shelf.Take(Math.Max(16, capacity));
        }

        public void Clear() => length = 0;

        /// <summary>Grows the array now (a load moment) so later writes up to <paramref name="capacity"/> do not.</summary>
        public void Reserve(int capacity)
        {
            if (data.Length < capacity)
                Ensure(capacity - length);
        }

        public void Ensure(int extra)
        {
            int needed = length + extra;
            if (needed <= data.Length)
                return;
            int size = data.Length;
            while (size < needed)
                size *= 2;
            if (_shelf == null)
            {
                Array.Resize(ref data, size);
                return;
            }
            var next = _shelf.Take(size);
            Buffer.BlockCopy(data, 0, next, 0, length);
            _shelf.Put(data);
            data = next;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            data[length++] = value;
        }

        public void WriteBytes(byte[] bytes, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(bytes, offset, data, length, count);
            length += count;
        }

        public void WriteBytes(RecordingBuffer other) => WriteBytes(other.data, 0, other.length);

        public unsafe void WriteBytes(byte* source, int count)
        {
            if (count <= 0)
                return;
            Ensure(count);
            fixed (byte* dst = &data[length])
                Buffer.MemoryCopy(source, dst, count, count);
            length += count;
        }

        /// <summary>Replaces the contents with <paramref name="other"/>'s, keeping this buffer's array when it fits.</summary>
        public void CopyFrom(RecordingBuffer other)
        {
            length = 0;
            WriteBytes(other.data, 0, other.length);
        }

        /// <summary>Writes a string's UTF-8 bytes (no length prefix) without allocating.</summary>
        public void WriteUtf8(string value)
        {
            Ensure(Encoding.UTF8.GetMaxByteCount(value.Length));
            length += Encoding.UTF8.GetBytes(value, 0, value.Length, data, length);
        }

        public bool SameBytes(RecordingBuffer other) => SameBytes(0, length, other.data, 0, other.length);

        public void WriteVarUInt(ulong value)
        {
            Ensure(10);
            while (value >= 0x80)
            {
                data[length++] = (byte)(value | 0x80);
                value >>= 7;
            }
            data[length++] = (byte)value;
        }

        public void WriteInt32(int value)
        {
            Ensure(4);
            data[length++] = (byte)value;
            data[length++] = (byte)(value >> 8);
            data[length++] = (byte)(value >> 16);
            data[length++] = (byte)(value >> 24);
        }

        public void WriteUInt64(ulong value)
        {
            Ensure(8);
            for (int i = 0; i < 8; i++)
                data[length++] = (byte)(value >> (8 * i));
        }

        public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

        public unsafe void WriteFloat(float value)
        {
            Ensure(4);
            uint bits = *(uint*)&value;
            for (int i = 0; i < 4; i++)
                data[length++] = (byte)(bits >> (8 * i));
        }

        public unsafe void WriteDouble(double value)
        {
            Ensure(8);
            ulong bits = *(ulong*)&value;
            for (int i = 0; i < 8; i++)
                data[length++] = (byte)(bits >> (8 * i));
        }

        public void WriteString(string value)
        {
            int count = Encoding.UTF8.GetByteCount(value);
            WriteVarUInt((ulong)count);
            Ensure(count);
            Encoding.UTF8.GetBytes(value, 0, value.Length, data, length);
            length += count;
        }

        public byte[] ToArray()
        {
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            return copy;
        }

        public bool SameBytes(int offset, int count, byte[] other, int otherOffset, int otherCount)
        {
            if (count != otherCount)
                return false;
            for (int i = 0; i < count; i++)
                if (data[offset + i] != other[otherOffset + i])
                    return false;
            return true;
        }

        public bool SameBytes(int offset, int count, byte[] other)
        {
            if (other == null || other.Length != count)
                return false;
            for (int i = 0; i < count; i++)
                if (data[offset + i] != other[i])
                    return false;
            return true;
        }
    }

    /// <summary>
    /// Byte arrays that buffers grew out of, kept for the next buffer that needs one. Chunk logs grow to megabytes
    /// early in a match; with a shelf the arrays they leave behind are reused instead of becoming garbage, so the
    /// recorder never triggers a collection by growing.
    /// </summary>
    public sealed class ByteShelf
    {
        /// <summary>The recorder's shelf: chunk logs, metadata batches, the encoder and compressed output share it.</summary>
        public static readonly ByteShelf shared = new ByteShelf();

        readonly System.Collections.Generic.List<byte[]> _free = new System.Collections.Generic.List<byte[]>();

        /// <summary>
        /// The smallest kept array of at least <paramref name="size"/> bytes, or a new one. Arrays more than four times
        /// too big are left for buffers that need them, so a small buffer never holds megabytes.
        /// </summary>
        public byte[] Take(int size)
        {
            lock (_free)
            {
                int best = -1;
                for (int i = 0; i < _free.Count; i++)
                    if (_free[i].Length >= size && _free[i].Length / 4 <= size &&
                        (best < 0 || _free[i].Length < _free[best].Length))
                        best = i;
                if (best >= 0)
                    return TakeAt(best);
            }
            return new byte[size];
        }

        byte[] TakeAt(int index)
        {
            var array = _free[index];
            _free[index] = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            return array;
        }

        public void Put(byte[] array)
        {
            lock (_free)
                _free.Add(array);
        }

        public int count
        {
            get { lock (_free) return _free.Count; }
        }
    }
}
