#if UNITY_WEBGL && !UNITY_EDITOR
#define PURRREC_WEB
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

namespace PurrNet.Recording
{
    public enum RecordKind : byte
    {
        Header = 1,
        Type = 2,
        Stream = 3,
        Entity = 4,
        Despawn = 5,
        Chunk = 6,
        Colliders = 7,
        Player = 8,
        Footer = 9,
        Mesh = 10,
        Batch = 11,
        Attach = 12,
        Detach = 13,
        Part = 14,
        Owner = 15,
        Observers = 16,
        Renderers = 17,
        Effects = 18,
        Terrain = 19
    }

    interface IStepwiseRecordProducer : IRecordProducer
    {
        void Begin();

        bool Step(long deadline);
    }

    public interface IRecordProducer
    {
        RecordingBuffer Produce();

        void Release();
    }

    public sealed class RecordingFile : IDisposable
    {
        struct PendingRecord
        {
            public RecordKind kind;
            public byte[] payload;
            public IRecordProducer producer;
            public bool compress;
            public IntPtr native;
            public int nativeLength;
#if !PURRREC_WEB
            public List<float[,]> terrainHeights;
            public byte[] terrainHoles;
#endif
        }

        readonly Queue<PendingRecord> _queue = new Queue<PendingRecord>(64);
        long _rawBytes;
#if !PURRREC_WEB
        long _pendingTerrainBytes;
#endif
        volatile Exception _error;

        /// <summary>
        /// What went wrong writing the file (the disk is full, the file was taken away), or null. Nothing more is
        /// written after it: records are dropped as they come.
        /// </summary>
        public Exception error => _error;
        volatile bool _busy;
        volatile bool _closed;

        /// <summary>Everything that was added is in the file and the file is closed: it can be read, sent or deleted.</summary>
        public bool closed => _closed;
        long _produceTicks, _maxRecordTicks;
#if !PURRREC_WEB
        bool _completed;
        readonly Thread _thread;
        Thread _after;
        FileStream _stream;
        long _bytesWritten, _compressTicks;
        readonly BufferStream _compressed = new BufferStream(new RecordingBuffer(ByteShelf.shared, 1 << 16));
        GCHandle _compressedHandle;
        readonly NativeSink _nativeSink = new NativeSink();
        GCHandle _nativeSinkHandle;
        readonly byte[] _slice = new byte[1 << 16];
#endif

        public double pumpBudgetMs { get; set; } = 1.0;

        public double produceMs => Interlocked.Read(ref _produceTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public double maxRecordMs => Interlocked.Read(ref _maxRecordTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#if PURRREC_WEB
        public double compressMs => WebRecording.Stat(_web, WebRecording.StatCompressMs);
        public bool nativeDeflate => _web != 0 && WebRecording.Stat(_web, WebRecording.StatDeflate) != 0;
        public long bytesWritten => (long)WebRecording.Stat(_web, WebRecording.StatBytes);
#else
        public double compressMs => Interlocked.Read(ref _compressTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public bool nativeDeflate { get; private set; } = true;
        public long bytesWritten => Interlocked.Read(ref _bytesWritten);
#endif

        public bool busy
        {
            get
            {
                lock (_queue)
                    return _busy || _queue.Count > 0;
            }
        }

        public string path { get; }

#if PURRREC_WEB
        public static bool encodesOnMainThread => true;
        internal long pendingTerrainBytes => 0;
#else
        public static bool encodesOnMainThread => false;
        internal long pendingTerrainBytes => Interlocked.Read(ref _pendingTerrainBytes);
#endif
        public long rawBytes => Interlocked.Read(ref _rawBytes);

#if PURRREC_WEB
        readonly int _web;
        readonly RecordingBuffer _header = new RecordingBuffer(16);

        public RecordingFile(string path, long maxLocalBytes, RecordingFile after = null)
        {
            this.path = path;
            _web = WebRecording.Open(Path.GetFileName(path), maxLocalBytes);
            if (_web == 0)
                return;
            var magic = new RecordingBuffer(16);
            magic.WriteBytes(Encoding.ASCII.GetBytes("PURRREC\0"), 0, 8);
            magic.WriteByte(1);
            magic.WriteByte(0);
            WebRecording.Append(_web, -1, false, magic.data, magic.length);
        }

        bool _stepping;

        public void Pump()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long deadline = start + (long)(pumpBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            while (_queue.Count > 0)
            {
                if (_error == null && _queue.Peek().producer is IStepwiseRecordProducer stepwise)
                {
                    if (!_stepping)
                    {
                        stepwise.Begin();
                        _stepping = true;
                    }
                    if (!stepwise.Step(deadline))
                        break;
                    _stepping = false;
                }
                WriteRecord(_queue.Dequeue(), _header);
                if (System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                    break;
            }
            long spent = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            _produceTicks += spent;
            if (spent > _maxRecordTicks)
                _maxRecordTicks = spent;
        }
#else
        public void Pump() { }

        /// <summary>
        /// The file is created, written and closed by a thread of its own: the caller never waits for the disk.
        /// <paramref name="after"/> is a file that was completed but may not be closed yet. This one's thread
        /// starts where that one's ends, so the two never work at the same time.
        /// </summary>
        public RecordingFile(string path, long maxLocalBytes, RecordingFile after = null)
        {
            this.path = path;
            _after = after?._thread;
            _compressedHandle = GCHandle.Alloc(_compressed.buffer);
            _nativeSinkHandle = GCHandle.Alloc(_nativeSink);
            _thread = new Thread(WriteLoop) { IsBackground = true, Name = "PurrDiction recording writer", Priority = System.Threading.ThreadPriority.BelowNormal };
            _thread.Start();
        }

        void WriteLoop()
        {
            UnityEngine.Profiling.Profiler.BeginThreadProfiling("PurrDiction", "Recording writer");
            try
            {
                _after?.Join();
                _after = null;
                Open();
                var header = new RecordingBuffer(16);
                while (Take(out var record))
                {
                    WriteRecord(record, header);
                    bool idle;
                    lock (_queue)
                        idle = _queue.Count == 0;
                    if (idle)
                        Flush();
                }
                Flush();
            }
            finally
            {
                Close();
                UnityEngine.Profiling.Profiler.EndThreadProfiling();
            }
        }

        void Open()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
                var magic = Encoding.ASCII.GetBytes("PURRREC\0");
                _stream.Write(magic, 0, magic.Length);
                _stream.WriteByte(1);
                _stream.WriteByte(0);
                Interlocked.Add(ref _bytesWritten, 10);
            }
            catch (Exception e)
            {
                _error = e;
            }
        }

        void Close()
        {
            if (_compressedHandle.IsAllocated)
                _compressedHandle.Free();
            if (_nativeSinkHandle.IsAllocated)
                _nativeSinkHandle.Free();
            _nativeSink.Free();
            try
            {
                _stream?.Dispose();
            }
            catch (Exception e)
            {
                _error ??= e;
            }
            _closed = true;
        }

        void Flush()
        {
            if (_error != null)
                return;
            try
            {
                _stream.Flush();
            }
            catch (Exception e)
            {
                _error = e;
            }
        }

        bool Take(out PendingRecord record)
        {
            // Release the previous snapshot before the writer blocks on an empty queue.
            record = default;
            lock (_queue)
            {
                while (_queue.Count == 0)
                {
                    if (_completed)
                        return false;
                    Monitor.Wait(_queue);
                }
                record = _queue.Dequeue();
                _busy = true;
                return true;
            }
        }
#endif

        public RecordingFile(string path) : this(path, 0) { }

        public void SetLink(string json)
        {
#if PURRREC_WEB
            if (_web != 0)
                WebRecording.Link(_web, json);
#endif
        }

        public void WriteJson(RecordKind kind, string json, bool compress = false)
        {
            Enqueue(kind, Encoding.UTF8.GetBytes(json), compress);
        }

        public void ReserveOutput(int bytes)
        {
#if !PURRREC_WEB
            _compressed.buffer.Reserve(bytes);
#endif
        }

        public void Enqueue(RecordKind kind, byte[] payload, bool compress)
        {
            if (_error != null)
                return;
            Interlocked.Add(ref _rawBytes, payload.Length);
            Add(new PendingRecord { kind = kind, payload = payload, compress = compress });
        }

        public void Enqueue(RecordKind kind, IRecordProducer producer, bool compress)
        {
            if (_error != null)
            {
                producer.Release();
                return;
            }
            Add(new PendingRecord { kind = kind, producer = producer, compress = compress });
        }

        /// <summary>
        /// A record whose payload is native memory (from UnsafeUtility.Malloc, Allocator.Persistent): the file takes
        /// it over, compresses and writes it from where it is, and frees it. For payloads of megabytes (meshes),
        /// which must not pass through the managed heap.
        /// </summary>
        public unsafe void EnqueueNative(RecordKind kind, IntPtr data, int length, bool compress)
        {
#if PURRREC_WEB
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, length);
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free((void*)data, Unity.Collections.Allocator.Persistent);
            Enqueue(kind, bytes, compress);
#else
            if (_error != null)
            {
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free((void*)data, Unity.Collections.Allocator.Persistent);
                return;
            }
            Interlocked.Add(ref _rawBytes, length);
            Add(new PendingRecord { kind = kind, native = data, nativeLength = length, compress = compress });
#endif
        }

        internal void EnqueueTerrain(RecordingBuffer prefix, List<float[,]> heights, byte[] holes)
        {
            if (_error != null)
                return;
            int heightBytes = 0;
            foreach (var rows in heights)
                heightBytes += rows.Length * sizeof(float);
            int holeBytes = holes?.Length ?? 0;
            int length = prefix.length + heightBytes + holeBytes;
#if PURRREC_WEB
            var bytes = new byte[length];
            Buffer.BlockCopy(prefix.data, 0, bytes, 0, prefix.length);
            int at = prefix.length;
            foreach (var rows in heights)
            {
                int rowBytes = rows.Length * sizeof(float);
                Buffer.BlockCopy(rows, 0, bytes, at, rowBytes);
                at += rowBytes;
            }
            if (holeBytes != 0)
                Buffer.BlockCopy(holes, 0, bytes, prefix.length + heightBytes, holeBytes);
            Enqueue(RecordKind.Terrain, bytes, true);
#else
            // The writer reads Unity's height snapshot directly. Only the small shared prefix needs a copy.
            Interlocked.Add(ref _rawBytes, length);
            var record = new PendingRecord
            {
                kind = RecordKind.Terrain, payload = prefix.ToArray(), compress = true,
                terrainHeights = heights, terrainHoles = holes, nativeLength = heightBytes
            };
            Interlocked.Add(ref _pendingTerrainBytes, heightBytes);
            Add(record);
#endif
        }

#if !PURRREC_WEB
        unsafe void WriteNative(RecordKind recordKind, ReadOnlySpan<byte> payload, bool compress, RecordingBuffer header,
            List<float[,]> heights = null, int heightBytes = 0, ReadOnlySpan<byte> holes = default)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int length = payload.Length + heightBytes + holes.Length;
            byte kind = (byte)recordKind;
            if (compress)
            {
                using var _ = CompressMarker.Auto();
                // Terrain grids can compress to a tiny fraction of their input, especially flat tiles.
                int reserve = length / 2 + 1024;
                bool terrain = recordKind == RecordKind.Terrain;
                if (terrain)
                    reserve = Math.Min(reserve, 64 * 1024);
                _nativeSink.Begin(reserve);
                bool compressed = NativeDeflate.TryCompress(payload, GCHandle.ToIntPtr(_nativeSinkHandle), heights, holes) && !_nativeSink.failed;
                if (!compressed && terrain)
                {
                    // The native Mono compressor is not present in all players. Stream from the same input
                    // through the reusable file slice if we have to use managed DEFLATE.
                    nativeDeflate = false;
                    _nativeSink.Begin(reserve);
                    using (var deflate = new DeflateStream(_nativeSink, System.IO.Compression.CompressionLevel.Fastest, true))
                    {
                        WriteSlices(deflate, payload);
                        WriteTerrainRows(deflate, heights);
                        WriteSlices(deflate, holes);
                    }
                    compressed = !_nativeSink.failed;
                }
                if (compressed && (!terrain || _nativeSink.length < length))
                {
                    length = _nativeSink.length;
                    payload = new ReadOnlySpan<byte>(_nativeSink.data, length);
                    heights = null;
                    holes = default;
                    kind |= 0x80;
                }
            }
            long compressedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            Interlocked.Add(ref _compressTicks, compressedAt - started);
            if (compressedAt - started > Interlocked.Read(ref _maxRecordTicks))
                Interlocked.Exchange(ref _maxRecordTicks, compressedAt - started);

            using var write = WriteMarker.Auto();
            header.Clear();
            header.WriteByte(kind);
            header.WriteVarUInt((ulong)length);
            _stream.Write(header.data, 0, header.length);
            WriteSlices(_stream, payload);
            WriteTerrainRows(_stream, heights);
            WriteSlices(_stream, holes);
            Interlocked.Add(ref _bytesWritten, header.length + length);
        }

        unsafe void WriteTerrainRows(Stream stream, List<float[,]> heights)
        {
            if (heights == null)
                return;
            foreach (var rows in heights)
                fixed (float* samples = &rows[0, 0])
                    WriteSlices(stream, new ReadOnlySpan<byte>(samples, rows.Length * sizeof(float)));
        }

        void WriteSlices(Stream stream, ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                int n = Math.Min(_slice.Length, source.Length);
                source.Slice(0, n).CopyTo(_slice);
                stream.Write(_slice, 0, n);
                source = source.Slice(n);
            }
        }
#endif

        static readonly ProfilerMarker EncodeMarker = new ProfilerMarker("Recording.Encode");
        static readonly ProfilerMarker CompressMarker = new ProfilerMarker("Recording.Compress");
        static readonly ProfilerMarker WriteMarker = new ProfilerMarker("Recording.Write");

        void Add(in PendingRecord record)
        {
            lock (_queue)
            {
                _queue.Enqueue(record);
                Monitor.Pulse(_queue);
            }
        }

        unsafe void WriteRecord(in PendingRecord record, RecordingBuffer header)
        {
            if (_error != null)
            {
                record.producer?.Release();
                ReleasePayload(record);
                return;
            }
            _busy = true;
            try
            {
#if !PURRREC_WEB
                if (record.native != IntPtr.Zero)
                {
                    WriteNative(record.kind, new ReadOnlySpan<byte>((void*)record.native, record.nativeLength), record.compress, header);
                    return;
                }
                if (record.terrainHeights != null)
                {
                    WriteNative(record.kind, record.payload, true, header, record.terrainHeights, record.nativeLength, record.terrainHoles);
                    return;
                }
#endif
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                byte[] payload = record.payload;
                int length = payload?.Length ?? 0;
                if (record.producer != null)
                {
                    using var _ = EncodeMarker.Auto();
                    var produced = record.producer.Produce();
                    payload = produced.data;
                    length = produced.length;
                    Interlocked.Add(ref _rawBytes, length);
                }
#if PURRREC_WEB
                if (_web != 0)
                    WebRecording.Append(_web, (int)record.kind, record.compress, payload, length);
#else
                long encoded = System.Diagnostics.Stopwatch.GetTimestamp();
                Interlocked.Add(ref _produceTicks, encoded - started);
                byte kind = (byte)record.kind;
                if (record.compress)
                {
                    using var _ = CompressMarker.Auto();
                    _compressed.buffer.Clear();
                    if (!NativeDeflate.TryCompress(payload.AsSpan(0, length), GCHandle.ToIntPtr(_compressedHandle)))
                    {
                        nativeDeflate = false;
                        _compressed.buffer.Clear();
                        using (var deflate = new DeflateStream(_compressed, System.IO.Compression.CompressionLevel.Optimal, true))
                            deflate.Write(payload, 0, length);
                    }
                    payload = _compressed.buffer.data;
                    length = _compressed.buffer.length;
                    kind |= 0x80;
                }
                long compressed = System.Diagnostics.Stopwatch.GetTimestamp();
                Interlocked.Add(ref _compressTicks, compressed - encoded);
                if (compressed - started > Interlocked.Read(ref _maxRecordTicks))
                    Interlocked.Exchange(ref _maxRecordTicks, compressed - started);

                using var write = WriteMarker.Auto();
                header.Clear();
                header.WriteByte(kind);
                header.WriteVarUInt((ulong)length);
                _stream.Write(header.data, 0, header.length);
                _stream.Write(payload, 0, length);
                Interlocked.Add(ref _bytesWritten, header.length + length);
#endif
            }
            catch (Exception e)
            {
                _error = e;
            }
            finally
            {
#if !PURRREC_WEB
                if (record.native != IntPtr.Zero || record.terrainHeights != null)
                    _nativeSink.Free();
#endif
                record.producer?.Release();
                ReleasePayload(record);
                _busy = false;
            }
        }

        unsafe void ReleasePayload(in PendingRecord record)
        {
#if !PURRREC_WEB
            if (record.terrainHeights != null)
                Interlocked.Add(ref _pendingTerrainBytes, -record.nativeLength);
#endif
            if (record.native != IntPtr.Zero)
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free((void*)record.native, Unity.Collections.Allocator.Persistent);
        }

        /// <summary>
        /// Nothing more will be added. The writer thread writes what is queued and closes the file, and
        /// <see cref="closed"/> says when: the caller does not wait. Without threads (WebGL) it is done at once.
        /// </summary>
        public void Complete()
        {
#if PURRREC_WEB
            Dispose();
#else
            lock (_queue)
            {
                _completed = true;
                Monitor.PulseAll(_queue);
            }
#endif
        }

        /// <summary>Completes the file and waits until it is closed.</summary>
        public void Dispose()
        {
#if PURRREC_WEB
            if (_closed)
                return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            while (_queue.Count > 0)
                WriteRecord(_queue.Dequeue(), _header);
            _produceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            if (_web != 0)
                WebRecording.Close(_web);
            _closed = true;
#else
            Complete();
            _thread.Join();
#endif
        }
    }

    sealed class BufferStream : Stream
    {
        public readonly RecordingBuffer buffer;

        public BufferStream(RecordingBuffer buffer) => this.buffer = buffer;

        public override void Write(byte[] source, int offset, int count) => buffer.WriteBytes(source, offset, count);
        public override void WriteByte(byte value) => buffer.WriteByte(value);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => buffer.length;
        public override long Position
        {
            get => buffer.length;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] destination, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

#if !PURRREC_WEB
    /// <summary>Compressed output that grows in native memory, for records too large to pass through the managed heap.</summary>
    sealed unsafe class NativeSink : Stream
    {
        public byte* data;
        public int length, capacity;
        public bool failed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            fixed (byte* source = buffer.AsSpan(offset, count))
                Write(source, count);
        }

        public void Begin(int reserve)
        {
            Free();
            capacity = Math.Max(1024, reserve);
            data = (byte*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Malloc(capacity, 1, Unity.Collections.Allocator.Persistent);
            length = 0;
            failed = false;
        }

        public void Write(byte* source, int count)
        {
            if (failed || data == null)
                return;
            if ((long)length + count > capacity)
            {
                long grown = Math.Max((long)capacity * 2, (long)length + count);
                if (grown > int.MaxValue)
                {
                    failed = true;
                    return;
                }
                var bigger = (byte*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Malloc(grown, 1, Unity.Collections.Allocator.Persistent);
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(bigger, data, length);
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free(data, Unity.Collections.Allocator.Persistent);
                data = bigger;
                capacity = (int)grown;
            }
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(data + length, source, count);
            length += count;
        }

        public void Free()
        {
            if (data != null)
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free(data, Unity.Collections.Allocator.Persistent);
            data = null;
            length = capacity = 0;
        }
    }

    static class NativeDeflate
    {
        delegate int Feeder(IntPtr buffer, int length, IntPtr state);

        [DllImport("MonoPosixHelper")]
        static extern IntPtr CreateZStream(int compress, byte gzip, IntPtr feeder, IntPtr state);

        [DllImport("MonoPosixHelper")]
        static extern int CloseZStream(IntPtr stream);

        [DllImport("MonoPosixHelper")]
        static extern int WriteZStream(IntPtr stream, IntPtr buffer, int length);

        static readonly Feeder FeederDelegate = Feed;
        static IntPtr _feeder;
        static volatile int _state;

        public static bool available => _state >= 0;

        [AOT.MonoPInvokeCallback(typeof(Feeder))]
        static unsafe int Feed(IntPtr buffer, int length, IntPtr state)
        {
            try
            {
                var target = GCHandle.FromIntPtr(state).Target;
                if (target is NativeSink sink)
                    sink.Write((byte*)buffer, length);
                else
                    ((RecordingBuffer)target).WriteBytes((byte*)buffer, length);
                return length;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static unsafe bool TryCompress(ReadOnlySpan<byte> payload, IntPtr output,
            List<float[,]> heights = null, ReadOnlySpan<byte> holes = default)
        {
            if (_state < 0)
                return false;
            try
            {
                if (_feeder == IntPtr.Zero)
                    _feeder = Marshal.GetFunctionPointerForDelegate(FeederDelegate);
                var stream = CreateZStream(1, 0, _feeder, output);
                if (stream == IntPtr.Zero)
                {
                    _state = -1;
                    return false;
                }
                bool wrote = WriteInput(stream, payload);
                if (wrote && heights != null)
                {
                    foreach (var rows in heights)
                    {
                        fixed (float* samples = &rows[0, 0])
                            wrote = WriteInput(stream, new ReadOnlySpan<byte>(samples, rows.Length * sizeof(float)));
                        if (!wrote)
                            break;
                    }
                }
                wrote = wrote && WriteInput(stream, holes);
                int closed = CloseZStream(stream);
                if (!wrote || closed < 0)
                    return false;
                _state = 1;
                return true;
            }
            catch (Exception)
            {
                _state = -1;
                return false;
            }
        }

        static unsafe bool WriteInput(IntPtr stream, ReadOnlySpan<byte> input)
        {
            if (input.IsEmpty)
                return true;
            fixed (byte* bytes = input)
                return WriteZStream(stream, (IntPtr)bytes, input.Length) >= 0;
        }
    }
#endif
}
