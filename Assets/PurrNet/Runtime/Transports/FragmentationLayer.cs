using System;
using System.Collections.Generic;
using PurrNet.Packing;

namespace PurrNet.Transports
{
    /// <summary>
    /// Modular, GC-free fragmentation layer for transports that don't handle fragmentation natively.
    /// Splits outgoing messages into MTU-sized fragments and reassembles incoming fragments.
    ///
    /// Hot path (unfragmented messages) produces zero GC allocations.
    /// Uses BitPacker pooling for all intermediate buffers.
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
        BitPacker _assemblyPacker;
        readonly List<ushort> _removeBuffer = new();

        struct ReassemblyEntry
        {
            public byte totalFragments;
            public byte receivedCount;
            public BitPacker[] fragments;
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
                var packer = BitPackerPool.Get();
                packer.WriteBits(FLAG_UNFRAGMENTED, 8);
                packer.WriteBytes(data);
                sendFragment(packer.ToByteData());
                BitPackerPool.Free(packer);
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

                var packer = BitPackerPool.Get();
                packer.WriteBits(FLAG_FRAGMENTED, 8);
                packer.WriteBits(msgId, 16);
                packer.WriteBits((ulong)i, 8);
                packer.WriteBits((ulong)totalFragments, 8);
                packer.WriteBytes(new ByteData(data.data, data.offset + payloadOffset, payloadLen));
                sendFragment(packer.ToByteData());
                BitPackerPool.Free(packer);
            }
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

            var reader = BitPackerPool.Get(data);
            byte flag = (byte)reader.ReadBits(8);

            if (flag == FLAG_UNFRAGMENTED)
            {
                BitPackerPool.Free(reader);
                assembled = new ByteData(data.data,
                    data.offset + UNFRAGMENTED_OVERHEAD,
                    data.length - UNFRAGMENTED_OVERHEAD);
                return true;
            }

            if (data.length < FRAGMENT_OVERHEAD)
            {
                BitPackerPool.Free(reader);
                assembled = default;
                return false;
            }

            ushort msgId = (ushort)reader.ReadBits(16);
            byte fragIdx = (byte)reader.ReadBits(8);
            byte totalFrags = (byte)reader.ReadBits(8);
            BitPackerPool.Free(reader);

            if (totalFrags == 0 || fragIdx >= totalFrags)
            {
                assembled = default;
                return false;
            }

            if (!_pending.TryGetValue(msgId, out var entry))
            {
                entry = new ReassemblyEntry
                {
                    totalFragments = totalFrags,
                    receivedCount = 0,
                    fragments = new BitPacker[totalFrags],
                    createdAtTick = Environment.TickCount
                };
            }
            else if (entry.totalFragments != totalFrags || entry.fragments[fragIdx] != null)
            {
                assembled = default;
                return false;
            }

            var fragPacker = BitPackerPool.Get();
            fragPacker.WriteBytes(new ByteData(data.data,
                data.offset + FRAGMENT_OVERHEAD,
                data.length - FRAGMENT_OVERHEAD));

            entry.fragments[fragIdx] = fragPacker;
            entry.receivedCount++;
            _pending[msgId] = entry;

            if (entry.receivedCount < entry.totalFragments)
            {
                assembled = default;
                return false;
            }

            if (_assemblyPacker != null)
                BitPackerPool.Free(_assemblyPacker);

            _assemblyPacker = BitPackerPool.Get();

            for (int i = 0; i < entry.totalFragments; i++)
            {
                _assemblyPacker.WriteBytes(entry.fragments[i].ToByteData());
                BitPackerPool.Free(entry.fragments[i]);
            }

            _pending.Remove(msgId);
            assembled = _assemblyPacker.ToByteData();
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

            if (_assemblyPacker != null)
            {
                BitPackerPool.Free(_assemblyPacker);
                _assemblyPacker = null;
            }
        }

        public void Dispose()
        {
            Reset();
        }

        void FreeEntry(ref ReassemblyEntry entry)
        {
            for (int i = 0; i < entry.totalFragments; i++)
            {
                if (entry.fragments[i] != null)
                {
                    BitPackerPool.Free(entry.fragments[i]);
                    entry.fragments[i] = null;
                }
            }
        }
    }
}
