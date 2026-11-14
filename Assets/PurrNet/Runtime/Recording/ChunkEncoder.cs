using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Mathf = UnityEngine.Mathf;

namespace PurrNet.Recording
{
    public sealed class RecordedLife
    {
        public ulong tick;
        public bool changed;
    }

    public sealed class StreamRows
    {
        public int id;
        public RowLayout layout;
        public bool forwardFill;
        public RecordedLife life;
        internal ulong aliveTick;
        internal int lastOffset;
        public StreamRows baseStream;
        public int[] baseColumns;
        internal int slot = -1;
        internal int rowCount;
        internal RecordingBuffer[] lastBytes = Array.Empty<RecordingBuffer>();
        internal object[] lastObject = Array.Empty<object>();
        internal int[] lastVersion = Array.Empty<int>();
        internal object box;
        internal bool retired;

        public bool hasRows => rowCount > 0;

        internal ulong lastTick => life != null && life.tick > aliveTick ? life.tick : aliveTick;

        public void Retire()
        {
            aliveTick = lastTick;
            life = null;
            retired = true;
        }
    }

    public struct ChunkStream
    {
        public int id;
        public RowLayout layout;
        public bool forwardFill;
        public int[] baseColumns;
        public int baseSlot;
        public ulong lastTick;
    }

    public sealed class ChunkLog
    {
        public readonly RecordingBuffer bytes;
        public readonly List<ChunkStream> streams = new List<ChunkStream>();
        public ulong firstTick;
        public ulong tickCount;
        public const int TickFollows = int.MinValue;
        internal ulong lastTick;
        internal bool hasTick;

        public ChunkLog() => bytes = new RecordingBuffer(1 << 16);

        public ChunkLog(ByteShelf shelf) => bytes = new RecordingBuffer(shelf, 1 << 16);

        public void Clear()
        {
            bytes.Clear();
            streams.Clear();
            hasTick = false;
        }
    }

    public sealed class ChunkEncoder
    {
        static readonly uint[] CrcTables = BuildCrcTables();

        static uint[] BuildCrcTables()
        {
            var tables = new uint[8 * 256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                tables[i] = c;
            }
            for (int t = 1; t < 8; t++)
                for (int i = 0; i < 256; i++)
                {
                    uint previous = tables[(t - 1) * 256 + i];
                    tables[t * 256 + i] = (previous >> 8) ^ tables[previous & 0xFF];
                }
            return tables;
        }

        readonly ByteShelf _shelf = ByteShelf.shared;
        readonly RecordingBuffer _buffer, _work, _coded;

        public ChunkEncoder()
        {
            _buffer = new RecordingBuffer(_shelf, 4096);
            _work = new RecordingBuffer(_shelf, 1024);
            _coded = new RecordingBuffer(_shelf, 1024);
        }
        readonly List<int> _keep = new List<int>();
        readonly Dictionary<RowLayout, bool[]> _numericMasks = new Dictionary<RowLayout, bool[]>();
        byte[] _plane = Array.Empty<byte>();

        int[] _slotFirst = Array.Empty<int>();
        int[] _slotCount = Array.Empty<int>();
        int[] _slotFill = Array.Empty<int>();
        int[] _slotManagedFirst = Array.Empty<int>();
        byte[] _rows = Array.Empty<byte>();
        int[] _rowOffset = Array.Empty<int>();
        ulong[] _ticks = Array.Empty<ulong>();
        int[] _valueStart = Array.Empty<int>();
        int[] _valueLength = Array.Empty<int>();
        int[] _entryOf = Array.Empty<int>();

        static void Grow<T>(ref T[] array, int size)
        {
            if (array.Length < size)
                array = new T[Math.Max(Mathf.NextPowerOfTwo(size), array.Length * 2)];
        }

        public void Reserve(int logCapacity)
        {
            Grow(ref _rows, logCapacity);
            Grow(ref _rowOffset, logCapacity / 16);
            Grow(ref _ticks, logCapacity / 16);
            _buffer.Reserve(logCapacity);
        }

        public RecordingBuffer Encode(ChunkLog log)
        {
            Begin(log);
            Step(long.MaxValue);
            return _buffer;
        }

        enum Phase { Count, Layout, Scatter, Header, Streams, Done }

        ChunkLog _log;
        Phase _phase = Phase.Done;
        int _at, _slot;
        ulong _tick;

        public RecordingBuffer result => _buffer;

        public void Begin(ChunkLog log)
        {
            _log = log;
            _phase = Phase.Count;
            _at = 0;
            _tick = 0;
            _slot = 0;
            int slots = log.streams.Count;
            Grow(ref _slotFirst, slots);
            Grow(ref _slotCount, slots);
            Grow(ref _slotFill, slots);
            Grow(ref _slotManagedFirst, slots);
            Array.Clear(_slotCount, 0, slots);
        }

        public bool Step(long deadline)
        {
            var log = _log;
            var streams = log.streams;
            byte[] data = log.bytes.data;
            int end = log.bytes.length;
            int work = 0;
            while (true)
            {
                switch (_phase)
                {
                    case Phase.Count:
                        while (_at < end)
                        {
                            if ((++work & 255) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                                return false;
                            int header = ReadInt(data, _at);
                            int slot = header & ~ChunkLog.TickFollows;
                            _at += header < 0 ? 12 : 4;
                            _slotCount[slot]++;
                            _at = SkipEntry(data, _at, streams[slot].layout);
                        }
                        _phase = Phase.Layout;
                        break;

                    case Phase.Layout:
                        Layout(streams);
                        _at = 0;
                        _tick = 0;
                        _phase = Phase.Scatter;
                        break;

                    case Phase.Scatter:
                        while (_at < end)
                        {
                            if ((++work & 255) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                                return false;
                            _at = ScatterEntry(data, _at, streams);
                        }
                        _phase = Phase.Header;
                        break;

                    case Phase.Header:
                        _buffer.Clear();
                        _buffer.WriteVarUInt(log.firstTick);
                        _buffer.WriteVarUInt(log.tickCount);
                        int blocks = 0;
                        for (int i = 0; i < streams.Count; i++)
                            if (_slotCount[i] > 0)
                                blocks++;
                        _buffer.WriteVarUInt((ulong)blocks);
                        _slot = 0;
                        _phase = Phase.Streams;
                        break;

                    case Phase.Streams:
                        while (_slot < streams.Count)
                        {
                            int slot = _slot++;
                            if (_slotCount[slot] == 0)
                                continue;
                            EncodeStream(log, slot);
                            if (_slot < streams.Count && System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                                return false;
                        }
                        _phase = Phase.Done;
                        _log = null;
                        return true;

                    default:
                        return true;
                }
            }
        }

        void EncodeStream(ChunkLog log, int slot)
        {
            var buffer = _buffer;
            var work = _work;
            var coded = _coded;
            var keep = _keep;
            byte[] plane = _plane;
            byte[] logBytes = log.bytes.data;

            int entries = _slotCount[slot];
            var s = log.streams[slot];
            int size = s.layout.rowSize;
            int first = _slotFirst[slot];
            int managedFields = s.layout.managed.Count;
            ulong firstTick = _ticks[first];

            int n = entries;
            if (s.forwardFill)
                n = (int)(Math.Max(Math.Min(s.lastTick, log.firstTick + log.tickCount - 1), _ticks[first + entries - 1]) - firstTick) + 1;
            Grow(ref _entryOf, n);
            var entryOf = _entryOf;
            work.Clear();
            work.Ensure(n * size);
            for (int r = 0, e = 0; r < n; r++)
            {
                if (!s.forwardFill)
                    e = r;
                else
                    while (e + 1 < entries && _ticks[first + e + 1] <= firstTick + (ulong)r)
                        e++;
                entryOf[r] = e;
                Buffer.BlockCopy(_rows, _rowOffset[first + e], work.data, r * size, size);
            }
            work.length = n * size;
            uint crc = s.forwardFill ? Crc32(work.data, 0, n * size) : 0;

            if (s.baseSlot >= 0 && _slotCount[s.baseSlot] > 0)
                XorWithBase(s, first, n, _slotFirst[s.baseSlot], _slotCount[s.baseSlot], work.data);

            keep.Clear();
            int managedBase = _slotManagedFirst[slot];
            for (int r = 0; r < n; r++)
            {
                if (r == 0 || !s.forwardFill ||
                    !SameRow(work.data, r, keep[keep.Count - 1], size, managedBase, managedFields, logBytes))
                    keep.Add(r);
            }
            int k = keep.Count;

            buffer.WriteVarUInt((ulong)s.id);
            buffer.WriteVarUInt((ulong)k);
            ulong previous = log.firstTick;
            foreach (int r in keep)
            {
                ulong tick = s.forwardFill ? firstTick + (ulong)r : _ticks[first + r];
                buffer.WriteVarUInt(tick - previous);
                previous = tick;
            }
            if (s.forwardFill)
                buffer.WriteVarUInt((ulong)(n - 1 - keep[0]));

            int total = k * size;
            if (total > 0)
            {
                coded.Clear();
                coded.Ensure(total);
                for (int i = 0; i < k; i++)
                    Buffer.BlockCopy(work.data, keep[i] * size, coded.data, i * size, size);
                coded.length = total;
                CodeColumns(s.layout, coded.data, k);

                if (plane.Length < total)
                {
                    if (plane.Length > 0)
                        _shelf.Put(plane);
                    plane = _shelf.Take(Mathf.NextPowerOfTwo(total));
                }
                for (int r = 0; r < k; r++)
                    for (int b = 0; b < size; b++)
                        plane[b * k + r] = coded.data[r * size + b];
                buffer.WriteBytes(plane, 0, total);
            }

            for (int f = 0; f < managedFields; f++)
            {
                int last = -1;
                foreach (int r in keep)
                {
                    int v = managedBase + entryOf[r] * managedFields + f;
                    if (last >= 0 && SameValue(logBytes, last, v))
                    {
                        buffer.WriteVarUInt(0);
                        continue;
                    }
                    buffer.WriteVarUInt((ulong)_valueLength[v] + 1);
                    buffer.WriteBytes(logBytes, _valueStart[v], _valueLength[v]);
                    last = v;
                }
            }

            if (s.forwardFill)
            {
                for (int i = 0; i < 4; i++)
                    buffer.WriteByte((byte)(crc >> (8 * i)));
            }

            _plane = plane;
        }

        void Layout(List<ChunkStream> streams)
        {
            int slots = streams.Count;
            int rowTotal = 0, rowBytes = 0, managedTotal = 0;
            for (int i = 0; i < slots; i++)
            {
                _slotFirst[i] = rowTotal;
                _slotManagedFirst[i] = managedTotal;
                _slotFill[i] = 0;
                rowTotal += _slotCount[i];
                rowBytes += _slotCount[i] * streams[i].layout.rowSize;
                managedTotal += _slotCount[i] * streams[i].layout.managed.Count;
            }
            Grow(ref _rows, rowBytes);
            Grow(ref _rowOffset, rowTotal);
            Grow(ref _ticks, rowTotal);
            Grow(ref _valueStart, managedTotal);
            Grow(ref _valueLength, managedTotal);

            int offset = 0;
            for (int i = 0; i < slots; i++)
            {
                int size = streams[i].layout.rowSize;
                for (int r = 0; r < _slotCount[i]; r++)
                    _rowOffset[_slotFirst[i] + r] = offset + r * size;
                offset += _slotCount[i] * size;
            }
        }

        int ScatterEntry(byte[] data, int at, List<ChunkStream> streams)
        {
            int header = ReadInt(data, at);
            int slot = header & ~ChunkLog.TickFollows;
            if (header < 0)
            {
                _tick = ReadULong(data, at + 4);
                at += 12;
            }
            else
                at += 4;
            var layout = streams[slot].layout;
            int r = _slotFill[slot]++;
            int row = _slotFirst[slot] + r;
            _ticks[row] = _tick;
            Buffer.BlockCopy(data, at, _rows, _rowOffset[row], layout.rowSize);
            at += layout.rowSize;

            int fields = layout.managed.Count;
            int managedRow = _slotManagedFirst[slot] + r * fields;
            for (int f = 0; f < fields; f++)
            {
                int length = ReadInt(data, at);
                at += 4;
                int v = managedRow + f;
                if (length < 0)
                {
                    _valueStart[v] = _valueStart[v - fields];
                    _valueLength[v] = _valueLength[v - fields];
                    continue;
                }
                _valueStart[v] = at;
                _valueLength[v] = length;
                at += length;
            }
            return at;
        }

        static int SkipEntry(byte[] data, int at, RowLayout layout)
        {
            at += layout.rowSize;
            for (int f = 0; f < layout.managed.Count; f++)
            {
                int length = ReadInt(data, at);
                at += 4 + Math.Max(0, length);
            }
            return at;
        }

        static int ReadInt(byte[] data, int at) =>
            data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24;

        static ulong ReadULong(byte[] data, int at) =>
            (uint)ReadInt(data, at) | (ulong)(uint)ReadInt(data, at + 4) << 32;

        unsafe bool SameValue(byte[] log, int a, int b)
        {
            int length = _valueLength[a];
            if (length != _valueLength[b])
                return false;
            int sa = _valueStart[a], sb = _valueStart[b];
            if (sa == sb)
                return true;
            if (length == 0)
                return true;
            fixed (byte* pa = &log[sa])
            fixed (byte* pb = &log[sb])
                return UnsafeUtility.MemCmp(pa, pb, length) == 0;
        }

        public static unsafe uint Crc32(byte[] data, int offset, int length)
        {
            if ((uint)offset > (uint)data.Length || (uint)length > (uint)(data.Length - offset))
                throw new ArgumentOutOfRangeException(nameof(length));
            uint crc = 0xFFFFFFFFu;
            if (length == 0)
                return 0;
            fixed (byte* start = &data[offset])
            fixed (uint* t = CrcTables)
            {
                byte* p = start;
                for (; length >= 8; length -= 8, p += 8)
                {
                    uint one = (uint)(p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24) ^ crc;
                    crc = t[7 * 256 + (one & 0xFF)] ^ t[6 * 256 + ((one >> 8) & 0xFF)] ^
                          t[5 * 256 + ((one >> 16) & 0xFF)] ^ t[4 * 256 + (one >> 24)] ^
                          t[3 * 256 + p[4]] ^ t[2 * 256 + p[5]] ^ t[256 + p[6]] ^ t[p[7]];
                }
                for (; length > 0; length--, p++)
                    crc = t[(crc ^ *p) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        void XorWithBase(in ChunkStream s, int first, int n, int baseFirst, int baseCount, byte[] rows)
        {
            int size = s.layout.rowSize;
            int j = -1;
            for (int r = 0; r < n; r++)
            {
                ulong tick = s.forwardFill ? _ticks[first] + (ulong)r : _ticks[first + r];
                while (j + 1 < baseCount && _ticks[baseFirst + j + 1] <= tick)
                    j++;
                if (j < 0)
                    continue;
                int baseRow = _rowOffset[baseFirst + j];
                for (int c = 0; c < s.baseColumns.Length; c += 2)
                {
                    int own = r * size + s.baseColumns[c], other = baseRow + s.baseColumns[c + 1];
                    for (int i = 0; i < 4; i++)
                        rows[own + i] ^= _rows[other + i];
                }
            }
        }

        unsafe bool SameRow(byte[] rows, int r, int previous, int size, int managedBase, int managedFields, byte[] log)
        {
            if (size > 0)
            {
                fixed (byte* a = &rows[r * size])
                fixed (byte* b = &rows[previous * size])
                    if (UnsafeUtility.MemCmp(a, b, size) != 0)
                        return false;
            }
            int own = managedBase + _entryOf[r] * managedFields, other = managedBase + _entryOf[previous] * managedFields;
            for (int f = 0; own != other && f < managedFields; f++)
                if (!SameValue(log, own + f, other + f))
                    return false;
            return true;
        }

        void CodeColumns(RowLayout layout, byte[] rows, int count)
        {
            int size = layout.rowSize;
            var isNumeric = NumericMask(layout);

            for (int r = count - 1; r > 0; r--)
            {
                int at = r * size, prev = at - size;
                for (int b = 0; b < size; b++)
                    if (!isNumeric[b])
                        rows[at + b] ^= rows[prev + b];
            }

            foreach (var c in layout.columns)
            {
                if (c.size == 4 && IsWideNumeric(c.type))
                {
                    uint prev = 0, prevDelta = 0;
                    for (int r = 0; r < count; r++)
                    {
                        int at = r * size + c.offset;
                        uint v = (uint)(rows[at] | rows[at + 1] << 8 | rows[at + 2] << 16 | rows[at + 3] << 24);
                        uint delta = v - prev, dd = delta - prevDelta;
                        prev = v;
                        prevDelta = delta;
                        uint z = (dd << 1) ^ (uint)((int)dd >> 31);
                        rows[at] = (byte)z;
                        rows[at + 1] = (byte)(z >> 8);
                        rows[at + 2] = (byte)(z >> 16);
                        rows[at + 3] = (byte)(z >> 24);
                    }
                }
                else if (c.size == 8 && IsWideNumeric(c.type))
                {
                    ulong prev = 0, prevDelta = 0;
                    for (int r = 0; r < count; r++)
                    {
                        int at = r * size + c.offset;
                        ulong v = 0;
                        for (int i = 0; i < 8; i++)
                            v |= (ulong)rows[at + i] << (8 * i);
                        ulong delta = v - prev, dd = delta - prevDelta;
                        prev = v;
                        prevDelta = delta;
                        ulong z = (dd << 1) ^ (ulong)((long)dd >> 63);
                        for (int i = 0; i < 8; i++)
                            rows[at + i] = (byte)(z >> (8 * i));
                    }
                }
            }
        }

        static bool IsWideNumeric(string type) =>
            type == "f32" || type == "i32" || type == "u32" || type == "f64" || type == "i64" || type == "u64";

        bool[] NumericMask(RowLayout layout)
        {
            if (_numericMasks.TryGetValue(layout, out var mask))
                return mask;
            mask = new bool[layout.rowSize];
            foreach (var c in layout.columns)
                if ((c.size == 4 || c.size == 8) && IsWideNumeric(c.type))
                    for (int i = 0; i < c.size; i++)
                        mask[c.offset + i] = true;
            _numericMasks[layout] = mask;
            return mask;
        }
    }
}
