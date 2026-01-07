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
        public Size count;
        public BitPacker data;
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
        static readonly ProfilerMarker _flushMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Flush");
        static readonly ProfilerMarker _flushChannelMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.FlushChannel");
        static readonly ProfilerMarker _getSingleBatchMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.GetBatchIndex");
        static readonly ProfilerMarker _queueSingleMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Queue");
        static readonly ProfilerMarker _batchWriteDeltasMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Queue.WriteDeltas");
        static readonly ProfilerMarker _batchReceivedMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.OnBatchReceived");
        static readonly ProfilerMarker _batchReceivedDeltasMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.OnBatchReceived.ReadDeltas");
        static readonly ProfilerMarker _batchWriteBitsMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.OnBatchReceived.WriteBits");

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
                        data = batch.batchedData
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
                bool removed = false;
                int writeIdx = 0;

                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];

                    if (batch.key.channel == channel)
                    {
                        SendBatch(ref batch);
                        batch.batchedData.Dispose();
                        removed = true;
                    }
                    else
                    {
                        // Keep this batch, shift it down if needed
                        if (writeIdx != i)
                            _batches[writeIdx] = _batches[i];
                        writeIdx++;
                    }
                }

                _batchCount = writeIdx;

                if (removed)
                {
                    _batchIndexMap.Clear();
                    for (int i = 0; i < _batchCount; i++)
                        _batchIndexMap[_batches[i].key] = i;
                }
            }
        }

        private void SendBatch(ref PendingBatchedData batch)
        {
            var data = new RPCBatchPacket
            {
                count = batch.batchCount,
                data = batch.batchedData
            };

            _playersManager.Send(batch.key.playerId, data, batch.key.channel);
        }

        private int GetBatchIndex(BatchKey key)
        {
            using (_getSingleBatchMarker.Auto())
            {
                if (_batchIndexMap.TryGetValue(key, out int idx))
                    return idx;

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
        }

        private void OnBatchReceived(PlayerID player, RPCBatchPacket data, bool asServer)
        {
            using (_batchReceivedMarker.Auto())
            {
                UnionRPCHeader lastHeader = default;
                Size lastLen = default;

                for (var i = 0; i < data.count.value; ++i)
                {
                    using (_batchReceivedDeltasMarker.Auto())
                    {
                        DeltaPacker<UnionRPCHeader>.ReadFunc(data.data, lastHeader, ref lastHeader);
                        DeltaPackInteger.ReadIndex(data.data, lastLen, ref lastLen);
                    }

                    int pos = data.data.positionInBits;
                    int len = (int)lastLen.value;

                    var bitData = new BitData(data.data, pos, len);
                    _onRPCReceived.Invoke(player, lastHeader, bitData, asServer);
                    data.data.AdvanceBits(len);
                }

                data.data.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Queue(DisposableList<PlayerID> targets, UnionRPCHeader header, BitData content, Channel channel)
        {
            for (var i = targets.Count - 1; i >= 0; i--)
                Queue(targets[i], header, content, channel);
        }

        public void Queue(PlayerID target, UnionRPCHeader header, BitData content, Channel channel)
        {
            using (_queueSingleMarker.Auto())
            {
                var batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
                ref var batch = ref _batches[batchIdx];

                int before = batch.batchedData.positionInBits;
                var contentLen = content.bitLength;

                using (_batchWriteDeltasMarker.Auto())
                {
                    DeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, batch.lastHeader, header);
                    DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);
                }

                int bytesAfterHeaderLen = batch.batchedData.positionInBytes + content.byteLength;

                // do some MTU checks past 1 batch
                if (batch.batchCount > 0 && bytesAfterHeaderLen + 10 >= batch.cachedMTU)
                {
                    // undo the last write
                    batch.batchedData.SetBitPosition(before);
                    SendBatch(ref batch);
                    batch.batchCount = 0;
                    batch.batchedData.ResetPositionAndMode(false);

                    // redo the last write
                    using (_batchWriteDeltasMarker.Auto())
                    {
                        DeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, default, header);
                        DeltaPackInteger.WriteIndex(batch.batchedData, default, contentLen);
                    }
                }

                ++batch.batchCount;
                batch.lastHeader = header;
                batch.lastDataLen = contentLen;

                using (_batchWriteBitsMarker.Auto())
                {
                    if (content.bitLength > 0)
                        batch.batchedData.WriteBitDataWithoutConsumingIt(content);
                }
            }
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
