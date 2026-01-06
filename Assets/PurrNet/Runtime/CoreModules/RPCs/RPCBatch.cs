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
        static readonly ProfilerMarker _queueMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Queue");
        static readonly ProfilerMarker _queueSimilarSearchMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Queue(Gathering Similar Batches)");
        static readonly ProfilerMarker _queueSimilarSendMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Queue(Sending Similar Batches)");
        static readonly ProfilerMarker _queueSingleMarker = new ProfilerMarker($"RPCBatch<{typeof(HEADER).Name}>.Queue(Single)");

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

        struct UsersBatchKey : IEquatable<UsersBatchKey>
        {
            public HEADER lastHeader;
            public Size lastDataLen;

            public bool Equals(UsersBatchKey other)
            {
                return lastHeader.Equals(other.lastHeader) &&
                       lastDataLen.Equals(other.lastDataLen);
            }

            public override bool Equals(object obj)
            {
                return obj is UsersBatchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(lastHeader, lastDataLen);
            }
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
                bool sentOne = false;
                for (int i = _batches.Count - 1; i >= 0; i--)
                {
                    var batch = _batches[i];

                    if (batch.key.channel != channel)
                        continue;

                    SendBatch(batch);
                    sentOne = true;
                    batch.batchedData.Dispose();
                    _batches.RemoveAt(i--);
                }

                if (sentOne)
                    _batchIndexMap.Clear();
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
                int c = _batches.Count;

                if (_batchIndexMap.TryGetValue(key, out int idx))
                    return idx;

                for (var i = c - 1; i >= 0; i--)
                {
                    if (BatchKey.AreEquals(key, _batches[i].key))
                    {
                        _batchIndexMap[key] = i;
                        return i;
                    }
                }

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
            using (_queueMarker.Auto())
            {
                Size len = content.length;
                var similarBatches = DisposableDictionary<UsersBatchKey, DisposableList<PlayerID>>.Create();
                similarBatches.dictionary.EnsureCapacity(targets.Count);

                using (_queueSimilarSearchMarker.Auto())
                {
                    for (var i = targets.Count - 1; i >= 0; i--)
                    {
                        var target = targets[i];

                        UsersBatchKey key;

                        if (_batchIndexMap.TryGetValue(new BatchKey { playerId = target, channel = channel },
                                out int idx))
                        {
                            var batch = _batches[idx];
                            key = new UsersBatchKey { lastHeader = batch.lastHeader, lastDataLen = batch.lastDataLen };
                        }
                        else key = new UsersBatchKey { lastHeader = default, lastDataLen = default };

                        if (similarBatches.TryGetValue(key, out var list))
                        {
                            list.Add(target);
                        }
                        else
                        {
                            var newList = DisposableList<PlayerID>.Create();
                            newList.Add(target);
                            similarBatches[key] = newList;
                        }
                    }
                }

                using (_queueSimilarSendMarker.Auto())
                {
                    using (var enumerator = similarBatches.dictionary.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            var key = enumerator.Current.Key;
                            var value = enumerator.Current.Value;

                            using var packer = BitPackerPool.Get();
                            DeltaPacker<HEADER>.WriteFunc(packer, key.lastHeader, header);
                            DeltaPackInteger.WriteIndex(packer, key.lastDataLen, len);

                            for (var i = value.Count - 1; i >= 0; i--)
                                Queue(value[i], packer, header, content, channel);

                            value.Dispose();
                        }
                    }
                }

                similarBatches.Dispose();
            }
        }

        public void Queue(PlayerID target, BitPacker header, HEADER headerVal, ByteData content, Channel channel)
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
                        batch.lastHeader = default;
                        batch.lastDataLen = default;
                        batch.batchedData.ResetPositionAndMode(false);
                    }
                }

                ++batch.batchCount;
                batch.lastHeader = headerVal;
                batch.lastDataLen = content.length;
                batch.batchedData.WriteBits(header);
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
