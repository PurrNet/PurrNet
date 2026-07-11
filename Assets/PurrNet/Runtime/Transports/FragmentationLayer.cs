using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Pooling;

namespace PurrNet.Transports
{
    /// <summary>
    /// Allocation-free-on-the-hot-path message fragmentation and bounded reassembly.
    /// Fragment loss is never recovered here: an incomplete message expires as a unit.
    ///
    /// Header format:
    ///   Unfragmented: [1 byte: 0x00] [payload]
    ///   Sequenced:    [1 byte: 0x02] [4 bytes: messageId] [payload]
    ///   Fragmented:   [1 byte: 0x01] [4 bytes: messageId] [4 bytes: totalLength]
    ///                 [2 bytes: fragmentStride] [1 byte: fragmentIndex]
    ///                 [1 byte: totalFragments] [payload]
    /// </summary>
    public sealed class FragmentationLayer : IDisposable
    {
        public delegate void FragmentCallback<TState>(ByteData fragment, TState state);

        /// <summary>Header overhead for unfragmented messages.</summary>
        public const int UNFRAGMENTED_OVERHEAD = 1;

        /// <summary>Header overhead for sequenced, unfragmented messages.</summary>
        public const int SEQUENCED_OVERHEAD = 5;

        /// <summary>Header overhead per fragmented message packet.</summary>
        public const int FRAGMENT_OVERHEAD = 13;

        public const int MAX_FRAGMENTS = byte.MaxValue;
        public const int MAX_MESSAGE_SIZE = 1024 * 1024;

        // Reassembly is exposed to the network and must be bounded independently of expiry.
        const int MAX_PENDING_MESSAGES = 128;
        const int MAX_PENDING_MESSAGES_PER_SENDER = 16;
        const int MAX_PENDING_BYTES = 8 * 1024 * 1024;
        const int MAX_PENDING_BYTES_PER_SENDER = 2 * 1024 * 1024;

        const byte FLAG_UNFRAGMENTED = 0;
        const byte FLAG_FRAGMENTED = 1;
        const byte FLAG_SEQUENCED = 2;

        static readonly FragmentCallback<Action<ByteData>> _invokeAction = InvokeAction;

        uint _nextMessageId;
        int _pendingBytes;
        int _nextCleanupAt;
        bool _cleanupScheduled;

        readonly Dictionary<ReassemblyKey, ReassemblyEntry> _pending = new(MAX_PENDING_MESSAGES);
        readonly Dictionary<int, SenderBudget> _senderBudgets = new(MAX_PENDING_MESSAGES);
        readonly Dictionary<StreamKey, uint> _latestSequencedMessages = new(MAX_PENDING_MESSAGES);
        readonly List<ReassemblyKey> _removeBuffer = new(MAX_PENDING_MESSAGES);
        readonly List<StreamKey> _streamRemoveBuffer = new(MAX_PENDING_MESSAGES);

        DisposableArray<byte> _sendBuffer;
        DisposableArray<byte> _completedBuffer;

        readonly struct ReassemblyKey : IEquatable<ReassemblyKey>
        {
            public readonly int senderId;
            public readonly byte streamId;
            public readonly uint messageId;

            public ReassemblyKey(int senderId, byte streamId, uint messageId)
            {
                this.senderId = senderId;
                this.streamId = streamId;
                this.messageId = messageId;
            }

            public bool Equals(ReassemblyKey other) => senderId == other.senderId &&
                                                        streamId == other.streamId &&
                                                        messageId == other.messageId;

            public override bool Equals(object obj) => obj is ReassemblyKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = senderId;
                    hash = (hash * 397) ^ streamId;
                    return (hash * 397) ^ (int)messageId;
                }
            }
        }

        readonly struct StreamKey : IEquatable<StreamKey>
        {
            public readonly int senderId;
            public readonly byte streamId;

            public StreamKey(int senderId, byte streamId)
            {
                this.senderId = senderId;
                this.streamId = streamId;
            }

            public bool Equals(StreamKey other) => senderId == other.senderId && streamId == other.streamId;
            public override bool Equals(object obj) => obj is StreamKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (senderId * 397) ^ streamId;
                }
            }
        }

        struct SenderBudget
        {
            public int messageCount;
            public int byteCount;
        }

        struct ReassemblyEntry
        {
            public byte totalFragments;
            public byte receivedCount;
            public ushort fragmentStride;
            public int totalLength;
            public int createdAtTick;
            public DisposableArray<byte> buffer;

            // 255 flags without allocating a second array for every pending message.
            ulong _received0;
            ulong _received1;
            ulong _received2;
            ulong _received3;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryMarkReceived(byte index)
            {
                int word = index >> 6;
                ulong bit = 1UL << (index & 63);

                switch (word)
                {
                    case 0:
                        if ((_received0 & bit) != 0) return false;
                        _received0 |= bit;
                        return true;
                    case 1:
                        if ((_received1 & bit) != 0) return false;
                        _received1 |= bit;
                        return true;
                    case 2:
                        if ((_received2 & bit) != 0) return false;
                        _received2 |= bit;
                        return true;
                    default:
                        if ((_received3 & bit) != 0) return false;
                        _received3 |= bit;
                        return true;
                }
            }
        }

        internal int pendingCount => _pending.Count;
        internal int pendingBytes => _pendingBytes;

        /// <summary>Returns the largest message accepted for the supplied packet size.</summary>
        public static int GetMaxMessageSize(int mtu, int reservedPrefix = 0)
        {
            int maxPayload = mtu - reservedPrefix - FRAGMENT_OVERHEAD;
            if (maxPayload <= 0)
                return 0;

            maxPayload = Math.Min(maxPayload, ushort.MaxValue);
            long result = (long)maxPayload * MAX_FRAGMENTS;
            return (int)Math.Min(result, MAX_MESSAGE_SIZE);
        }

        /// <summary>Returns the largest sequenced message accepted for the supplied packet size.</summary>
        public static int GetMaxSequencedMessageSize(int mtu, int reservedPrefix = 0)
        {
            int singlePacketPayload = Math.Max(0, mtu - reservedPrefix - SEQUENCED_OVERHEAD);
            return Math.Max(singlePacketPayload, GetMaxMessageSize(mtu, reservedPrefix));
        }

        /// <summary>
        /// Sends a message with no prefix reserved for the caller.
        /// </summary>
        public void Send(ByteData data, int mtu, Action<ByteData> sendFragment)
        {
            if (sendFragment == null)
                throw new ArgumentNullException(nameof(sendFragment));

            Send(data, mtu, 0, sendFragment, _invokeAction);
        }

        /// <summary>
        /// Sends a message on a sequenced stream. Even a single-packet message carries a message id
        /// so it can invalidate older incomplete fragmented messages on the same stream.
        /// </summary>
        public void SendSequenced(ByteData data, int mtu, Action<ByteData> sendFragment)
        {
            if (sendFragment == null)
                throw new ArgumentNullException(nameof(sendFragment));

            Send(data, mtu, 0, sendFragment, _invokeAction, true);
        }

        /// <summary>
        /// Splits a message into packets no larger than <paramref name="mtu"/>. The caller may
        /// reserve bytes at the front of every packet for its own framing and must overwrite all
        /// reserved bytes in the callback. Buffers passed to the callback are reused and are only
        /// valid for the duration of the callback.
        /// </summary>
        public void Send<TState>(ByteData data, int mtu, int reservedPrefix, TState state,
            FragmentCallback<TState> sendFragment)
        {
            Send(data, mtu, reservedPrefix, state, sendFragment, false);
        }

        /// <summary>
        /// Sends on a sequenced stream while reserving a caller-owned prefix. Single-packet and
        /// fragmented messages share the same monotonically increasing message-id space.
        /// </summary>
        public void SendSequenced<TState>(ByteData data, int mtu, int reservedPrefix, TState state,
            FragmentCallback<TState> sendFragment)
        {
            Send(data, mtu, reservedPrefix, state, sendFragment, true);
        }

        void Send<TState>(ByteData data, int mtu, int reservedPrefix, TState state,
            FragmentCallback<TState> sendFragment, bool sequenced)
        {
            if (sendFragment == null)
                throw new ArgumentNullException(nameof(sendFragment));
            if (reservedPrefix < 0)
                throw new ArgumentOutOfRangeException(nameof(reservedPrefix));

            int singlePacketOverhead = sequenced ? SEQUENCED_OVERHEAD : UNFRAGMENTED_OVERHEAD;
            if (data.length + reservedPrefix + singlePacketOverhead <= mtu)
            {
                int packetLength = reservedPrefix + singlePacketOverhead + data.length;
                EnsureBuffer(ref _sendBuffer, packetLength);
                _sendBuffer.array[reservedPrefix] = sequenced ? FLAG_SEQUENCED : FLAG_UNFRAGMENTED;
                if (sequenced)
                    WriteUInt32(_sendBuffer.array, reservedPrefix + 1, NextMessageId());
                Buffer.BlockCopy(data.data, data.offset, _sendBuffer.array,
                    reservedPrefix + singlePacketOverhead, data.length);
                sendFragment(new ByteData(_sendBuffer.array, 0, packetLength), state);
                return;
            }

            int availablePayload = mtu - reservedPrefix - FRAGMENT_OVERHEAD;
            if (availablePayload <= 0)
                throw new ArgumentException(
                    $"MTU {mtu} is too small for {reservedPrefix} reserved bytes and the " +
                    $"{FRAGMENT_OVERHEAD}-byte fragmentation header.", nameof(mtu));

            int fragmentStride = Math.Min(availablePayload, ushort.MaxValue);
            int totalFragments = (data.length + fragmentStride - 1) / fragmentStride;

            if (data.length > MAX_MESSAGE_SIZE || totalFragments > MAX_FRAGMENTS)
                throw new ArgumentException(
                    $"Data ({data.length} bytes) exceeds max fragmentable size for MTU {mtu}. " +
                    $"Max: {GetMaxMessageSize(mtu, reservedPrefix)} bytes.", nameof(data));

            uint messageId = NextMessageId();

            for (int i = 0; i < totalFragments; i++)
            {
                int payloadOffset = i * fragmentStride;
                int payloadLength = Math.Min(fragmentStride, data.length - payloadOffset);
                int packetLength = reservedPrefix + FRAGMENT_OVERHEAD + payloadLength;

                EnsureBuffer(ref _sendBuffer, packetLength);
                byte[] packet = _sendBuffer.array;
                int header = reservedPrefix;
                packet[header] = FLAG_FRAGMENTED;
                WriteUInt32(packet, header + 1, messageId);
                WriteInt32(packet, header + 5, data.length);
                WriteUInt16(packet, header + 9, (ushort)fragmentStride);
                packet[header + 11] = (byte)i;
                packet[header + 12] = (byte)totalFragments;

                Buffer.BlockCopy(data.data, data.offset + payloadOffset, packet,
                    reservedPrefix + FRAGMENT_OVERHEAD, payloadLength);
                sendFragment(new ByteData(packet, 0, packetLength), state);
            }
        }

        /// <summary>Receives on a single unordered stream.</summary>
        public bool Receive(ByteData data, out ByteData assembled)
        {
            return Receive(0, 0, false, data, out assembled);
        }

        /// <summary>
        /// Processes a packet for a sender and logical stream. Sequenced streams discard older
        /// incomplete messages as soon as any newer message is observed. Reassembled
        /// data remains valid until the next completed reassembly, <see cref="Reset"/>, or disposal.
        /// </summary>
        public bool Receive(int senderId, byte streamId, bool sequenced, ByteData data, out ByteData assembled)
        {
            assembled = default;

            if (data.length < UNFRAGMENTED_OVERHEAD)
                return false;

            int header = data.offset;
            byte flag = data.data[header];

            if (flag == FLAG_UNFRAGMENTED)
            {
                assembled = new ByteData(data.data, header + UNFRAGMENTED_OVERHEAD,
                    data.length - UNFRAGMENTED_OVERHEAD);
                return true;
            }

            if (flag == FLAG_SEQUENCED)
            {
                if (!sequenced || data.length < SEQUENCED_OVERHEAD)
                    return false;

                uint completedMessageId = ReadUInt32(data.data, header + 1);
                if (!AcceptCompletedSequenced(senderId, streamId, completedMessageId))
                    return false;

                assembled = new ByteData(data.data, header + SEQUENCED_OVERHEAD,
                    data.length - SEQUENCED_OVERHEAD);
                return true;
            }

            if (flag != FLAG_FRAGMENTED || data.length < FRAGMENT_OVERHEAD)
                return false;

            uint messageId = ReadUInt32(data.data, header + 1);
            int totalLength = ReadInt32(data.data, header + 5);
            ushort fragmentStride = ReadUInt16(data.data, header + 9);
            byte fragmentIndex = data.data[header + 11];
            byte totalFragments = data.data[header + 12];
            int payloadLength = data.length - FRAGMENT_OVERHEAD;

            if (!ValidateFragment(totalLength, fragmentStride, fragmentIndex, totalFragments, payloadLength))
                return false;

            var key = new ReassemblyKey(senderId, streamId, messageId);
            bool hasEntry = _pending.TryGetValue(key, out var entry);

            if (sequenced && !AcceptSequenced(key, hasEntry))
                return false;

            if (!hasEntry)
            {
                if (!TryReserve(senderId, totalLength))
                    return false;

                entry = new ReassemblyEntry
                {
                    totalFragments = totalFragments,
                    fragmentStride = fragmentStride,
                    totalLength = totalLength,
                    createdAtTick = Environment.TickCount,
                    // Every range is validated and copied exactly once before completion.
                    buffer = DisposableArray<byte>.CreateUninitialized(totalLength)
                };
            }
            else if (entry.totalFragments != totalFragments ||
                     entry.fragmentStride != fragmentStride ||
                     entry.totalLength != totalLength)
            {
                return false;
            }

            if (!entry.TryMarkReceived(fragmentIndex))
                return false;

            int destinationOffset = fragmentIndex * fragmentStride;
            Buffer.BlockCopy(data.data, header + FRAGMENT_OVERHEAD, entry.buffer.array,
                destinationOffset, payloadLength);
            entry.receivedCount++;

            if (entry.receivedCount < entry.totalFragments)
            {
                _pending[key] = entry;
                return false;
            }

            _completedBuffer.Dispose();
            _completedBuffer = entry.buffer;
            entry.buffer = default;
            _pending.Remove(key);
            Release(senderId, totalLength);

            assembled = new ByteData(_completedBuffer.array, 0, totalLength);
            return true;
        }

        /// <summary>Runs stale cleanup at most once per interval.</summary>
        public void CleanupStaleIfDue(int maxAgeMs, int intervalMs)
        {
            if (_pending.Count == 0)
                return;

            int now = Environment.TickCount;
            if (_cleanupScheduled && unchecked(now - _nextCleanupAt) < 0)
                return;

            _nextCleanupAt = unchecked(now + Math.Max(1, intervalMs));
            _cleanupScheduled = true;
            CleanupStale(maxAgeMs, now);
        }

        /// <summary>Removes incomplete messages at least <paramref name="maxAgeMs"/> old.</summary>
        public void CleanupStale(int maxAgeMs)
        {
            CleanupStale(maxAgeMs, Environment.TickCount);
        }

        public void RemoveSender(int senderId)
        {
            _removeBuffer.Clear();
            foreach (var pair in _pending)
            {
                if (pair.Key.senderId == senderId)
                    _removeBuffer.Add(pair.Key);
            }

            RemoveBufferedEntries();

            _streamRemoveBuffer.Clear();
            foreach (var pair in _latestSequencedMessages)
            {
                if (pair.Key.senderId == senderId)
                    _streamRemoveBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _streamRemoveBuffer.Count; i++)
                _latestSequencedMessages.Remove(_streamRemoveBuffer[i]);
        }

        public void Reset()
        {
            foreach (var pair in _pending)
            {
                var entry = pair.Value;
                entry.buffer.Dispose();
            }

            _pending.Clear();
            _senderBudgets.Clear();
            _latestSequencedMessages.Clear();
            _removeBuffer.Clear();
            _streamRemoveBuffer.Clear();
            _pendingBytes = 0;
            _nextCleanupAt = 0;
            _cleanupScheduled = false;

            _completedBuffer.Dispose();
            _completedBuffer = default;
            _sendBuffer.Dispose();
            _sendBuffer = default;
        }

        public void Dispose() => Reset();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        uint NextMessageId()
        {
            uint messageId = _nextMessageId;
            _nextMessageId = unchecked(messageId + 1);
            return messageId;
        }

        bool AcceptSequenced(ReassemblyKey key, bool hasEntry)
        {
            var stream = new StreamKey(key.senderId, key.streamId);
            if (!_latestSequencedMessages.TryGetValue(stream, out uint latest))
            {
                _latestSequencedMessages.Add(stream, key.messageId);
                return true;
            }

            int relative = unchecked((int)(key.messageId - latest));
            if (relative < 0)
                return false;

            if (relative == 0)
                return hasEntry;

            DiscardStream(stream);
            _latestSequencedMessages[stream] = key.messageId;
            return true;
        }

        bool AcceptCompletedSequenced(int senderId, byte streamId, uint messageId)
        {
            var stream = new StreamKey(senderId, streamId);
            if (!_latestSequencedMessages.TryGetValue(stream, out uint latest))
            {
                _latestSequencedMessages.Add(stream, messageId);
                return true;
            }

            int relative = unchecked((int)(messageId - latest));
            if (relative <= 0)
                return false;

            DiscardStream(stream);
            _latestSequencedMessages[stream] = messageId;
            return true;
        }

        void DiscardStream(StreamKey stream)
        {
            _removeBuffer.Clear();
            foreach (var pair in _pending)
            {
                if (pair.Key.senderId == stream.senderId && pair.Key.streamId == stream.streamId)
                    _removeBuffer.Add(pair.Key);
            }

            RemoveBufferedEntries();
        }

        bool TryReserve(int senderId, int byteCount)
        {
            if (_pending.Count >= MAX_PENDING_MESSAGES || byteCount > MAX_PENDING_BYTES - _pendingBytes)
                return false;

            _senderBudgets.TryGetValue(senderId, out var budget);
            if (budget.messageCount >= MAX_PENDING_MESSAGES_PER_SENDER ||
                byteCount > MAX_PENDING_BYTES_PER_SENDER - budget.byteCount)
                return false;

            budget.messageCount++;
            budget.byteCount += byteCount;
            _senderBudgets[senderId] = budget;
            _pendingBytes += byteCount;
            return true;
        }

        void Release(int senderId, int byteCount)
        {
            _pendingBytes -= byteCount;
            if (!_senderBudgets.TryGetValue(senderId, out var budget))
                return;

            budget.messageCount--;
            budget.byteCount -= byteCount;
            if (budget.messageCount <= 0)
                _senderBudgets.Remove(senderId);
            else
                _senderBudgets[senderId] = budget;
        }

        void CleanupStale(int maxAgeMs, int now)
        {
            _removeBuffer.Clear();
            foreach (var pair in _pending)
            {
                int elapsed = unchecked(now - pair.Value.createdAtTick);
                if (elapsed < 0 || elapsed >= maxAgeMs)
                    _removeBuffer.Add(pair.Key);
            }

            RemoveBufferedEntries();
        }

        void RemoveBufferedEntries()
        {
            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                var key = _removeBuffer[i];
                if (!_pending.TryGetValue(key, out var entry))
                    continue;

                entry.buffer.Dispose();
                _pending.Remove(key);
                Release(key.senderId, entry.totalLength);
            }
        }

        static bool ValidateFragment(int totalLength, ushort stride, byte index, byte count, int payloadLength)
        {
            if (totalLength <= 0 || totalLength > MAX_MESSAGE_SIZE || stride == 0 ||
                count == 0 || index >= count)
                return false;

            int expectedCount = (totalLength + stride - 1) / stride;
            if (expectedCount != count)
                return false;

            int offset = index * stride;
            int expectedPayload = Math.Min(stride, totalLength - offset);
            return expectedPayload > 0 && payloadLength == expectedPayload;
        }

        static void EnsureBuffer(ref DisposableArray<byte> buffer, int size)
        {
            if (!buffer.isDisposed && buffer.Count >= size)
                return;

            buffer.Dispose();
            buffer = DisposableArray<byte>.CreateUninitialized(size);
        }

        static void InvokeAction(ByteData data, Action<ByteData> callback) => callback(data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint ReadUInt32(byte[] data, int offset)
        {
            return data[offset] |
                   ((uint)data[offset + 1] << 8) |
                   ((uint)data[offset + 2] << 16) |
                   ((uint)data[offset + 3] << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteInt32(byte[] data, int offset, int value) => WriteUInt32(data, offset, (uint)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ReadInt32(byte[] data, int offset) => (int)ReadUInt32(data, offset);
    }
}
