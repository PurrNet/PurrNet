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
        Observers = 16
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
        }

        readonly Queue<PendingRecord> _queue = new Queue<PendingRecord>(64);
        long _rawBytes;
        volatile Exception _error;
        volatile bool _busy;
        long _produceTicks, _maxRecordTicks;
#if !PURRREC_WEB
        bool _completed;
        readonly Thread _thread;
        readonly FileStream _stream;
        long _bytesWritten, _compressTicks;
        readonly BufferStream _compressed = new BufferStream(new RecordingBuffer(ByteShelf.shared, 1 << 16));
        GCHandle _compressedHandle;
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
#else
        public static bool encodesOnMainThread => false;
#endif
        public long rawBytes => Interlocked.Read(ref _rawBytes);

#if PURRREC_WEB
        readonly int _web;
        readonly RecordingBuffer _header = new RecordingBuffer(16);

        public RecordingFile(string path, long maxLocalBytes)
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

        public RecordingFile(string path, long maxLocalBytes)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            var magic = Encoding.ASCII.GetBytes("PURRREC\0");
            _stream.Write(magic, 0, magic.Length);
            _stream.WriteByte(1);
            _stream.WriteByte(0);
            _bytesWritten = 10;
            _compressedHandle = GCHandle.Alloc(_compressed.buffer);
            _thread = new Thread(WriteLoop) { IsBackground = true, Name = "PurrDiction recording writer", Priority = System.Threading.ThreadPriority.BelowNormal };
            _thread.Start();
        }

        void WriteLoop()
        {
            UnityEngine.Profiling.Profiler.BeginThreadProfiling("PurrDiction", "Recording writer");
            try
            {
                var header = new RecordingBuffer(16);
                while (Take(out var record))
                {
                    WriteRecord(record, header);
                    bool idle;
                    lock (_queue)
                        idle = _queue.Count == 0;
                    if (idle)
                        _stream.Flush();
                }
                _stream.Flush();
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndThreadProfiling();
            }
        }

        bool Take(out PendingRecord record)
        {
            lock (_queue)
            {
                while (_queue.Count == 0)
                {
                    if (_completed)
                    {
                        record = default;
                        return false;
                    }
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

        void WriteRecord(in PendingRecord record, RecordingBuffer header)
        {
            if (_error != null)
            {
                record.producer?.Release();
                return;
            }
            _busy = true;
            try
            {
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
                    if (!NativeDeflate.TryCompress(payload, length, GCHandle.ToIntPtr(_compressedHandle)))
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
                record.producer?.Release();
                _busy = false;
            }
        }

        public void Dispose()
        {
#if PURRREC_WEB
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            while (_queue.Count > 0)
                WriteRecord(_queue.Dequeue(), _header);
            _produceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            if (_web != 0)
                WebRecording.Close(_web);
#else
            lock (_queue)
            {
                _completed = true;
                Monitor.PulseAll(_queue);
            }
            _thread.Join();
            if (_compressedHandle.IsAllocated)
                _compressedHandle.Free();
            _stream.Dispose();
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
                var output = (RecordingBuffer)GCHandle.FromIntPtr(state).Target;
                output.WriteBytes((byte*)buffer, length);
                return length;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static unsafe bool TryCompress(byte[] payload, int length, IntPtr output)
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
                int written = 0;
                if (length > 0)
                {
                    fixed (byte* p = payload)
                        written = WriteZStream(stream, (IntPtr)p, length);
                }
                int closed = CloseZStream(stream);
                if (written < 0 || closed < 0)
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
    }
#endif
}
