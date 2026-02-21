using System;
using System.Runtime.CompilerServices;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Collections;
using Unity.Profiling;

namespace PurrNet.Modules
{
    internal struct RPCBatchPacket : IPackedAuto
    {
        public const int HEADER = 6;

        public Size count;
        public BitData data;
    }

    internal struct PendingBatchedData
    {
        public BatchKey key;
        public UnionRPCHeader lastHeader;
        public Size lastDataLen;
        public int batchCount;
        public int cachedMTU;
        public BitPacker batchedData;
    }

    public sealed class RPCBatch : IDisposable
    {
        public const int MAX_HEADER_SIZE = RPCBatchPacket.HEADER + UnionRPCHeader.MAX_SIZE;

        static readonly ProfilerMarker _flushMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Flush");
        static readonly ProfilerMarker _flushChannelMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.FlushChannel");
        static readonly ProfilerMarker _queueSingleMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Queue");
        static readonly ProfilerMarker _queueMultiMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.QueueMulti");
        static readonly ProfilerMarker _batchReceivedMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.OnBatchReceived");

        private readonly PlayersManager _playersManager;
        private PendingBatchedData[] _batches = new PendingBatchedData[128];
        private NativeHashMap<BatchKey, int> _batchIndexMap;
        private int _batchCount = 0;

        public delegate void RPCReceivedDelegate(PlayerID sender, UnionRPCHeader header, BitData content, bool asServer);
        private readonly RPCReceivedDelegate _onRPCReceived;

        public RPCBatch(PlayersManager playersManager, RPCReceivedDelegate callback)
        {
            _playersManager = playersManager;
            _onRPCReceived = callback;
            _playersManager.Subscribe<RPCBatchPacket>(OnBatchReceived);
            _batchIndexMap = new NativeHashMap<BatchKey, int>(128, Allocator.Persistent);
        }

        public void Dispose()
        {
            _playersManager.Unsubscribe<RPCBatchPacket>(OnBatchReceived);
            _batchIndexMap.Dispose();
        }

        public void Flush()
        {
            using (_flushMarker.Auto())
            {
                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];
                    var data = new RPCBatchPacket
                    {
                        count = batch.batchCount,
                        data = new BitData(batch.batchedData)
                    };

                    _playersManager.Send(batch.key.playerId, data, batch.key.channel);
                    batch.batchedData.Dispose();
                }

                _batchCount = 0;
                _batchIndexMap.Clear();
            }
        }

        public void FlushChannel(Channel channel)
        {
            using (_flushChannelMarker.Auto())
            {
                int writeIdx = 0;

                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];

                    if (batch.key.channel == channel)
                    {
                        SendBatch(ref batch);
                        batch.batchedData.Dispose();
                        _batchIndexMap.Remove(batch.key);
                    }
                    else
                    {
                        // Keep this batch, shift it down if needed
                        if (writeIdx != i)
                        {
                            _batches[writeIdx] = _batches[i];
                            _batchIndexMap[batch.key] = writeIdx;
                        }
                        writeIdx++;
                    }
                }

                _batchCount = writeIdx;
            }
        }

        private void SendBatch(ref PendingBatchedData batch)
        {
            var data = new RPCBatchPacket
            {
                count = batch.batchCount,
                data = new BitData(batch.batchedData)
            };

            _playersManager.Send(batch.key.playerId, data, batch.key.channel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBatchIndex(BatchKey key)
        {
            if (_batchIndexMap.TryGetValue(key, out int idx))
                return idx;
            return CreateBatch(key);
        }

        private int CreateBatch(BatchKey key)
        {
            // Resize if needed
            if (_batchCount >= _batches.Length)
                Array.Resize(ref _batches, _batches.Length * 2);

            int c = _batchCount;
            _batches[c] = new PendingBatchedData
            {
                key = key,
                batchedData = BitPackerPool.Get(),
                cachedMTU = _playersManager.GetMTU(key.playerId, key.channel, key.playerId != PlayerID.Server)
            };
            _batchIndexMap[key] = c;
            _batchCount++;
            return c;
        }

        private unsafe void OnBatchReceived(PlayerID player, RPCBatchPacket data, bool asServer)
        {
            _batchReceivedMarker.Begin();

            UnionRPCHeader lastHeader = default;
            Size lastLen = default;

            var packer = data.data.packer;
            using (data.data.AutoScope())
            {
                for (var i = 0; i < data.count.value; ++i)
                {
                    NativeDeltaPacker<UnionRPCHeader>.ReadFunc(packer, lastHeader, ref lastHeader);
                    DeltaPackInteger.ReadIndex(packer, lastLen, ref lastLen);

                    int pos = packer.positionInBits;
                    int len = (int)lastLen.value;

                    var bitData = new BitData(packer, pos, len);
                    _onRPCReceived.Invoke(player, lastHeader, bitData, asServer);

                    packer.SetBitPosition(pos + len);
                }
            }

            _batchReceivedMarker.End();
        }

        public unsafe void Queue(DisposableList<PlayerID> targets, UnionRPCHeader header, BitData content, Channel channel)
        {
            _queueMultiMarker.Begin();

            var contentLen = content.bitLength;
            int contentByteLen = content.byteLength;
            bool hasContent = contentLen.value > 0;

            for (var i = targets.Count - 1; i >= 0; i--)
            {
                var batchIdx = GetBatchIndex(new BatchKey { playerId = targets[i], channel = channel });
                ref var batch = ref _batches[batchIdx];

                int before = batch.batchedData.positionInBits;

                NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, batch.lastHeader, header);
                DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);

                // do some MTU checks past 1 batch
                if (batch.batchCount > 0)
                {
                    int bytesAfterHeaderLen = batch.batchedData.positionInBytes + contentByteLen;
                    if (bytesAfterHeaderLen + 10 >= batch.cachedMTU)
                    {
                        // undo the last write
                        batch.batchedData.SetBitPosition(before);
                        SendBatch(ref batch);
                        batch.batchCount = 0;
                        batch.batchedData.ResetPositionAndMode(false);

                        // redo the last write
                        NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, default, header);
                        DeltaPackInteger.WriteIndex(batch.batchedData, default, contentLen);
                    }
                }

                ++batch.batchCount;
                batch.lastHeader = header;
                batch.lastDataLen = contentLen;

                if (hasContent)
                    batch.batchedData.WriteBitDataWithoutConsumingIt(content);
            }

            _queueMultiMarker.End();
        }

        public unsafe void Queue(PlayerID target, UnionRPCHeader header, BitData content, Channel channel)
        {
            _queueSingleMarker.Begin();

            var batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
            ref var batch = ref _batches[batchIdx];

            int before = batch.batchedData.positionInBits;
            var contentLen = content.bitLength;

            NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, batch.lastHeader, header);
            DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);

            // do some MTU checks past 1 batch
            if (batch.batchCount > 0)
            {
                int bytesAfterHeaderLen = batch.batchedData.positionInBytes + content.byteLength;
                if (bytesAfterHeaderLen + 10 >= batch.cachedMTU)
                {
                    // undo the last write
                    batch.batchedData.SetBitPosition(before);
                    SendBatch(ref batch);
                    batch.batchCount = 0;
                    batch.batchedData.ResetPositionAndMode(false);

                    // redo the last write
                    NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, default, header);
                    DeltaPackInteger.WriteIndex(batch.batchedData, default, contentLen);
                }
            }

            ++batch.batchCount;
            batch.lastHeader = header;
            batch.lastDataLen = contentLen;

            if (contentLen.value > 0)
                batch.batchedData.WriteBitDataWithoutConsumingIt(content);

            _queueSingleMarker.End();
        }

        public void Clear()
        {
            for (int i = 0; i < _batchCount; i++)
                _batches[i].batchedData.Dispose();

            _batchCount = 0;
            _batchIndexMap.Clear();
        }
    }
}
