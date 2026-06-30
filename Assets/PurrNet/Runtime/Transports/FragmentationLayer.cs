using System;
using System.Buffers;
using System.Collections.Generic;
using PurrNet.Pooling;

namespace PurrNet.Transports
{
    /// <summary>
    /// Modular, GC-free fragmentation layer for transports that don't handle fragmentation natively.
    /// Splits outgoing messages into MTU-sized fragments and reassembles incoming fragments.
    ///
    /// Hot path (unfragmented messages) reuses pooled byte buffers after first growth.
    /// Uses pooled byte buffers for fragment reassembly.
    ///
    /// Header format:
    ///   Unfragmented: [1 byte: 0x00] [payload]
    ///   Fragmented:   [1 byte: 0x01] [2 bytes: messageId] [1 byte: fragmentIndex] [1 byte: totalFragments] [payload]
    /// </summary>
    public class FragmentationLayer : IDisposable
    {
        /// <summary>Header overhead for unfragmented messages (1 byte).</summary>
        public const int UNFRAGMENTED_OVERHEAD = 1;

        /// <summary>Header overhead per fragment (5 bytes).</summary>
        public const int FRAGMENT_OVERHEAD = 5;

        /// <summary>Maximum number of fragments per message.</summary>
        public const int MAX_FRAGMENTS = 255;

        const byte FLAG_UNFRAGMENTED = 0;
        const byte FLAG_FRAGMENTED = 1;

        ushort _nextMessageId;
        readonly Dictionary<ushort, ReassemblyEntry> _pending = new();
        DisposableArray<byte> _sendBuffer;
        DisposableArray<byte> _assemblyBuffer;
        readonly List<ushort> _removeBuffer = new();

        struct ReassemblyEntry
        {
            public byte totalFragments;
            public byte receivedCount;
            public int totalLength;
            public DisposableArray<byte>[] fragments;
            public int createdAtTick;
        }

        /// <summary>
        /// Returns the maximum message size that can be sent with the given MTU.
        /// </summary>
        public static int GetMaxMessageSize(int mtu)
        {
            return (mtu - FRAGMENT_OVERHEAD) * MAX_FRAGMENTS;
        }

        /// <summary>
        /// Sends data, fragmenting if it exceeds the MTU.
        /// Calls <paramref name="sendFragment"/> for each fragment (or once for small messages).
        /// The ByteData passed to the callback is only valid during the callback.
        /// Cache the delegate to avoid GC allocations.
        /// </summary>
        public void Send(ByteData data, int mtu, Action<ByteData> sendFragment)
        {
            if (data.length + UNFRAGMENTED_OVERHEAD <= mtu)
            {
                int packetLength = UNFRAGMENTED_OVERHEAD + data.length;
                EnsureBuffer(ref _sendBuffer, packetLength);
                _sendBuffer.array[0] = FLAG_UNFRAGMENTED;
                Buffer.BlockCopy(data.data, data.offset, _sendBuffer.array, UNFRAGMENTED_OVERHEAD, data.length);
                sendFragment(new ByteData(_sendBuffer.array, 0, packetLength));
                return;
            }

            int maxPayload = mtu - FRAGMENT_OVERHEAD;

            if (maxPayload <= 0)
                throw new ArgumentException(
                    $"MTU {mtu} is too small for the fragment header ({FRAGMENT_OVERHEAD} bytes).");

            int totalFragments = (data.length + maxPayload - 1) / maxPayload;

            if (totalFragments > MAX_FRAGMENTS)
                throw new ArgumentException(
                    $"Data ({data.length} bytes) exceeds max fragmentable size for MTU {mtu}. " +
                    $"Max: {GetMaxMessageSize(mtu)} bytes.");

            var msgId = _nextMessageId++;

            for (int i = 0; i < totalFragments; i++)
            {
                int payloadOffset = i * maxPayload;
                int payloadLen = Math.Min(maxPayload, data.length - payloadOffset);
                int packetLength = FRAGMENT_OVERHEAD + payloadLen;

                EnsureBuffer(ref _sendBuffer, packetLength);
                _sendBuffer.array[0] = FLAG_FRAGMENTED;
                _sendBuffer.array[1] = (byte)(msgId & 0xFF);
                _sendBuffer.array[2] = (byte)(msgId >> 8);
                _sendBuffer.array[3] = (byte)i;
                _sendBuffer.array[4] = (byte)totalFragments;
                Buffer.BlockCopy(data.data, data.offset + payloadOffset, _sendBuffer.array, FRAGMENT_OVERHEAD, payloadLen);
                sendFragment(new ByteData(_sendBuffer.array, 0, packetLength));
            }
        }

        static void EnsureBuffer(ref DisposableArray<byte> buffer, int size)
        {
            if (!buffer.isDisposed && buffer.Count >= size)
                return;

            buffer.Dispose();
            buffer = DisposableArray<byte>.Create(size);
        }

        /// <summary>
        /// Processes a received fragment. Returns true when a complete message is ready.
        /// For unfragmented messages, <paramref name="assembled"/> is a zero-copy slice of the input.
        /// For reassembled messages, <paramref name="assembled"/> references an internal buffer
        /// valid until the next successful reassembly, <see cref="Reset"/>, or <see cref="Dispose"/>.
        /// </summary>
        public bool Receive(ByteData data, out ByteData assembled)
        {
            if (data.length < UNFRAGMENTED_OVERHEAD)
            {
                assembled = default;
                return false;
            }

            byte flag = data.data[data.offset];

            if (flag == FLAG_UNFRAGMENTED)
            {
                assembled = new ByteData(data.data,
                    data.offset + UNFRAGMENTED_OVERHEAD,
                    data.length - UNFRAGMENTED_OVERHEAD);
                return true;
            }

            if (data.length < FRAGMENT_OVERHEAD)
            {
                assembled = default;
                return false;
            }

            ushort msgId = (ushort)(data.data[data.offset + 1] | (data.data[data.offset + 2] << 8));
            byte fragIdx = data.data[data.offset + 3];
            byte totalFrags = data.data[data.offset + 4];

            if (totalFrags == 0 || fragIdx >= totalFrags)
            {
                assembled = default;
                return false;
            }

            if (!_pending.TryGetValue(msgId, out var entry))
            {
                var fragments = ArrayPool<DisposableArray<byte>>.Shared.Rent(totalFrags);
                Array.Clear(fragments, 0, totalFrags);

                entry = new ReassemblyEntry
                {
                    totalFragments = totalFrags,
                    receivedCount = 0,
                    totalLength = 0,
                    fragments = fragments,
                    createdAtTick = Environment.TickCount
                };
            }
            else if (entry.totalFragments != totalFrags || !entry.fragments[fragIdx].isDisposed)
            {
                assembled = default;
                return false;
            }

            int payloadLength = data.length - FRAGMENT_OVERHEAD;
            var payload = DisposableArray<byte>.Create(payloadLength);
            Buffer.BlockCopy(data.data, data.offset + FRAGMENT_OVERHEAD, payload.array, 0, payloadLength);

            entry.fragments[fragIdx] = payload;
            entry.receivedCount++;
            entry.totalLength += payloadLength;
            _pending[msgId] = entry;

            if (entry.receivedCount < entry.totalFragments)
            {
                assembled = default;
                return false;
            }

            EnsureBuffer(ref _assemblyBuffer, entry.totalLength);

            int offset = 0;
            for (int i = 0; i < entry.totalFragments; i++)
            {
                var fragmentPayload = entry.fragments[i];
                Buffer.BlockCopy(fragmentPayload.array, 0, _assemblyBuffer.array, offset, fragmentPayload.Count);
                offset += fragmentPayload.Count;
                fragmentPayload.Dispose();
                entry.fragments[i] = default;
            }

            _pending.Remove(msgId);
            ArrayPool<DisposableArray<byte>>.Shared.Return(entry.fragments, true);
            assembled = new ByteData(_assemblyBuffer.array, 0, entry.totalLength);
            return true;
        }

        /// <summary>
        /// Removes reassembly entries older than <paramref name="maxAgeMs"/> milliseconds.
        /// Call periodically to prevent memory buildup from lost fragments.
        /// </summary>
        public void CleanupStale(int maxAgeMs)
        {
            int now = Environment.TickCount;
            _removeBuffer.Clear();

            foreach (var kvp in _pending)
            {
                int elapsed = unchecked(now - kvp.Value.createdAtTick);
                if (elapsed < 0 || elapsed > maxAgeMs)
                    _removeBuffer.Add(kvp.Key);
            }

            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                if (_pending.TryGetValue(_removeBuffer[i], out var entry))
                {
                    FreeEntry(ref entry);
                    _pending.Remove(_removeBuffer[i]);
                }
            }
        }

        /// <summary>
        /// Resets all state and returns pooled resources.
        /// </summary>
        public void Reset()
        {
            foreach (var kvp in _pending)
            {
                var entry = kvp.Value;
                FreeEntry(ref entry);
            }
            _pending.Clear();

            _assemblyBuffer.Dispose();
            _assemblyBuffer = default;
            _sendBuffer.Dispose();
            _sendBuffer = default;
        }

        public void Dispose()
        {
            Reset(); 
        }

        void FreeEntry(ref ReassemblyEntry entry)
        {
            if (entry.fragments == null)
                return;

            for (int i = 0; i < entry.totalFragments; i++)
            {
                if (!entry.fragments[i].isDisposed)
                {
                    entry.fragments[i].Dispose();
                    entry.fragments[i] = default;
                }
            }

            ArrayPool<DisposableArray<byte>>.Shared.Return(entry.fragments, true);
            entry.fragments = null;
            entry.totalLength = 0;
        }
    }
}
