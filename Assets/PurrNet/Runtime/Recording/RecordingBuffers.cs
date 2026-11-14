using System;
using System.Globalization;
using System.Text;

namespace PurrNet.Recording
{
    public sealed class RecordingBuffer
    {
        public byte[] data;
        public int length;
        readonly ByteShelf _shelf;

        public RecordingBuffer(int capacity = 256)
        {
            data = new byte[Math.Max(16, capacity)];
        }

        public RecordingBuffer(ByteShelf shelf, int capacity)
        {
            _shelf = shelf;
            data = shelf.Take(Math.Max(16, capacity));
        }

        public void Clear() => length = 0;

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

        public void CopyFrom(RecordingBuffer other)
        {
            length = 0;
            WriteBytes(other.data, 0, other.length);
        }

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

    public sealed class ByteShelf
    {
        public static readonly ByteShelf shared = new ByteShelf();

        readonly System.Collections.Generic.List<byte[]> _free = new System.Collections.Generic.List<byte[]>();

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
