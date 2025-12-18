using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Transports;

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

        public void Flush()
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

        public void FlushChannel(Channel channel)
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

            for (var i = 0; i < data.count.value; ++i)
            {
                DeltaPacker<HEADER>.Read(data.data, lastHeader, ref lastHeader);
                var rpcData = Packer<ByteData>.Read(data.data);
                _onRPCReceived.Invoke(player, lastHeader, rpcData, asServer);
            }

            data.data.Dispose();
        }

        private static int GetHeaderSize(HEADER old, HEADER header)
        {
            using var tmp = BitPackerPool.Get();
            DeltaPacker<HEADER>.Write(tmp, old, header);
            return tmp.length + 10;
        }

        public void Queue(PlayerID target, HEADER header, ByteData content, Channel channel)
        {
            var batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
            var batch = _batches[batchIdx];

            if (batch.batchCount > 0)
            {
                int headerSize = GetHeaderSize(batch.lastHeader, header);
                int packetSize = headerSize + content.length;
                int mtu = _playersManager.GetMTU(target, channel, target != PlayerID.Server);

                if (batch.batchedData.length + packetSize >= mtu)
                {
                    batch.completed = true;
                    _batches[batchIdx] = batch;
                    // create new batch
                    batchIdx = GetBatchIndex(new BatchKey { playerId = target, channel = channel });
                    batch = _batches[batchIdx];
                }
            }

            ++batch.batchCount;

            DeltaPacker<HEADER>.Write(batch.batchedData, batch.lastHeader, header);
            batch.lastHeader = header;

            Packer<ByteData>.Write(batch.batchedData, content);

            _batches[batchIdx] = batch;
        }

        public void Clear()
        {
            _batches.Clear();
        }
    }
}
