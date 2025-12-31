using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    [RegisterNetworkType(typeof(RPCBatch<NetworkIdentityRPCHeader>.RPCBatchPacket))]
    [RegisterNetworkType(typeof(RPCBatch<NetworkModuleRPCHeader>.RPCBatchPacket))]
    [RegisterNetworkType(typeof(RPCBatch<StaticRPCHeader>.RPCBatchPacket))]
    public sealed class RPCBatch<HEADER> : IDisposable where HEADER : unmanaged
    {
        struct BatchKey
        {
            public PlayerID playerId;
            public Channel channel;

            public bool Equals(BatchKey other)
            {
                return playerId == other.playerId && channel == other.channel;
            }
        }

        struct PlayerRpcBatchedData
        {
            public BatchKey key;
            public bool completed;

            public HEADER lastHeader;
            public Size lastDataLen;
            public int batchCount;
            public BitPacker batchedData;
        }

        struct RPCBatchPacket : IPackedAuto
        {
            public Size count;
            public BitPacker data;
        }

        private readonly PlayersManager _playersManager;
        private readonly List<PlayerRpcBatchedData> _batches = new ();

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

        static readonly ProfilerMarker _flushMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Flush");

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
            }
        }

        static readonly ProfilerMarker _flushChannelMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.FlushChannel");

        public void FlushChannel(Channel channel)
        {
            using (_flushChannelMarker.Auto())
            {
                for (int i = 0; i < _batches.Count; i++)
                {
                    var batch = _batches[i];

                    if (batch.key.channel != channel)
                        continue;

                    var data = new RPCBatchPacket
                    {
                        count = batch.batchCount,
                        data = batch.batchedData
                    };

                    _playersManager.Send(batch.key.playerId, data, batch.key.channel);
                    batch.batchedData.Dispose();

                    _batches.RemoveAt(i--);
                }
            }
        }

        private int GetBatchIndex(BatchKey key)
        {
            for (var i = 0; i < _batches.Count; i++)
            {
                if (_batches[i].key.Equals(key) && !_batches[i].completed)
                    return i;
            }

            int c = _batches.Count;
            _batches.Add(new PlayerRpcBatchedData { key = key, batchedData = BitPackerPool.Get() });
            return c;
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

        static readonly ProfilerMarker _queueMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Queue");

        public void Queue(PlayerID target, HEADER header, ByteData content, Channel channel)
        {
            using (_queueMarker.Auto())
            {
                var batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
                var batch = _batches[batchIdx];

                int before = batch.batchedData.positionInBits;
                Size contentLen = content.length;

                DeltaPacker<HEADER>.Write(batch.batchedData, batch.lastHeader, header);
                DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);

                int bytesAfterHeaderLen = batch.batchedData.positionInBytes + content.length;

                // do some MTU checks past 1 batch
                if (batch.batchCount > 0)
                {
                    int mtu = _playersManager.GetMTU(target, channel, target != PlayerID.Server);

                    if (bytesAfterHeaderLen + 10 >= mtu) // 10 here is just a safety margin
                    {
                        // undo the last write
                        batch.batchedData.SetBitPosition(before);
                        batch.completed = true;
                        _batches[batchIdx] = batch;
                        // create new batch
                        batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
                        batch = _batches[batchIdx];
                        // redo the last write
                        DeltaPacker<HEADER>.Write(batch.batchedData, batch.lastHeader, header);
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
            _batches.Clear();
        }
    }
}
