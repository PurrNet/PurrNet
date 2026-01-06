using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    [RegisterNetworkType(typeof(RPCBatch<NetworkIdentityRPCHeader>.RPCBatchPacket))]
    [RegisterNetworkType(typeof(RPCBatch<NetworkModuleRPCHeader>.RPCBatchPacket))]
    [RegisterNetworkType(typeof(RPCBatch<StaticRPCHeader>.RPCBatchPacket))]
    public sealed class RPCBatch<HEADER> : IDisposable where HEADER : unmanaged, IEquatable<HEADER>
    {
        static readonly ProfilerMarker _flushMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Flush");
        static readonly ProfilerMarker _flushChannelMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.FlushChannel");
        static readonly ProfilerMarker _getSingleBatchMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.GetBatchIndex");
        static readonly ProfilerMarker _queueSingleMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Queue");

        struct PendingBatchedData
        {
            public BatchKey key;
            public HEADER lastHeader;
            public Size lastDataLen;
            public int batchCount;
            public int? cachedMTU;
            public BitPacker batchedData;
        }

        struct RPCBatchPacket : IPackedAuto
        {
            public Size count;
            public BitPacker data;
        }

        private readonly PlayersManager _playersManager;
        private readonly List<PendingBatchedData> _batches = new ();
        private readonly Dictionary<BatchKey, int> _batchIndexMap = new();

        public delegate void RPCReceivedDelegate(PlayerID sender, HEADER header, ByteData content, bool asServer);
        private readonly RPCReceivedDelegate _onRPCReceived;

        public RPCBatch(PlayersManager playersManager, RPCReceivedDelegate callback)
        {
            _playersManager = playersManager;
            _onRPCReceived = callback;
            _playersManager.Subscribe<RPCBatchPacket>(OnBatchReceived);
        }

        public void Dispose()
        {
            _playersManager.Unsubscribe<RPCBatchPacket>(OnBatchReceived);
        }

        public void Flush()
        {
            using (_flushMarker.Auto())
            {
                for (int i = 0; i < _batches.Count; i++)
                {
                    var batch = _batches[i];
                    var data = new RPCBatchPacket
                    {
                        count = batch.batchCount,
                        data = batch.batchedData
                    };

                    _playersManager.Send(batch.key.playerId, data, batch.key.channel);
                    batch.batchedData.Dispose();
                }

                _batches.Clear();
                _batchIndexMap.Clear();
            }
        }

        public void FlushChannel(Channel channel)
        {
            using (_flushChannelMarker.Auto())
            {
                bool removed = false;
                for (int i = _batches.Count - 1; i >= 0; i--)
                {
                    var batch = _batches[i];

                    if (batch.key.channel != channel)
                        continue;

                    SendBatch(batch);
                    batch.batchedData.Dispose();
                    _batches.RemoveAt(i);
                    removed = true;
                }

                if (removed)
                {
                    _batchIndexMap.Clear();
                    for (int i = 0; i < _batches.Count; i++)
                        _batchIndexMap[_batches[i].key] = i;
                }
            }
        }

        private void SendBatch(PendingBatchedData batch)
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

                int c = _batches.Count;
                _batches.Add(new PendingBatchedData { key = key, batchedData = BitPackerPool.Get() });
                _batchIndexMap[key] = c;
                return c;
            }
        }

        private void OnBatchReceived(PlayerID player, RPCBatchPacket data, bool asServer)
        {
            HEADER lastHeader = default;
            Size lastLen = default;

            using var tmp = BitPackerPool.Get();

            for (var i = 0; i < data.count.value; ++i)
            {
                DeltaPacker<HEADER>.Read(data.data, lastHeader, ref lastHeader);
                DeltaPackInteger.ReadIndex(data.data, lastLen, ref lastLen);
                int pos = data.data.positionInBits;

                tmp.WriteBytes(data.data, lastLen);
                _onRPCReceived.Invoke(player, lastHeader, tmp.ToByteData(), asServer);
                tmp.ResetPositionAndMode(false);

                data.data.SetBitPosition(pos + lastLen * 8);
            }

            data.data.Dispose();
        }

        public void Queue(DisposableList<PlayerID> targets, HEADER header, ByteData content, Channel channel)
        {
            for (var i = targets.Count - 1; i >= 0; i--)
                Queue(targets[i], header, content, channel);
        }

        public void Queue(PlayerID target, BitPacker header, BitPacker defaultHeader, HEADER headerVal, ByteData content, Channel channel)
        {
            using (_queueSingleMarker.Auto())
            {
                var batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
                var batch = _batches[batchIdx];
                int bytesAfterHeaderLen = batch.batchedData.positionInBytes + content.length + header.positionInBytes;

                // do some MTU checks past 1 batch
                if (batch.batchCount > 0)
                {
                    batch.cachedMTU ??= _playersManager.GetMTU(target, channel, target != PlayerID.Server);
                    if (bytesAfterHeaderLen + 10 >= batch.cachedMTU.Value) // 10 here is just a safety margin
                    {
                        SendBatch(batch);
                        batch.batchCount = 0;
                        batch.cachedMTU = null;
                        batch.batchedData.ResetPositionAndMode(false);
                        batch.batchedData.WriteBits(defaultHeader);
                    }
                    else
                    {
                        batch.batchedData.WriteBits(header);
                    }
                }
                else
                {
                    batch.batchedData.WriteBits(header);
                }

                ++batch.batchCount;
                batch.lastHeader = headerVal;
                batch.lastDataLen = content.length;
                if (content.length > 0)
                    batch.batchedData.WriteBytes(content);

                _batches[batchIdx] = batch;
            }
        }

        public void Queue(PlayerID target, HEADER header, ByteData content, Channel channel)
        {
            using (_queueSingleMarker.Auto())
            {
                var batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
                var batch = _batches[batchIdx];

                int before = batch.batchedData.positionInBits;
                Size contentLen = content.length;

                DeltaPacker<HEADER>.WriteFunc(batch.batchedData, batch.lastHeader, header);
                DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);

                int bytesAfterHeaderLen = batch.batchedData.positionInBytes + content.length;

                // do some MTU checks past 1 batch
                if (batch.batchCount > 0)
                {
                    batch.cachedMTU ??= _playersManager.GetMTU(target, channel, target != PlayerID.Server);
                    if (bytesAfterHeaderLen + 10 >= batch.cachedMTU.Value) // 10 here is just a safety margin
                    {
                        // undo the last write
                        batch.batchedData.SetBitPosition(before);
                        SendBatch(batch);

                        batch.batchCount = 0;
                        batch.lastHeader = default;
                        batch.lastDataLen = default;
                        batch.batchedData.ResetPositionAndMode(false);

                        // redo the last write
                        DeltaPacker<HEADER>.WriteFunc(batch.batchedData, batch.lastHeader, header);
                        DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);
                    }
                }

                ++batch.batchCount;
                batch.lastHeader = header;
                batch.lastDataLen = contentLen;

                if (content.length > 0)
                    batch.batchedData.WriteBytes(content);

                _batches[batchIdx] = batch;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _batches.Count; i++)
                _batches[i].batchedData.Dispose();

            _batches.Clear();
            _batchIndexMap.Clear();
        }
    }
}
