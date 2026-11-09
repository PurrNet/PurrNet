using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    internal struct RPCBatchPacket : IPackedAuto
    {
        public const int HEADER = 6;

        public Size count;
        public BitData data;
    }

    internal struct ImmediateRPCBatchPacket : IPackedAuto
    {
        public Size count;
        public BitData data;
    }

    internal struct PendingBatchedData
    {
        public BatchKey key;
        public UnionRPCHeader lastHeader;
        public Size lastDataLen;
        public ulong stateVersion;
        public int batchCount;
        public int cachedMTU;
        public BitPacker batchedData;
        // 1-based index into the shared groups; 0 = this batch owns its content. While grouped,
        // batchedData is empty and the group holds the bytes every member will receive.
        public int groupPlusOne;
    }

    // Recipients whose pending batches are byte-identical share one buffer: an RPC queued to
    // every member is appended once and the result is sent to each member on flush.
    internal sealed class SharedRPCBatch
    {
        public UnionRPCHeader lastHeader;
        public Size lastDataLen;
        public ulong stateVersion;
        public int batchCount;
        public int cachedMTU;
        public Channel channel;
        public BitPacker data;
        public readonly List<int> members = new();
        public ulong hitOp;
        public int hits;
        public ulong appendOp;
        public ulong splitOp;
        // Bumped on every membership change, so a cached "these targets are this group" expires.
        public int modCount;
    }

    internal struct CachedRPCEntry
    {
        public UnionRPCHeader previousHeader;
        public Size previousDataLen;
        public ulong previousStateVersion;
        public int bitLength;
        public BitPacker data;
        public bool isValid;
    }

#if UNITY_INCLUDE_TESTS
    internal interface IRPCBatchBackend
    {
        MTUExceededBehaviour mtuExceededBehaviour { get; }
        int GetMTU(PlayerID player, Channel channel, bool asServer);
        void Send(PlayerID player, RPCBatchPacket data, Channel channel, MTUExceededBehaviour? mtuOverride = null);

        void Send(PlayerID player, ImmediateRPCBatchPacket data, Channel channel,
            MTUExceededBehaviour? mtuOverride = null)
        {
        }

        void Subscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback);
        void Unsubscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback);
    }
#endif

    public readonly struct ObserverFilter
    {
        public readonly PlayerID skipA;
        public readonly PlayerID skipB;
        public readonly PlayerID only;
        public readonly bool hasSkipA;
        public readonly bool hasSkipB;
        public readonly bool hasOnly;

        public ObserverFilter(PlayerID skipA, bool hasSkipA, PlayerID skipB, bool hasSkipB)
        {
            this.skipA = skipA;
            this.hasSkipA = hasSkipA;
            this.skipB = skipB;
            this.hasSkipB = hasSkipB;
            only = default;
            hasOnly = false;
        }

        public ObserverFilter(PlayerID skipA, bool hasSkipA, PlayerID skipB, bool hasSkipB, PlayerID only, bool hasOnly)
        {
            this.skipA = skipA;
            this.hasSkipA = hasSkipA;
            this.skipB = skipB;
            this.hasSkipB = hasSkipB;
            this.only = only;
            this.hasOnly = hasOnly;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldSkip(PlayerID player)
        {
            if (hasOnly && player != only) return true;
            if (hasSkipA && player == skipA) return true;
            if (hasSkipB && player == skipB) return true;
            return false;
        }
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
#if UNITY_INCLUDE_TESTS
        private readonly IRPCBatchBackend _backend;
#endif
        private PendingBatchedData[] _batches = new PendingBatchedData[128];
        private readonly BatchIndexMap _batchIndexMap;
        private int _batchCount = 0;
        private ulong _nextStateVersion;
        private CachedRPCEntry _entryCacheA;
        private CachedRPCEntry _entryCacheB;
        private readonly List<SharedRPCBatch> _groups = new();
        private readonly Stack<SharedRPCBatch> _freeGroups = new();
        private readonly List<int> _queueTargets = new();
        private readonly List<int> _newGroup = new();
        private readonly List<int> _groupScratch = new();
        private ulong[] _batchOp = new ulong[128];
        private ulong _queueOp;
        private readonly List<PlayerID> _sendScratch = new();

        // The last fan-out's targets, when they were exactly one group's members: an RPC to the
        // same recipients (every object observed by everyone) appends once with no per-target work.
        private PlayerID[] _lastTargets = new PlayerID[16];
        private int _lastTargetCount;
        private Channel _lastChannel;
        private SharedRPCBatch _lastGroup;
        private int _lastGroupIndex = -1;
        private int _lastGroupMod;

        public delegate void RPCReceivedDelegate(PlayerID sender, UnionRPCHeader header, BitData content, bool asServer);
        private readonly RPCReceivedDelegate _onRPCReceived;

        private readonly bool _subscribed;
        private readonly bool _sendAsImmediate;

        /// <summary>
        /// Raised when immediate content reaches the transport outside a flush
        /// (oversized solo entry), so the owner can still force a send this frame.
        /// </summary>
        internal Action onImmediateAutoFlush;

        internal delegate void LocalBatchDelivery(Size count, BitData payload, bool immediate);

        internal Func<PlayerID, bool> isLocalClient;
        internal LocalBatchDelivery deliverLocally;

        private struct LocalBatch
        {
            public Size count;
            public BitData payload;
        }

        private readonly List<LocalBatch> _localPending = new List<LocalBatch>();
        private readonly List<LocalBatch> _localDelivering = new List<LocalBatch>();
        private bool _deliveringLocally;

        public RPCBatch(PlayersManager playersManager, RPCReceivedDelegate callback, bool subscribeToReceives = true,
            bool sendAsImmediate = false)
        {
            _playersManager = playersManager;
            _onRPCReceived = callback;
            _batchIndexMap = new BatchIndexMap(128);
            _subscribed = subscribeToReceives;
            _sendAsImmediate = sendAsImmediate;
            if (subscribeToReceives)
                _playersManager.Subscribe<RPCBatchPacket>(OnBatchReceived);
        }

#if UNITY_INCLUDE_TESTS
        internal RPCBatch(IRPCBatchBackend backend, RPCReceivedDelegate callback, bool sendAsImmediate = false)
        {
            _backend = backend;
            _onRPCReceived = callback;
            _batchIndexMap = new BatchIndexMap(128);
            _subscribed = true;
            _sendAsImmediate = sendAsImmediate;
            _backend.Subscribe(OnBatchReceived);
        }
#endif

        public bool hasPending
        {
            get
            {
                for (int i = 0; i < _batchCount; i++)
                {
                    if (_batches[i].batchCount > 0)
                        return true;
                }

                for (int i = 0; i < _groups.Count; i++)
                {
                    if (_groups[i] != null && _groups[i].batchCount > 0)
                        return true;
                }

                return false;
            }
        }

        public void Dispose()
        {
#if UNITY_INCLUDE_TESTS
            if (_playersManager != null)
            {
                if (_subscribed)
                    _playersManager.Unsubscribe<RPCBatchPacket>(OnBatchReceived);
            }
            else
                _backend.Unsubscribe(OnBatchReceived);
#else
            if (_subscribed)
                _playersManager.Unsubscribe<RPCBatchPacket>(OnBatchReceived);
#endif

            Clear();
            while (_freeGroups.Count > 0)
                _freeGroups.Pop().data?.Dispose();
            _entryCacheA.data?.Dispose();
            _entryCacheB.data?.Dispose();
            _batchIndexMap.Dispose();
        }

        public void Flush()
        {
            using (_flushMarker.Auto())
            {
                for (int g = 0; g < _groups.Count; g++)
                {
                    var group = _groups[g];
                    if (group == null)
                        continue;

                    SendGroup(group);
                    ReleaseGroup(g);
                }

                _groups.Clear();

                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];
                    SendBatch(ref batch);
                    batch.batchedData.Dispose();
                }

                _batchCount = 0;
                _batchIndexMap.Clear();
                _nextStateVersion = 0;
            }

            DeliverLocalBatches();
        }

        private void DeliverLocalBatches()
        {
            // A delivered RPC may flush again (a despawn, say); the outer loop picks up what it adds.
            if (_deliveringLocally)
                return;

            _deliveringLocally = true;
            try
            {
                while (_localPending.Count > 0)
                {
                    _localDelivering.AddRange(_localPending);
                    _localPending.Clear();

                    for (int i = 0; i < _localDelivering.Count; i++)
                    {
                        var local = _localDelivering[i];
                        try
                        {
                            deliverLocally?.Invoke(local.count, local.payload, _sendAsImmediate);
                        }
                        catch (Exception e)
                        {
                            PurrLogger.LogException(e);
                        }
                        finally
                        {
                            local.payload.Dispose();
                        }
                    }

                    _localDelivering.Clear();
                }
            }
            finally
            {
                _deliveringLocally = false;
            }
        }

        public void FlushChannel(Channel channel)
        {
            using (_flushChannelMarker.Auto())
            {
                // Compaction below moves batch indices; give every member its own copy first.
                DissolveAllGroups();

                int writeIdx = 0;

                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];

                    if (batch.key.channel == channel)
                    {
                        SendBatch(ref batch);
                        batch.batchedData.Dispose();
                        _batchIndexMap.Remove(batch.key.playerId.id.value, batch.key.channel);
                    }
                    else
                    {
                        // Keep this batch, shift it down if needed
                        if (writeIdx != i)
                        {
                            _batches[writeIdx] = _batches[i];
                            _batchIndexMap.Set(batch.key.playerId.id.value, batch.key.channel, writeIdx);
                        }
                        writeIdx++;
                    }
                }

                _batchCount = writeIdx;
            }

            DeliverLocalBatches();
        }

        private void SendBatch(ref PendingBatchedData batch)
        {
            if (batch.batchCount == 0)
                return;

            Send(batch.key.playerId, batch.batchCount, new BitData(batch.batchedData), batch.key.channel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Send(PlayerID player, Size count, BitData payload, Channel channel,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (deliverLocally != null && isLocalClient != null && isLocalClient(player))
            {
                // The pending buffer is reused right after this call, so the local copy owns its bits.
                var copy = payload.Duplicate();
                copy.packer?.ResetMode(true);
                _localPending.Add(new LocalBatch { count = count, payload = copy });
                return;
            }

            if (_sendAsImmediate)
            {
                var immediate = new ImmediateRPCBatchPacket
                {
                    count = count,
                    data = payload
                };

#if UNITY_INCLUDE_TESTS
                if (_playersManager != null)
                    _playersManager.Send(player, immediate, channel, mtuOverride);
                else
                    _backend.Send(player, immediate, channel, mtuOverride);
#else
                _playersManager.Send(player, immediate, channel, mtuOverride);
#endif
                return;
            }

            var data = new RPCBatchPacket
            {
                count = count,
                data = payload
            };

#if UNITY_INCLUDE_TESTS
            if (_playersManager != null)
                _playersManager.Send(player, data, channel, mtuOverride);
            else
                _backend.Send(player, data, channel, mtuOverride);
#else
            _playersManager.Send(player, data, channel, mtuOverride);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MTUExceededBehaviour ResolveMTUBehaviour(Channel channel, MTUBehaviour mtuOverride)
        {
            // sequencing is a channel-wide property; per-message overrides only apply to Unreliable
            if (channel != Channel.UnreliableSequenced &&
                mtuOverride != MTUBehaviour.NetworkManager)
                return mtuOverride.Resolve(default);

#if UNITY_INCLUDE_TESTS
            return _playersManager != null
                ? _playersManager.mtuExceededBehaviour
                : _backend.mtuExceededBehaviour;
#else
            return _playersManager.mtuExceededBehaviour;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsUnreliable(Channel channel)
        {
            return channel is Channel.Unreliable or Channel.UnreliableSequenced;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMTU(PlayerID player, Channel channel, bool asServer)
        {
#if UNITY_INCLUDE_TESTS
            return _playersManager != null
                ? _playersManager.GetMTU(player, channel, asServer)
                : _backend.GetMTU(player, channel, asServer);
#else
            return _playersManager.GetMTU(player, channel, asServer);
#endif
        }

        private void SendGroup(SharedRPCBatch group)
        {
            if (group.batchCount == 0)
                return;

            _sendScratch.Clear();
            for (int m = 0; m < group.members.Count; m++)
            {
                var player = _batches[group.members[m]].key.playerId;
                if (deliverLocally != null && isLocalClient != null && isLocalClient(player))
                    Send(player, group.batchCount, new BitData(group.data), group.channel);
                else
                    _sendScratch.Add(player);
            }

            if (_sendScratch.Count == 1)
                Send(_sendScratch[0], group.batchCount, new BitData(group.data), group.channel);
            else if (_sendScratch.Count > 1)
                SendMany(_sendScratch, group.batchCount, new BitData(group.data), group.channel);
        }

        // One serialization for every recipient of a shared batch.
        private void SendMany(List<PlayerID> players, Size count, BitData payload, Channel channel)
        {
#if UNITY_INCLUDE_TESTS
            if (_playersManager == null)
            {
                for (int i = 0; i < players.Count; i++)
                    Send(players[i], count, payload, channel);
                return;
            }
#endif
            if (_sendAsImmediate)
            {
                _playersManager.Send((IReadOnlyList<PlayerID>)players,
                    new ImmediateRPCBatchPacket { count = count, data = payload }, channel);
                return;
            }

            _playersManager.Send((IReadOnlyList<PlayerID>)players, new RPCBatchPacket { count = count, data = payload },
                channel);
        }

        private bool TryQueueSameTargets(IReadOnlyList<PlayerID> targets, in UnionRPCHeader header,
            in BitData content, Channel channel)
        {
            var group = _lastGroup;
            if (group == null || channel != _lastChannel || targets.Count != _lastTargetCount ||
                _lastGroupIndex < 0 || _lastGroupIndex >= _groups.Count || _groups[_lastGroupIndex] != group ||
                group.modCount != _lastGroupMod || group.members.Count != targets.Count)
                return false;

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != _lastTargets[i])
                    return false;
            }

            EnsureEntryCache();
            ResetEntryCache();
            ulong stateVersion = NextStateVersion();
            return TryAppendToGroup(group, header, content, content.bitLength, stateVersion);
        }

        private void RememberTargets(IReadOnlyList<PlayerID> targets, Channel channel)
        {
            _lastGroup = null;
            if (targets.Count < 3)
                return;

            int first = GetBatchIndex(targets[0], channel);
            int groupPlusOne = _batches[first].groupPlusOne;
            if (groupPlusOne == 0)
                return;

            var group = _groups[groupPlusOne - 1];
            if (group.members.Count != targets.Count)
                return;

            for (int i = 1; i < targets.Count; i++)
            {
                if (_batches[GetBatchIndex(targets[i], channel)].groupPlusOne != groupPlusOne)
                    return;
            }

            if (_lastTargets.Length < targets.Count)
                _lastTargets = new PlayerID[Math.Max(targets.Count, _lastTargets.Length * 2)];
            for (int i = 0; i < targets.Count; i++)
                _lastTargets[i] = targets[i];

            _lastTargetCount = targets.Count;
            _lastChannel = channel;
            _lastGroup = group;
            _lastGroupIndex = groupPlusOne - 1;
            _lastGroupMod = group.modCount;
        }

        private void ResetGroupContent(SharedRPCBatch group)
        {
            group.data.ResetPositionAndMode(false);
            group.batchCount = 0;
            group.lastHeader = default;
            group.lastDataLen = default;
            group.stateVersion = 0;
        }

        // Detaches every member (their own batches stay empty) and recycles the group.
        private void ReleaseGroup(int groupIndex)
        {
            var group = _groups[groupIndex];
            for (int m = 0; m < group.members.Count; m++)
                _batches[group.members[m]].groupPlusOne = 0;

            group.members.Clear();
            group.modCount++;
            ResetGroupContent(group);
            _groups[groupIndex] = null;
            _freeGroups.Push(group);
        }

        // Gives one member its own copy of the group content, then leaves the group.
        private void DetachFromGroup(int batchIdx)
        {
            ref var batch = ref _batches[batchIdx];
            int groupIndex = batch.groupPlusOne - 1;
            var group = _groups[groupIndex];

            batch.groupPlusOne = 0;
            batch.batchedData.ResetPositionAndMode(false);
            if (group.data.positionInBits > 0)
                batch.batchedData.WriteBitsWithoutConsumingItUnchecked(group.data, group.data.positionInBits);
            batch.batchCount = group.batchCount;
            batch.lastHeader = group.lastHeader;
            batch.lastDataLen = group.lastDataLen;
            batch.stateVersion = group.stateVersion;

            group.members.Remove(batchIdx);
            group.modCount++;
            if (group.members.Count == 1)
                DetachFromGroup(group.members[0]);
            else if (group.members.Count == 0)
            {
                ResetGroupContent(group);
                _groups[groupIndex] = null;
                _freeGroups.Push(group);
            }
        }

        private void DissolveAllGroups()
        {
            for (int g = 0; g < _groups.Count; g++)
            {
                var group = _groups[g];
                while (group != null && group.members.Count > 0 && _groups[g] == group)
                    DetachFromGroup(group.members[group.members.Count - 1]);
            }

            _groups.Clear();
        }

        private SharedRPCBatch CreateGroup(List<int> members, Channel channel, int mtu)
        {
            var group = _freeGroups.Count > 0 ? _freeGroups.Pop() : new SharedRPCBatch();
            group.data ??= BitPackerPool.Get();
            ResetGroupContent(group);
            group.channel = channel;
            group.cachedMTU = mtu;
            group.hitOp = 0;
            group.appendOp = 0;
            group.splitOp = 0;

            int index = _groups.Count;
            _groups.Add(group);
            group.modCount++;
            for (int i = 0; i < members.Count; i++)
            {
                group.members.Add(members[i]);
                _batches[members[i]].groupPlusOne = index + 1;
            }

            return group;
        }

        // Appends one entry to a group; false when it cannot be shared (an entry that alone
        // exceeds the MTU), and the caller queues it per member instead.
        private bool TryAppendToGroup(SharedRPCBatch group, in UnionRPCHeader header, in BitData content,
            Size contentLen, ulong stateVersion)
        {
            GetCachedEntry(group.lastHeader, group.lastDataLen, group.stateVersion, header, content, contentLen,
                out var entryData, out int entryBitLength);

            if (group.batchCount > 0)
            {
                int bytesAfterEntry = (group.data.positionInBits + entryBitLength + 7) >> 3;
                if (bytesAfterEntry + 10 >= group.cachedMTU)
                {
                    SendGroup(group);
                    ResetGroupContent(group);
                    GetCachedEntry(default, default, 0, header, content, contentLen, out entryData,
                        out entryBitLength);

                    if (_sendAsImmediate)
                        onImmediateAutoFlush?.Invoke();
                }
            }

            if (group.batchCount == 0 && ((entryBitLength + 7) >> 3) + 10 >= group.cachedMTU)
                return false;

            group.data.WriteBitsWithoutConsumingItUnchecked(entryData, entryBitLength);
            ++group.batchCount;
            group.lastHeader = header;
            group.lastDataLen = contentLen;
            group.stateVersion = stateVersion;
            return true;
        }

        // Fan-out queue: whole groups get the entry once, empty batches that receive it together
        // form a new group, everything else (partially targeted groups split first) is per batch.
        private void QueueShared(List<int> targets, in UnionRPCHeader header, in BitData content, Size contentLen,
            Channel channel, ulong stateVersion, MTUBehaviour mtuOverride)
        {
            ulong op = ++_queueOp;

            for (int i = 0; i < targets.Count; i++)
            {
                _batchOp[targets[i]] = op;
                int g = _batches[targets[i]].groupPlusOne - 1;
                if (g < 0)
                    continue;

                var group = _groups[g];
                if (group.hitOp != op)
                {
                    group.hitOp = op;
                    group.hits = 0;
                }

                group.hits++;
            }

            _newGroup.Clear();
            int newGroupMtu = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                int idx = targets[i];
                int g = _batches[idx].groupPlusOne - 1;

                if (g >= 0)
                {
                    var group = _groups[g];

                    // Mostly targeted (an observers RPC that skips its owner, say): the members it
                    // skips take their own copy and the group keeps the shared buffer.
                    if (group.hits < group.members.Count && group.hits * 2 >= group.members.Count &&
                        group.splitOp != op)
                    {
                        group.splitOp = op;
                        _groupScratch.Clear();
                        for (int m = 0; m < group.members.Count; m++)
                        {
                            if (_batchOp[group.members[m]] != op)
                                _groupScratch.Add(group.members[m]);
                        }

                        for (int m = 0; m < _groupScratch.Count; m++)
                        {
                            if (_batches[_groupScratch[m]].groupPlusOne == g + 1)
                                DetachFromGroup(_groupScratch[m]);
                        }

                        // A split of two leaves a single member, which dissolves the group.
                        if (_batches[idx].groupPlusOne != g + 1)
                        {
                            QueueCached(_batches[idx].key.playerId, header, content, contentLen, channel,
                                stateVersion, mtuOverride);
                            continue;
                        }
                    }

                    if (group.hits == group.members.Count)
                    {
                        if (group.appendOp == op)
                            continue;


                        group.appendOp = op;
                        if (TryAppendToGroup(group, header, content, contentLen, stateVersion))
                            continue;

                        // Oversized entry: fall back to the per-batch path for every member.
                        var members = ListPool<int>.Instantiate();
                        members.AddRange(group.members);
                        for (int m = members.Count - 1; m >= 0; m--)
                        {
                            if (_batches[members[m]].groupPlusOne != 0)
                                DetachFromGroup(members[m]);
                        }

                        for (int m = 0; m < members.Count; m++)
                        {
                            ref var member = ref _batches[members[m]];
                            QueueCached(member.key.playerId, header, content, contentLen, channel, stateVersion,
                                mtuOverride);
                        }

                        ListPool<int>.Destroy(members);
                        continue;
                    }

                    DetachFromGroup(idx);
                }

                ref var batch = ref _batches[idx];
                if (batch.batchCount == 0 && (_newGroup.Count == 0 || batch.cachedMTU == newGroupMtu))
                {
                    newGroupMtu = batch.cachedMTU;
                    _newGroup.Add(idx);
                    continue;
                }

                QueueCached(batch.key.playerId, header, content, contentLen, channel, stateVersion, mtuOverride);
            }

            if (_newGroup.Count >= 2)
            {
                var group = CreateGroup(_newGroup, channel, newGroupMtu);
                group.appendOp = op;
                if (TryAppendToGroup(group, header, content, contentLen, stateVersion))
                    return;

                while (group.members.Count > 0)
                    DetachFromGroup(group.members[group.members.Count - 1]);
            }

            for (int i = 0; i < _newGroup.Count; i++)
            {
                ref var batch = ref _batches[_newGroup[i]];
                QueueCached(batch.key.playerId, header, content, contentLen, channel, stateVersion, mtuOverride);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBatchIndex(PlayerID playerId, Channel channel)
        {
            if (_batchIndexMap.TryGetValue(playerId.id.value, channel, out int idx))
                return idx;
            return CreateBatch(new BatchKey { playerId = playerId, channel = channel });
        }

        private int CreateBatch(BatchKey key)
        {
            // Resize if needed
            if (_batchCount >= _batches.Length)
            {
                Array.Resize(ref _batches, _batches.Length * 2);
                Array.Resize(ref _batchOp, _batches.Length);
            }

            int c = _batchCount;
            _batches[c] = new PendingBatchedData
            {
                key = key,
                batchedData = BitPackerPool.Get(),
                cachedMTU = GetMTU(key.playerId, key.channel, key.playerId != PlayerID.Server)
            };
            _batchIndexMap.Set(key.playerId.id.value, key.channel, c);
            _batchCount++;
            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetEntryCache()
        {
            _entryCacheA.isValid = false;
            _entryCacheB.isValid = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureEntryCache()
        {
            _entryCacheA.data ??= BitPackerPool.Get();
            _entryCacheB.data ??= BitPackerPool.Get();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextStateVersion()
        {
            ulong version = ++_nextStateVersion;
            if (version != 0)
                return version;

            // Version zero is reserved for a new/default batch state. If the counter wraps,
            // give every live batch a unique version before issuing the next shared version.
            // This prevents a partially filtered recipient from aliasing a state last used
            // 2^64 queue operations ago.
            for (int i = 0; i < _batchCount; i++)
                _batches[i].stateVersion = ++_nextStateVersion;

            return ++_nextStateVersion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCacheHit(in CachedRPCEntry cache, ulong previousStateVersion,
            in UnionRPCHeader previousHeader, Size previousDataLen)
        {
            if (!cache.isValid)
                return false;

            if (previousStateVersion != 0 && cache.previousStateVersion == previousStateVersion)
                return true;

            return cache.previousDataLen.value == previousDataLen.value &&
                   cache.previousHeader.Equals(previousHeader);
        }

        private static unsafe void BuildCachedEntry(ref CachedRPCEntry cache, in UnionRPCHeader previousHeader,
            Size previousDataLen, ulong previousStateVersion, in UnionRPCHeader header, in BitData content,
            Size contentLen)
        {
            var packer = cache.data;
            packer.ResetPositionAndMode(false);
            NativeDeltaPacker<UnionRPCHeader>.WriteFunc(packer, previousHeader, header);
            DeltaPackInteger.WriteIndex(packer, previousDataLen, contentLen);

            if (contentLen.value > 0)
                packer.WriteBitDataWithoutConsumingIt(content);

            cache.previousHeader = previousHeader;
            cache.previousDataLen = previousDataLen;
            cache.previousStateVersion = previousStateVersion;
            cache.bitLength = packer.positionInBits;
            cache.isValid = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GetCachedEntry(in UnionRPCHeader previousHeader, Size previousDataLen,
            ulong previousStateVersion, in UnionRPCHeader header, in BitData content, Size contentLen,
            out BitPacker data, out int bitLength)
        {
            if (IsCacheHit(_entryCacheA, previousStateVersion, previousHeader, previousDataLen))
            {
                data = _entryCacheA.data;
                bitLength = _entryCacheA.bitLength;
                return;
            }

            if (IsCacheHit(_entryCacheB, previousStateVersion, previousHeader, previousDataLen))
            {
                data = _entryCacheB.data;
                bitLength = _entryCacheB.bitLength;
                return;
            }

            ref var cache = ref (_entryCacheA.isValid ? ref _entryCacheB : ref _entryCacheA);
            BuildCachedEntry(ref cache, previousHeader, previousDataLen, previousStateVersion, header, content,
                contentLen);
            data = cache.data;
            bitLength = cache.bitLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QueueCached(PlayerID target, in UnionRPCHeader header, in BitData content, Size contentLen,
            Channel channel, ulong stateVersion, MTUBehaviour mtuOverride)
        {
            var batchIdx = GetBatchIndex(target, channel);
            if (_batches[batchIdx].groupPlusOne != 0)
                DetachFromGroup(batchIdx);
            ref var batch = ref _batches[batchIdx];

            GetCachedEntry(batch.lastHeader, batch.lastDataLen, batch.stateVersion, header, content, contentLen,
                out var entryData, out int entryBitLength);

            if (batch.batchCount > 0)
            {
                int bytesAfterEntry = (batch.batchedData.positionInBits + entryBitLength + 7) >> 3;
                if (bytesAfterEntry + 10 >= batch.cachedMTU)
                {
                    SendBatch(ref batch);
                    batch.batchCount = 0;
                    batch.batchedData.ResetPositionAndMode(false);
                    GetCachedEntry(default, default, 0, header, content, contentLen, out entryData,
                        out entryBitLength);

                    if (_sendAsImmediate)
                        onImmediateAutoFlush?.Invoke();
                }
            }

            if (batch.batchCount == 0 && IsUnreliable(channel) &&
                ((entryBitLength + 7) >> 3) + 10 >= batch.cachedMTU)
            {
                switch (ResolveMTUBehaviour(channel, mtuOverride))
                {
                    case MTUExceededBehaviour.UpgradeToReliable:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogWarning(
                            $"RPC exceeds MTU ({(entryBitLength + 7) >> 3} bytes, MTU {batch.cachedMTU}). " +
                            $"Upgrading {channel} to {Channel.ReliableOrdered}.");
                        QueueDirect(target, header, content, Channel.ReliableOrdered, mtuOverride);
                        return;
                    case MTUExceededBehaviour.Drop:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogError(
                            $"RPC exceeds MTU ({(entryBitLength + 7) >> 3} bytes, MTU {batch.cachedMTU}). " +
                            $"Dropping {channel} packet.");
                        return;
                    default:
                        batch.batchedData.WriteBitsWithoutConsumingItUnchecked(entryData, entryBitLength);
                        SendSoloEntry(ref batch, channel);
                        return;
                }
            }

            batch.batchedData.WriteBitsWithoutConsumingItUnchecked(entryData, entryBitLength);
            ++batch.batchCount;
            batch.lastHeader = header;
            batch.lastDataLen = contentLen;
            batch.stateVersion = stateVersion;
        }

        private void SendSoloEntry(ref PendingBatchedData batch, Channel channel)
        {
            Send(batch.key.playerId, 1, new BitData(batch.batchedData), channel, MTUExceededBehaviour.Fragment);
            ResetBatchEncoderState(ref batch);

            if (_sendAsImmediate)
                onImmediateAutoFlush?.Invoke();
        }

        private static void ResetBatchEncoderState(ref PendingBatchedData batch)
        {
            batch.batchedData.ResetPositionAndMode(false);
            batch.lastHeader = default;
            batch.lastDataLen = default;
            batch.stateVersion = 0;
            batch.batchCount = 0;
        }

        private void OnBatchReceived(PlayerID player, RPCBatchPacket data, bool asServer)
        {
            ProcessReceivedBatch(player, data.count, data.data, asServer);
        }

        internal unsafe void ProcessReceivedBatch(PlayerID player, Size count, BitData data, bool asServer)
        {
            _batchReceivedMarker.Begin();

            UnionRPCHeader lastHeader = default;
            Size lastLen = default;

            var packer = data.packer;
            using (data.AutoScope())
            {
                for (var i = 0; i < count.value; ++i)
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

        public unsafe void Queue(DisposableList<PlayerID> targets, UnionRPCHeader header, BitData content, Channel channel,
            MTUBehaviour mtuOverride = MTUBehaviour.NetworkManager)
        {
            ValidateChannel(channel);
            _queueMultiMarker.Begin();

            if (targets.Count < 3)
            {
                for (var i = targets.Count - 1; i >= 0; i--)
                    QueueDirect(targets[i], header, content, channel, mtuOverride);

                _queueMultiMarker.End();
                return;
            }

            EnsureEntryCache();
            ResetEntryCache();
            ulong stateVersion = NextStateVersion();
            var contentLen = content.bitLength;

            _queueTargets.Clear();
            for (var i = targets.Count - 1; i >= 0; i--)
                _queueTargets.Add(GetBatchIndex(targets[i], channel));
            QueueShared(_queueTargets, header, content, contentLen, channel, stateVersion, mtuOverride);

            _queueMultiMarker.End();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldQueueDirect(IReadOnlyList<PlayerID> targets, in ObserverFilter filter,
            bool hasFilter)
        {
            if (targets.Count < 3)
                return true;

            if (!hasFilter || targets.Count > 4)
                return false;

            int includedCount = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!filter.ShouldSkip(targets[i]))
                    includedCount++;
            }
            return includedCount < 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateChannel(Channel channel)
        {
            if ((uint)channel > (uint)Channel.Unreliable)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }

        public unsafe void Queue(IReadOnlyList<PlayerID> targets, UnionRPCHeader header, BitData content, Channel channel,
            ObserverFilter filter = default,
            MTUBehaviour mtuOverride = MTUBehaviour.NetworkManager)
        {
            ValidateChannel(channel);
            _queueMultiMarker.Begin();

            bool hasFilter = filter.hasSkipA || filter.hasSkipB || filter.hasOnly;

            if (!hasFilter && TryQueueSameTargets(targets, header, content, channel))
            {
                _queueMultiMarker.End();
                return;
            }

            if (ShouldQueueDirect(targets, filter, hasFilter))
            {
                for (var i = targets.Count - 1; i >= 0; i--)
                {
                    var target = targets[i];
                    if (!hasFilter || !filter.ShouldSkip(target))
                        QueueDirect(target, header, content, channel, mtuOverride);
                }

                _queueMultiMarker.End();
                return;
            }

            EnsureEntryCache();
            ResetEntryCache();
            ulong stateVersion = NextStateVersion();
            var contentLen = content.bitLength;

            _queueTargets.Clear();
            for (var i = targets.Count - 1; i >= 0; i--)
            {
                var target = targets[i];
                if (hasFilter && filter.ShouldSkip(target)) continue;
                _queueTargets.Add(GetBatchIndex(target, channel));
            }
            QueueShared(_queueTargets, header, content, contentLen, channel, stateVersion, mtuOverride);
            if (!hasFilter)
                RememberTargets(targets, channel);

            _queueMultiMarker.End();
        }

        private unsafe void QueueDirect(PlayerID target, in UnionRPCHeader header, in BitData content,
            Channel channel, MTUBehaviour mtuOverride)
        {
            var batchIdx = GetBatchIndex(target, channel);
            if (_batches[batchIdx].groupPlusOne != 0)
                DetachFromGroup(batchIdx);
            ref var batch = ref _batches[batchIdx];

            int before = batch.batchedData.positionInBits;
            int contentByteLen = content.byteLength;
            var contentLen = content.bitLength;

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

                    if (_sendAsImmediate)
                        onImmediateAutoFlush?.Invoke();

                    // redo the last write
                    NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, default, header);
                    DeltaPackInteger.WriteIndex(batch.batchedData, default, contentLen);
                }
            }

            // the entry alone exceeds the MTU; the packer only holds this entry's header at this point
            if (batch.batchCount == 0 && IsUnreliable(channel) &&
                batch.batchedData.positionInBytes + contentByteLen + 10 >= batch.cachedMTU)
            {
                int size = batch.batchedData.positionInBytes + contentByteLen;
                switch (ResolveMTUBehaviour(channel, mtuOverride))
                {
                    case MTUExceededBehaviour.UpgradeToReliable:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogWarning(
                            $"RPC exceeds MTU ({size} bytes, MTU {batch.cachedMTU}). " +
                            $"Upgrading {channel} to {Channel.ReliableOrdered}.");
                        QueueDirect(target, header, content, Channel.ReliableOrdered, mtuOverride);
                        return;
                    case MTUExceededBehaviour.Drop:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogError(
                            $"RPC exceeds MTU ({size} bytes, MTU {batch.cachedMTU}). " +
                            $"Dropping {channel} packet.");
                        return;
                    default:
                        if (contentLen.value > 0)
                            batch.batchedData.WriteBitDataWithoutConsumingIt(content);
                        SendSoloEntry(ref batch, channel);
                        return;
                }
            }

            ++batch.batchCount;
            batch.lastHeader = header;
            batch.lastDataLen = contentLen;
            batch.stateVersion = 0;

            if (contentLen.value > 0)
                batch.batchedData.WriteBitDataWithoutConsumingIt(content);
        }

        public unsafe void Queue(PlayerID target, UnionRPCHeader header, BitData content, Channel channel,
            MTUBehaviour mtuOverride = MTUBehaviour.NetworkManager)
        {
            ValidateChannel(channel);
            _queueSingleMarker.Begin();

            QueueDirect(target, header, content, channel, mtuOverride);
            _queueSingleMarker.End();
        }

        public void Clear()
        {
            for (int g = 0; g < _groups.Count; g++)
            {
                if (_groups[g] != null)
                    ReleaseGroup(g);
            }

            _groups.Clear();

            for (int i = 0; i < _batchCount; i++)
                _batches[i].batchedData.Dispose();

            _batchCount = 0;
            _batchIndexMap.Clear();
            _nextStateVersion = 0;

            for (int i = 0; i < _localPending.Count; i++)
                _localPending[i].payload.Dispose();
            _localPending.Clear();
        }
    }
}
