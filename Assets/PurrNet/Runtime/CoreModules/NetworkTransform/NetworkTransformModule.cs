using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    [PackerDependencies]
    public class NetworkTransformModule : INetworkModule, IPromoteToServerModule
    {
        public static long entriesWrittenCount;
        public static long adaptiveHoldCount;
        public static long sharedPacketCount;

        static readonly ProfilerMarker _postFixedUpdateMarker = new ProfilerMarker("NetworkTransform.PostFixedUpdate");
        static readonly ProfilerMarker _gatherStateMarker = new ProfilerMarker("NetworkTransform.GatherState");
        static readonly ProfilerMarker _prepareUnreliableMarker = new ProfilerMarker("NetworkTransform.PrepareState");
        static readonly ProfilerMarker _queueChangedMarker = new ProfilerMarker("NetworkTransform.QueueChangedState");
        static readonly ProfilerMarker _decodeUnreliableMarker = new ProfilerMarker("NetworkTransform.DecodeState");
        static readonly ProfilerMarker _processAckMarker = new ProfilerMarker("NetworkTransform.ProcessAck");
        static readonly ProfilerMarker _replayMarker = new ProfilerMarker("NetworkTransform.ReplayState");
        static readonly ProfilerMarker _flushAckMarker = new ProfilerMarker("NetworkTransform.FlushAck");
        static readonly ProfilerMarker _shareKeysMarker = new ProfilerMarker("NetworkTransform.ShareKeys");
        static readonly ProfilerMarker _commitMarker = new ProfilerMarker("NetworkTransform.CommitPacket");

        private readonly List<NetworkTransform> _networkTransforms = new();
        private readonly List<NetworkTransform> _byIndex = new();
        private readonly Stack<int> _freeIndices = new();
        private readonly List<NetworkTransform> _changedTransforms = new();
        private readonly Dictionary<PlayerID, NTUnreliableSendStream> _sendStreams = new();
        private readonly Dictionary<NTUnreliableSendStream, PlayerID> _streamPlayers = new();
        private readonly Dictionary<PlayerID, NTUnreliableRecvStream> _recvStreams = new();
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersBroadcaster _broadcaster;
        private readonly NetworkManager _manager;
        private readonly SceneID _scene;
        private readonly HierarchyFactory _factory;
        private bool _asServer;
        private ushort _currentTick;
        private int _anchorAdoptDelay = 1;

        public NetworkTransformModule(NetworkManager manager, PlayersBroadcaster broadcaster,
            ScenePlayersModule scenePlayers, SceneID scene, HierarchyFactory factory)
        {
            _manager = manager;
            _scenePlayers = scenePlayers;
            _broadcaster = broadcaster;
            _scene = scene;
            _factory = factory;
        }

        public void PromoteToServerModule()
        {
            for (var i = 0; i < _networkTransforms.Count; i++)
            {
                _networkTransforms[i].PromoteNTRegistrationToServer();
                if (_networkTransforms[i].ntClientModule == this)
                {
                    _networkTransforms[i].ntClientModule = null;
                    _networkTransforms[i].ntServerModule = this;
                }
            }

            _asServer = true;
            ReleaseAllStreams();

            for (var i = 0; i < _networkTransforms.Count; i++)
                _networkTransforms[i].ResetUnreliableStream();
        }

        public void PostPromoteToServerModule()
        {
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            _broadcaster.Subscribe<NetworkTransformUnreliableDelta>(OnUnreliableDelta);
            _broadcaster.Subscribe<NetworkTransformUnreliableAck>(OnUnreliableAck);
            _broadcaster.Subscribe<NetworkTransformUnreliableNack>(OnUnreliableNack);
            _broadcaster.Subscribe<NetworkTransformInitialState>(OnInitialState);
            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
        }

        public void Disable(bool asServer)
        {
            _broadcaster.Unsubscribe<NetworkTransformUnreliableDelta>(OnUnreliableDelta);
            _broadcaster.Unsubscribe<NetworkTransformUnreliableAck>(OnUnreliableAck);
            _broadcaster.Unsubscribe<NetworkTransformUnreliableNack>(OnUnreliableNack);
            _broadcaster.Unsubscribe<NetworkTransformInitialState>(OnInitialState);
            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            ReleaseAllStreams();
        }

        private void ReleaseAllStreams()
        {
            foreach (var stream in _sendStreams.Values)
            {
                NTUnreliable.Release(stream.ring);
                stream.ReleaseAdaptive();
                stream.ReleaseDeferred();
            }
            foreach (var stream in _recvStreams.Values)
                NTUnreliable.Release(stream.ring);
            _repairs.Clear();
            if (_global != null)
            {
                NTUnreliable.Release(_global.ring);
                _global.ReleaseAdaptive();
                _global.ReleaseDeferred();
                _global = null;
            }
            _sendStreams.Clear();
            _streamPlayers.Clear();
            _recvStreams.Clear();
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (scene != _scene)
                return;

            // Client streams are keyed PlayerID.Server; when the local player leaves the scene
            // both ends must restart, or a re-join pairs a fresh sender seq with a stale recv window.
            if (!asServer)
            {
                ReleaseAllStreams();
                return;
            }

            if (_sendStreams.TryGetValue(player, out var leaving))
            {
                if (leaving.leader == _global && _global != null)
                {
                    _global.followers.Remove(leaving);
                    leaving.leader = null;
                }
                else
                {
                    Detach(leaving);
                }
            }

            if (_sendStreams.Remove(player, out var send))
            {
                _streamPlayers.Remove(send);
                NTUnreliable.Release(send.ring);
                send.ReleaseAdaptive();
                send.ReleaseDeferred();
            }
            if (_recvStreams.Remove(player, out var recv))
                NTUnreliable.Release(recv.ring);
        }

        private bool IsLive(NetworkTransform nt) => nt is not null && nt.GetNTIndex(_asServer) >= 0;

        // Clears the baseline and bars every entry sent up to now from becoming one again:
        // a late ack of an earlier packet must not resurrect the state that was just dropped.
        private void ClearBaseline(NTUnreliableSendStream stream, NetworkID nid)
        {
            if (!TryGetRegisteredTransform(nid, out var nt))
                return;

            int index = nt.GetNTIndex(_asServer);
            if (index >= 0 && index < stream.baselines.Length)
            {
                stream.baselines[index] = new NTBaselineSlot
                {
                    hasFloor = true,
                    floorTick = _currentTick
                };
            }
        }

        private void ClearAdaptive(NTUnreliableSendStream stream, NetworkID nid)
        {
            if (TryGetRegisteredTransform(nid, out var nt))
                stream.SetAdaptive(nt, null);
        }

        internal NTUnreliableSendStream GetSendStream(PlayerID player)
        {
            if (!_sendStreams.TryGetValue(player, out var stream))
            {
                stream = new NTUnreliableSendStream { asServer = _asServer };
                stream.EnsureBaselineCapacity(_byIndex.Count);
                _sendStreams.Add(player, stream);
                _streamPlayers[stream] = player;
            }

            return stream;
        }

        internal static void AddPending(NTUnreliableSendStream stream, NetworkTransform nt)
        {
            if (!nt || !nt.id.HasValue)
                return;

            var nid = nt.id.Value;

            // New transforms carry the highest ids, so most inserts land at the end of the list.
            int count = stream.pending.Count;
            if (count == 0 || (stream.pending[count - 1] is { ntRegistered: true } last && last.ntNid < nid))
            {
                stream.pending.Add(nt);
                stream.SetPending(nt, true);
                return;
            }

            int lo = 0;
            int hi = stream.pending.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var midNt = stream.pending[mid];
                if (!midNt || !midNt.id.HasValue)
                {
                    stream.pending.RemoveAt(mid);
                    hi = stream.pending.Count - 1;
                    continue;
                }

                var midNid = midNt.id.Value;
                if (midNid.Equals(nid))
                {
                    stream.pending[mid] = nt;
                    stream.SetPending(nt, true);
                    return;
                }

                if (midNid < nid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            stream.pending.Insert(lo, nt);
            stream.SetPending(nt, true);
        }

        internal static bool RemovePending(NTUnreliableSendStream stream, NetworkID nid)
        {
            int lo = 0;
            int hi = stream.pending.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var midNt = stream.pending[mid];
                if (!midNt || !midNt.id.HasValue)
                {
                    stream.pending.RemoveAt(mid);
                    hi = stream.pending.Count - 1;
                    continue;
                }

                var midNid = midNt.id.Value;
                if (midNid.Equals(nid))
                {
                    stream.pending.RemoveAt(mid);
                    stream.SetPending(midNt, false);
                    return true;
                }

                if (midNid < nid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return false;
        }

        internal NTUnreliableRecvStream GetRecvStream(PlayerID player)
        {
            if (!_recvStreams.TryGetValue(player, out var stream))
            {
                stream = new NTUnreliableRecvStream();
                _recvStreams.Add(player, stream);
            }

            return stream;
        }

        internal static bool MarkReceived(NTUnreliableRecvStream stream, ushort seq, out long packetOrder)
        {
            if (!stream.ackInit)
            {
                stream.ackInit = true;
                stream.latestSeq = seq;
                stream.latestOrder = 0;
                stream.ackBits = 0;
                packetOrder = 0;
                return true;
            }

            var diff = (short)(seq - stream.latestSeq);

            switch (diff)
            {
                case > 0:
                {
                    stream.ackBits = diff switch
                    {
                        >= 33 => 0,
                        32 => 1u << 31,
                        _ => (stream.ackBits << diff) | (1u << (diff - 1))
                    };

                    stream.latestOrder += diff;
                    stream.latestSeq = seq;
                    packetOrder = stream.latestOrder;
                    return true;
                }
                case 0:
                    packetOrder = stream.latestOrder;
                    return false;
            }

            int d = -diff;
            if (d > 32)
            {
                packetOrder = stream.latestOrder - d;
                return false;
            }

            uint mask = 1u << (d - 1);
            if ((stream.ackBits & mask) != 0)
            {
                packetOrder = stream.latestOrder - d;
                return false;
            }

            stream.ackBits |= mask;
            packetOrder = stream.latestOrder - d;
            return true;
        }

        internal static bool IsValidEntryBounds(int bodyStart, int bodyLength, int packetEnd)
        {
            return bodyStart >= 0 && bodyStart <= packetEnd &&
                   bodyLength > 0 && bodyLength <= packetEnd - bodyStart;
        }

        private void OnUnreliableDelta(PlayerID player, NetworkTransformUnreliableDelta data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var sender = asServer ? player : PlayerID.Server;
            var stream = GetRecvStream(sender);

            if (data.ack.HasValue && _sendStreams.TryGetValue(sender, out var sendStream))
            {
                var ack = data.ack.Value;
                ProcessAck(sendStream, ack.seq, ack.ackBits);
            }

            using var decodeScope = _decodeUnreliableMarker.Auto();

            bool oldAckInit = stream.ackInit;
            ushort oldLatestSeq = stream.latestSeq;
            long oldLatestOrder = stream.latestOrder;
            uint oldAckBits = stream.ackBits;
            bool oldAckDirty = stream.ackDirty;

            if (!MarkReceived(stream, data.seq, out var packetOrder))
                return;

            bool committed = false;
            var decoded = ListPool<NTUnreliableEntry>.Instantiate();

            try
            {
                using var packet = BitPackerPool.Get(data.packet);
                packet.ResetPositionAndMode(true);

                long packetEndLong = data.packet.length * 8L;
                if (packetEndLong > int.MaxValue)
                    return;
                int packetEnd = (int)packetEndLong;

                int ntCount = default;
                int lastDist = 0;
                NetworkID lastNid = default;
                PackedInt lastLen = default;

                Packer<int>.Read(packet, ref ntCount);

                // Every entry consumes at least one body bit in addition to its framing.
                // This rejects hostile counts before they can turn a tiny packet into a long loop.
                if (ntCount < 0 || ntCount > packetEnd - packet.positionInBits)
                    return;

                for (var i = 0; i < ntCount; i++)
                {
                    PackedInt length = default;
                    DeltaPacker<PackedInt>.Read(packet, lastLen, ref length);
                    lastLen = length;
                    DeltaPacker<NetworkID>.Read(packet, lastNid, ref lastNid);

                    int bodyStart = packet.positionInBits;
                    if (!IsValidEntryBounds(bodyStart, length.value, packetEnd))
                        return;
                    int bodyEnd = bodyStart + length.value;

                    // The abs/gen/dist header is fixed-layout and MUST be consumed even for entries
                    // that get skipped: the dist chain is cross-entry decoder state.
                    bool isAbsolute = packet.ReadBits(1) == 1;
                    byte gen = default;
                    int dist = 0;

                    if (isAbsolute)
                    {
                        Packer<byte>.Read(packet, ref gen);
                    }
                    else if (packet.ReadBits(1) == 1)
                    {
                        dist = lastDist;
                    }
                    else
                    {
                        dist = (int)packet.ReadBits(NTUnreliable.DISTANCE_BITS) + 1;
                        lastDist = dist;
                    }

                    if (packet.positionInBits > bodyEnd)
                        return;

                    bool recorded = false;

                    if (!data.patch && stream.hasPatches &&
                        (short)(data.tick - stream.lastPatchTick) < NTUnreliable.MAX_BASELINE_AGE &&
                        _factory.TryGetIdentity(_scene, lastNid, out var patchedIdentity) &&
                        patchedIdentity is NetworkTransform patchedNt &&
                        patchedNt.unreliableRecvHistory != null && patchedNt.unreliableRecvHistory.IsPatched(data.tick))
                    {
                        packet.SetBitPosition(bodyEnd);
                        continue;
                    }

                    if (_factory.TryGetIdentity(_scene, lastNid, out var identity) && identity is NetworkTransform nt &&
                        (!asServer || nt.IsControlling(player, false)))
                    {
                        NetworkTransformState state = default;
                        NetworkTransformVelocity velocity = default;
                        bool ok;

                        if (isAbsolute)
                        {
                            state = nt.ReadAbsoluteState(packet);
                            ok = true;
                        }
                        else if (dist > 0 && nt.unreliableRecvHistory != null &&
                                 nt.unreliableRecvHistory.TryGet((ushort)(data.tick - dist), out var baseline,
                                     out var baseVel, out gen))
                        {
                            int tickDist = dist;
                            var predicted = NTUnreliable.GetDeltaPrediction(baseline, baseVel, tickDist);
                            state = nt.ReadDeltaState(packet, baseline, predicted);
                            velocity = NetworkTransformVelocity.Derive(baseline, state, tickDist);
                            ok = true;
                        }
                        else
                        {
                            ok = false;
                        }

                        if (packet.positionInBits > bodyEnd)
                            return;

                        if (ok)
                        {
                            NetworkIdentity frameParent = null;
                            if (state.frame == NetworkTransformFrame.LocalIdentity)
                                _factory.TryGetIdentity(_scene, state.parentId, out frameParent);

                            if (data.repair ||
                                nt.TryApplyUnreliableState(state, gen, packetOrder, data.tick, frameParent, isAbsolute))
                            {
                                decoded.Add(new NTUnreliableEntry
                                {
                                    nid = lastNid,
                                    state = state,
                                    velocity = velocity,
                                    tick = data.tick,
                                    gen = gen,
                                    transform = nt
                                });
                                recorded = true;
                            }
                        }
                    }

                    // Anything not recorded must not become an acked baseline on the sender —
                    // acks are packet-granular, so a NACK is the only per-entry signal.
                    if (!recorded)
                    {
                        if (identity is NetworkTransform missed)
                        {
                            missed.unreliableRecvHistory?.Invalidate(data.tick);
                            if (stream.sendsRepairs && !data.repair && !isAbsolute && dist > 0 &&
                                (!asServer || missed.IsControlling(player, false)))
                                RememberUnknown(stream, packet, bodyEnd, lastNid, false, gen, dist, packetOrder,
                                    data.tick, stream.baselineWaiting);
                            else
                                SendNack(sender, lastNid, data.tick);
                        }
                        else
                        {
                            // Not spawned here yet: reliable spawns can trail this stream. Keep the
                            // newest absolute (so the transform can take it once it exists) and
                            // the newest delta it misses.
                            RememberUnknown(stream, packet, bodyEnd, lastNid, isAbsolute, gen, dist, packetOrder,
                                data.tick);
                        }
                    }

                    packet.SetBitPosition(bodyEnd);
                }

                if (data.patch)
                {
                    stream.hasPatches = true;
                    stream.lastPatchTick = data.tick;
                }

                if (data.repair)
                    stream.sendsRepairs = true;

                for (int i = 0; i < decoded.Count; i++)
                {
                    var entry = decoded[i];
                    var history = entry.transform.unreliableRecvHistory ??= new NTRecvHistory();
                    history.Store(entry.tick, entry.state, entry.velocity, entry.gen, data.patch);

                    if (data.patch && stream.pendingNacks.TryGetValue(entry.nid, out var nackTick) &&
                        nackTick == entry.tick)
                        stream.pendingNacks.Remove(entry.nid);
                }

                ref var slot = ref stream.ring[data.seq % NTUnreliable.RING_SIZE];
                slot = new NTUnreliableSlot { used = true, seq = data.seq };
                stream.ackDirty = true;
                stream.packetsSinceAck++;
                if (NTUnreliable.IsAnchorTick(data.tick))
                    stream.anchorUnacked = true;
                committed = true;
            }
            catch (System.Exception exception) when (exception is System.IndexOutOfRangeException or
                                                      System.ArgumentOutOfRangeException or
                                                      System.OverflowException)
            {
                // A short or otherwise malformed packet is equivalent to packet loss. The finally
                // block restores the receive window so a corrupt datagram cannot be acknowledged.
            }
            finally
            {
                if (decoded != null)
                    ListPool<NTUnreliableEntry>.Destroy(decoded);

                if (!committed)
                {
                    stream.ackInit = oldAckInit;
                    stream.latestSeq = oldLatestSeq;
                    stream.latestOrder = oldLatestOrder;
                    stream.ackBits = oldAckBits;
                    stream.ackDirty = oldAckDirty;
                }
            }

            // Only sample the clock offset from packets that decoded cleanly — the
            // rollback above doesn't cover offset state, so a malformed packet must
            // not touch it.
            if (committed)
                UpdateOffsetEstimate(stream, data.tick);

            if (committed && ShouldFlushAckAfterPacket(stream))
                SendStandaloneAck(sender, stream);
        }

        // Mid-receive acks only guard the selective-ACK window. Everything else waits for the end
        // of the tick so a sender tick is acknowledged as a whole and peers adopt anchors together.

        internal static bool ShouldFlushAckAfterPacket(NTUnreliableRecvStream stream)
        {
            return stream.ackDirty && stream.packetsSinceAck >= NTUnreliable.ACK_WINDOW / 2;
        }

        internal static bool ShouldFlushAckAfterTick(NTUnreliableRecvStream stream)
        {
            if (!stream.ackDirty)
                return false;

            stream.ackDelayTicks++;
            return stream.anchorUnacked || stream.packetsSinceAck >= NTUnreliable.ACK_PACKET_THRESHOLD ||
                   stream.ackDelayTicks >= NTUnreliable.ACK_INTERVAL_TICKS;
        }

        private void OnUnreliableAck(PlayerID player, NetworkTransformUnreliableAck data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var key = asServer ? player : PlayerID.Server;
            if (_sendStreams.TryGetValue(key, out var stream))
                ProcessAck(stream, data.seq, data.ackBits);
        }

        internal void ProcessAck(NTUnreliableSendStream stream, ushort seq, uint ackBits)
        {
            if (!stream.hasAckWindow || (short)(seq - stream.lastAckSeq) >= 0)
            {
                stream.hasAckWindow = true;
                stream.lastAckSeq = seq;
                stream.lastAckBits = ackBits;
            }

            using var _ = _processAckMarker.Auto();
            List<int> completed = null;

            try
            {
                TryAdoptAck(stream, seq, ref completed);
                for (int i = 0; i < 32; i++)
                {
                    if ((ackBits & (1u << i)) != 0)
                        TryAdoptAck(stream, (ushort)(seq - 1 - i), ref completed);
                }

                if (completed != null)
                    RemovePendingCompleted(stream, completed);
            }
            finally
            {
                if (completed != null)
                    ListPool<int>.Destroy(completed);
            }
        }

        private static void RemovePendingCompleted(NTUnreliableSendStream stream, List<int> completed)
        {
            if (completed.Count == 0)
                return;

            var flags = stream.ackCompleted;
            for (int i = 0; i < completed.Count; i++)
                flags[completed[i]] = true;

            var pending = stream.pending;
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < pending.Count; readIndex++)
            {
                var nt = pending[readIndex];
                int index = nt is null ? -1 : nt.GetNTIndex(stream.asServer);
                if (index >= 0 && index < flags.Length && flags[index])
                {
                    stream.pendingByIndex[index] = false;
                    continue;
                }

                pending[writeIndex++] = nt;
            }

            if (writeIndex < pending.Count)
                pending.RemoveRange(writeIndex, pending.Count - writeIndex);

            for (int i = 0; i < completed.Count; i++)
                flags[completed[i]] = false;
        }

        private void TryAdoptAck(NTUnreliableSendStream stream, ushort seq, ref List<int> completed)
        {
            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (!slot.used || slot.seq != seq || slot.acked)
                return;

            slot.acked = true;

            // Fan-out streams adopt anchors at a fixed delay after the anchor tick instead of on
            // arrival: every peer whose ack landed inside the window switches on the same tick,
            // so their baselines (and therefore their packets) stay identical.
            // The deferred record takes over the slot entries, since the ring may wrap first.
            if (slot.anchor && stream.asServer)
            {
                // Rises at once, decays slowly: the adoption delay follows the slower acks.
                float lag = (short)(_currentTick - slot.tick);
                stream.anchorAckLag = lag >= stream.anchorAckLag
                    ? lag
                    : stream.anchorAckLag + (lag - stream.anchorAckLag) * 0.05f;

                // Adopted before its ack arrived: the ack now confirms delivery, so transforms
                // whose final state it carried can complete.
                if (slot.optimistic && (stream.leader != _global || slot.patch))
                    CompleteAdopted(stream, slot.entries, ref completed);

                NTSharedEntries.Release(slot.entries);
                slot.entries = null;
                return;
            }

            // A global fan-out peer takes the shared packets' baselines from the shared stream; only
            // its own patch packets carry state it adopts itself.
            if (stream.leader == _global && _global != null)
            {
                if (!slot.patch)
                {
                    NTSharedEntries.Release(slot.entries);
                    slot.entries = null;
                    return;
                }
            }
            // First-baseline adoption is per-peer state: split the peer from its leader or followers.
            else if (slot.entries is not NTSharedEntries { hasAbsolute: false })
                Detach(stream);

            AdoptEntries(stream, slot.entries, slot.order, slot.anchor, true, ref completed);
            slot.entries = null;
        }

        private void AdoptDeferredAnchors(NTUnreliableSendStream stream)
        {
            var deferred = stream.deferredAnchors;
            if (deferred.Count == 0)
                return;

            List<int> completed = null;

            try
            {
                int writeIndex = 0;
                for (int i = 0; i < deferred.Count; i++)
                {
                    var anchor = deferred[i];
                    if ((short)(_currentTick - anchor.tick) < _anchorAdoptDelay)
                    {
                        deferred[writeIndex++] = anchor;
                        continue;
                    }

                    switch (DecideAnchor(stream, anchor))
                    {
                        case NTAnchorDecision.Lost:
                            NTSharedEntries.Release(anchor.entries);
                            break;
                        case NTAnchorDecision.Acked:
                            AdoptEntries(stream, anchor.entries, anchor.order, true, true, ref completed);
                            break;
                        default:
                            stream.ring[anchor.seq % NTUnreliable.RING_SIZE].optimistic = true;
                            AdoptEntries(stream, anchor.entries, anchor.order, true, false, ref completed);
                            break;
                    }
                }

                deferred.RemoveRange(writeIndex, deferred.Count - writeIndex);

                if (completed != null)
                    RemovePendingCompleted(stream, completed);
            }
            finally
            {
                if (completed != null)
                    ListPool<int>.Destroy(completed);
            }
        }

        private enum NTAnchorDecision : byte
        {
            Lost,
            Acked,
            Optimistic
        }

        // Every peer adopts an anchor on the same tick unless its ack window already shows the
        // packet as lost. A peer that turns out not to have it fails the tick lookup and NACKs,
        // which resets that transform to an absolute; only real acks complete transforms.
        private static NTAnchorDecision DecideAnchor(NTUnreliableSendStream stream, in NTDeferredAnchor anchor)
        {
            ref var slot = ref stream.ring[anchor.seq % NTUnreliable.RING_SIZE];
            if (!slot.used || slot.seq != anchor.seq)
                return NTAnchorDecision.Lost;

            if (slot.acked)
                return NTAnchorDecision.Acked;

            if (!stream.hasAckWindow)
                return NTAnchorDecision.Optimistic;

            int d = (short)(stream.lastAckSeq - anchor.seq);
            if (d <= 0)
                return NTAnchorDecision.Optimistic;
            if (d > 32)
                return NTAnchorDecision.Lost;

            return (stream.lastAckBits & (1u << (d - 1))) == 0 ? NTAnchorDecision.Lost : NTAnchorDecision.Acked;
        }

        private static bool CanComplete(NTUnreliableSendStream state, in NTUnreliableEntry entry, NetworkTransform nt,
            int index)
        {
            bool restSettled = !nt.hasSyncStrategy ||
                               state.adaptiveByIndex[index] is not { } lastWrite ||
                               (lastWrite.restConfirmed && lastWrite.redundancy == 0);
            return entry.genEpoch == nt.sendGenEpoch && entry.revision == nt.capturedRevision && restSettled;
        }

        // Completes transforms whose optimistically adopted baseline is the acknowledged entry.
        private void CompleteAdopted(NTUnreliableSendStream stream, List<NTUnreliableEntry> list,
            ref List<int> completed)
        {
            if (list == null)
                return;

            var state = stream.leader == _global && _global != null ? stream : stream.leader ?? stream;
            int start = completed?.Count ?? 0;

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                var nt = entry.transform;
                if (!IsLive(nt) || !nt.ntNid.Equals(entry.nid) || entry.life != nt.ntLife)
                    continue;

                int index = nt.GetNTIndex(stream.asServer);
                if (index < 0 || index >= state.baselines.Length)
                    continue;

                ref var baseline = ref state.baselines[index];
                if (!baseline.has || baseline.tick != entry.tick || baseline.revision != entry.revision ||
                    !state.pendingByIndex[index] || !CanComplete(state, entry, nt, index))
                    continue;

                completed ??= ListPool<int>.Instantiate();
                completed.Add(index);
            }

            // Removing pending transforms is per-peer state.
            if (completed != null && completed.Count > start && stream.leader != _global)
                Detach(stream);
        }

        // Whether adopting these entries with completion would change the pending set.
        private static bool AnyCompletes(NTUnreliableSendStream state, List<NTUnreliableEntry> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                var nt = entry.transform;
                if (nt is null || nt.GetNTIndex(state.asServer) < 0 || !nt.ntNid.Equals(entry.nid))
                    continue;

                int index = nt.GetNTIndex(state.asServer);
                if (index < state.pendingByIndex.Length && state.pendingByIndex[index] &&
                    CanComplete(state, entry, nt, index))
                    return true;
            }

            return false;
        }

        private void AdoptEntries(NTUnreliableSendStream stream, List<NTUnreliableEntry> list, uint order, bool anchor,
            bool allowComplete, ref List<int> completed, List<NetworkID> excluded = null)
        {
            // Off-anchor packets only establish first baselines, and those entries are absolute.
            int count = anchor || list is not NTSharedEntries { hasAbsolute: false } ? list.Count : 0;
            for (int i = 0; i < count; i++)
            {
                var entry = list[i];
                if (excluded != null && excluded.Contains(entry.nid))
                    continue;

                var nt = entry.transform;
                if (!IsLive(nt) || !nt.ntNid.Equals(entry.nid))
                {
                    if (!TryGetRegisteredTransform(entry.nid, out nt))
                        continue;
                }

                // Entries of an earlier registration (pooled respawn, or another instance under
                // the same id) never become baselines of the current one.
                if (entry.transform is not null && (!ReferenceEquals(entry.transform, nt) || entry.life != nt.ntLife))
                    continue;

                // ACK slots are processed newest-first. Once a newer state for this transform was
                // adopted, older retransmissions cannot improve its baseline or complete a newer
                // revision, so skip the registration/generation work entirely.
                int index = nt.GetNTIndex(stream.asServer);
                ref var currentBaseline = ref stream.baselines[index];
                // Ticks order adoptions in any peer's packet numbering (follower state is copied
                // between peers); the packet order only breaks ties within a tick.
                if (currentBaseline.has && ((short)(entry.tick - currentBaseline.tick) < 0 ||
                                            (entry.tick == currentBaseline.tick && order <= currentBaseline.order)))
                    continue;

                // Only anchor packets move an established baseline, even when an off-anchor packet
                // already carries the current revision: every peer then holds the same baseline tick
                // and the per-peer share keys converge.
                if (currentBaseline.has && !anchor)
                    continue;

                uint expectedEpoch = stream.generationOverrides.Count > 0 &&
                                     stream.generationOverrides.TryGetValue(entry.nid, out var generation)
                    ? generation.epoch
                    : nt.sendGenEpoch;

                bool restSettled = !nt.hasSyncStrategy ||
                                   stream.adaptiveByIndex[index] is not { } lastWrite ||
                                   (lastWrite.restConfirmed && lastWrite.redundancy == 0);

                bool completes = entry.genEpoch == expectedEpoch && entry.revision == nt.capturedRevision &&
                                 restSettled;

                if (currentBaseline.hasFloor && (short)(entry.tick - currentBaseline.floorTick) <= 0)
                    continue;

                if (stream.prevBaselines != null)
                    stream.prevBaselines[index] = currentBaseline;

                currentBaseline.has = true;
                currentBaseline.hasFloor = false;
                currentBaseline.state = entry.state;
                currentBaseline.velocity = entry.velocity;
                currentBaseline.tick = entry.tick;
                currentBaseline.gen = entry.gen;
                currentBaseline.genEpoch = entry.genEpoch;
                currentBaseline.revision = entry.revision;
                currentBaseline.order = order;

                if (completes && allowComplete)
                {
                    completed ??= ListPool<int>.Instantiate();
                    completed.Add(index);
                }
            }

            // Once this packet is acknowledged, every state needed for future deltas lives
            // in stream.baselines. Retaining the packet's full snapshots until ring wrap makes
            // clean-link memory scale with 64 packets instead of the actual in-flight window.
            NTSharedEntries.Release(list);
        }

        internal void SendInitialState(IReadOnlyList<PlayerID> players, NetworkID id, in NetworkTransformState state,
            byte gen)
        {
            _broadcaster.Send(players, new NetworkTransformInitialState
            {
                scene = _scene,
                id = id,
                state = state,
                gen = gen
            });
        }

        private void OnInitialState(PlayerID player, NetworkTransformInitialState data, bool asServer)
        {
            if (asServer || data.scene != _scene)
                return;

            if (_factory.TryGetIdentity(_scene, data.id, out var identity) && identity is NetworkTransform nt)
                nt.TryApplyTargetedState(data.state, true, data.gen);
        }

        private void OnUnreliableNack(PlayerID player, NetworkTransformUnreliableNack data, bool asServer)
        {
            // One module per scene receives every NACK message; only the addressed one owns the list.
            if (data.scene != _scene || data.ids.isDisposed || data.ticks.isDisposed)
                return;

            try
            {

                var key = asServer ? player : PlayerID.Server;
                if (!_sendStreams.TryGetValue(key, out var stream))
                    return;

                var localPlayer = GetLocalPlayer();
                bool detached = false;

                for (int i = 0; i < data.ids.Count; i++)
                {
                    var id = data.ids[i];
                    if (i < data.ticks.Count && IsAnsweredNack(stream, id, data.ticks[i]))
                        continue;

                    if (stream.leader == _global && _global != null)
                    {
                        if (!TryGetRegisteredTransform(id, out var resetNt))
                            continue;
                        AddException(stream, resetNt.GetNTIndex(_asServer));
                    }
                    else if (!detached)
                    {
                        Detach(stream);
                        detached = true;
                    }

                    ClearBaseline(stream, id);
                    ClearAdaptive(stream, id);

                    if (stream.pendingInitialized && TryGetRegisteredTransform(id, out var nt) &&
                        IsSendCandidate(nt, key, localPlayer))
                        AddPending(stream, nt);
                }
            }
            finally
            {
                if (!data.ids.isDisposed)
                    data.ids.Dispose();
                if (!data.ticks.isDisposed)
                    data.ticks.Dispose();
            }
        }

        private bool IsAnsweredNack(NTUnreliableSendStream stream, NetworkID nid, ushort tick)
        {
            if (!TryGetRegisteredTransform(nid, out var nt))
                return false;

            int index = nt.GetNTIndex(_asServer);
            var state = stream.leader != null && !stream.IsOverridden(index) ? stream.leader : stream;
            if (index < 0 || index >= state.baselines.Length)
                return false;

            ref var baseline = ref state.baselines[index];
            if (baseline.has)
                return (short)(baseline.tick - tick) > 0;

            return baseline.hasFloor && (short)(baseline.floorTick - tick) >= 0;
        }

        // NACKs are collected per sender and sent once per tick: a spawn burst can leave a
        // receiver missing thousands of transforms at once.
        private void SendNack(PlayerID sender, NetworkID nid, ushort tick)
        {
            var pending = GetRecvStream(sender).pendingNacks;
            if (!pending.TryGetValue(nid, out var queued) || (short)(tick - queued) > 0)
                pending[nid] = tick;
        }

        private void FlushNacks(PlayerID sender, NTUnreliableRecvStream stream)
        {
            if (stream.pendingNacks.Count == 0)
                return;

            var ids = DisposableList<NetworkID>.Create(stream.pendingNacks.Count);
            var ticks = DisposableList<ushort>.Create(stream.pendingNacks.Count);
            foreach (var (id, tick) in stream.pendingNacks)
            {
                ids.Add(id);
                ticks.Add(tick);
            }
            stream.pendingNacks.Clear();

            // Reliable: the NACK is the only per-entry correction against packet-granular acks;
            // losing it while the ack lands wedges a resting object on a phantom baseline.
            _broadcaster.Send(sender, new NetworkTransformUnreliableNack { scene = _scene, ids = ids, ticks = ticks },
                Channel.ReliableUnordered);
            ids.Dispose();
            ticks.Dispose();
        }

        private void SendStandaloneAck(PlayerID sender, NTUnreliableRecvStream stream)
        {
            using var _ = _flushAckMarker.Auto();

            stream.ackDirty = false;
            stream.anchorUnacked = false;
            stream.ackDelayTicks = 0;
            stream.packetsSinceAck = 0;

            var ack = new NetworkTransformUnreliableAck
            {
                scene = _scene,
                seq = stream.latestSeq,
                ackBits = stream.ackBits
            };
            _broadcaster.Send(sender, ack, Channel.Unreliable);
        }

        private void FlushAcks()
        {
            foreach (var (sender, stream) in _recvStreams)
            {
                FlushUnknownNacks(sender, stream);
                FlushBaselineWaits(sender, stream);
                FlushNacks(sender, stream);

                if (!stream.ackDirty)
                    continue;

                if (!ShouldFlushAckAfterTick(stream))
                    continue;

                SendStandaloneAck(sender, stream);
            }
        }

        // A transform that never appears (despawned before it was spawned here) is forgotten
        // after this many flushes without news; the sender has stopped writing it by then.
        internal const int UNKNOWN_IDLE_FLUSHES = 900;
        private readonly List<NetworkID> _unknownKeys = new();

        private const int UNKNOWN_KEPT_ENTRIES = 12;
        private const int UNKNOWN_KEPT_BITS = 16384;

        private static readonly System.Comparison<NTUnknownEntry> _unknownOrder = (a, b) =>
            a.tick == b.tick ? a.order.CompareTo(b.order) : (short)(a.tick - b.tick);

        private static void RememberUnknown(NTUnreliableRecvStream stream, BitPacker packet, int bodyEnd,
            NetworkID nid, bool isAbsolute, byte gen, int dist, long order, ushort tick,
            Dictionary<NetworkID, NTUnknownWait> waiting = null)
        {
            waiting ??= stream.unknownWaiting;
            if (!waiting.TryGetValue(nid, out var wait))
            {
                wait = new NTUnknownWait();
                waiting.Add(nid, wait);
            }

            wait.fresh = true;
            wait.idle = 0;

            var body = wait.body ??= BitPackerPool.Get();

            // An absolute supersedes everything before it; older entries are only kept while
            // there is no newer absolute to start the replay from.
            if (isAbsolute)
            {
                wait.entries.Clear();
                body.ResetPositionAndMode(false);
            }
            else if (body.positionInBits > UNKNOWN_KEPT_BITS)
            {
                wait.entries.Clear();
                body.ResetPositionAndMode(false);
            }
            else if (wait.entries.Count >= UNKNOWN_KEPT_ENTRIES)
            {
                wait.entries.RemoveAt(0);
            }

            int start = body.positionInBits;
            for (int left = bodyEnd - packet.positionInBits; left > 0;)
            {
                int n = left > 64 ? 64 : left;
                body.WriteBits(packet.ReadBits((byte)n), (byte)n);
                left -= n;
            }

            wait.entries.Add(new NTUnknownEntry
            {
                tick = tick,
                gen = gen,
                order = order,
                absolute = isAbsolute,
                dist = dist,
                start = start
            });
        }

        // Transforms that were not spawned yet when entries for them arrived. Once one exists, the
        // kept entries are replayed in tick order, as if they had arrived after the spawn; it is
        // NACKed only if the newest of them still cannot be recorded. Undecided in the tick the
        // entries arrived, since spawns are created after this flush.
        private void FlushUnknownNacks(PlayerID sender, NTUnreliableRecvStream stream)
        {
            var waiting = stream.unknownWaiting;
            if (waiting.Count == 0)
                return;

            _unknownKeys.Clear();
            foreach (var key in waiting.Keys)
                _unknownKeys.Add(key);

            for (int i = 0; i < _unknownKeys.Count; i++)
            {
                var nid = _unknownKeys[i];
                var wait = waiting[nid];

                if (_factory.TryGetIdentity(_scene, nid, out var identity) && identity is NetworkTransform nt)
                {
                    ReplayUnknown(sender, nid, nt, wait);
                    wait.body?.Dispose();
                    waiting.Remove(nid);
                    continue;
                }

                if (wait.fresh)
                {
                    wait.fresh = false;
                    continue;
                }

                if (++wait.idle > UNKNOWN_IDLE_FLUSHES)
                {
                    wait.body?.Dispose();
                    waiting.Remove(nid);
                }
            }
        }

        // A shared anchor the receiver lost is resent once the sender sees the loss; deltas against it
        // wait for that repair for a few flushes and are replayed then, and only NACKed if it
        // never comes.
        private const int BASELINE_WAIT_FLUSHES = 8;

        private void FlushBaselineWaits(PlayerID sender, NTUnreliableRecvStream stream)
        {
            var waiting = stream.baselineWaiting;
            if (waiting.Count == 0)
                return;

            _unknownKeys.Clear();
            foreach (var key in waiting.Keys)
                _unknownKeys.Add(key);

            for (int i = 0; i < _unknownKeys.Count; i++)
            {
                var nid = _unknownKeys[i];
                var wait = waiting[nid];
                var entries = wait.entries;

                if (entries.Count == 0 || !_factory.TryGetIdentity(_scene, nid, out var identity) ||
                    identity is not NetworkTransform nt)
                {
                    wait.body?.Dispose();
                    waiting.Remove(nid);
                    continue;
                }

                entries.Sort(_unknownOrder);
                bool newestRecorded = false;
                for (int e = 0; e < entries.Count; e++)
                    newestRecorded = TryApplyLateEntry(nt, wait.body, entries[e]);

                if (!newestRecorded && ++wait.idle <= BASELINE_WAIT_FLUSHES)
                    continue;

                var newest = entries[entries.Count - 1].tick;
                if (!newestRecorded && !(nt.unreliableRecvHistory?.HasNewer(newest) ?? false))
                    SendNack(sender, nid, newest);

                wait.body?.Dispose();
                waiting.Remove(nid);
            }
        }

        private void ReplayUnknown(PlayerID sender, NetworkID nid, NetworkTransform nt, NTUnknownWait wait)
        {
            var entries = wait.entries;
            if (entries.Count == 0)
                return;

            entries.Sort(_unknownOrder);

            bool newestRecorded = false;
            for (int e = 0; e < entries.Count; e++)
                newestRecorded = TryApplyLateEntry(nt, wait.body, entries[e]);

            if (!newestRecorded)
                SendNack(sender, nid, entries[entries.Count - 1].tick);
        }

        private bool TryApplyLateEntry(NetworkTransform nt, BitPacker body, in NTUnknownEntry entry)
        {
            try
            {
                body.ResetPositionAndMode(true);
                body.SetBitPosition(entry.start);

                NetworkTransformState state;
                NetworkTransformVelocity velocity = default;
                byte gen = entry.gen;
                var history = nt.unreliableRecvHistory ??= new NTRecvHistory();

                if (entry.absolute)
                {
                    state = nt.ReadAbsoluteState(body);
                }
                else
                {
                    if (entry.dist <= 0 || !history.TryGet((ushort)(entry.tick - entry.dist), out var baseline,
                            out var baseVel, out gen))
                        return false;

                    var predicted = NTUnreliable.GetDeltaPrediction(baseline, baseVel, entry.dist);
                    state = nt.ReadDeltaState(body, baseline, predicted);
                    velocity = NetworkTransformVelocity.Derive(baseline, state, entry.dist);
                }

                NetworkIdentity frameParent = null;
                if (state.frame == NetworkTransformFrame.LocalIdentity)
                    _factory.TryGetIdentity(_scene, state.parentId, out frameParent);

                if (!nt.TryApplyUnreliableState(state, gen, entry.order, entry.tick, frameParent, entry.absolute))
                    return false;

                history.Store(entry.tick, state, velocity, gen);
                return true;
            }
            catch (System.Exception exception) when (exception is System.IndexOutOfRangeException or
                                                      System.ArgumentOutOfRangeException or
                                                      System.OverflowException)
            {
                return false;
            }
        }

        private PlayerID GetLocalPlayer()
        {
            if (_manager.TryGetModule<PlayersManager>(false, out var _players))
                return _players.localPlayerId.GetValueOrDefault();
            return PlayerID.Server;
        }

        private bool IsSendCandidate(NetworkTransform nt, PlayerID player, PlayerID localPlayer)
        {
            if (!nt || !nt.IsSpawned(_asServer) || !nt.id.HasValue)
                return false;

            if (player == PlayerID.Server)
                return nt.IsControlling(localPlayer, false);

            return !nt.IsControlling(player, false) && nt.IsObserver(player);
        }

        private NTUnreliableSendStream PrepareSendStream(PlayerID player, PlayerID localPlayer)
        {
            var stream = GetSendStream(player);
            if (stream.pendingInitialized)
                return stream;

            stream.pendingInitialized = true;

            for (int i = 0; i < _networkTransforms.Count; i++)
            {
                var nt = _networkTransforms[i];
                if (IsSendCandidate(nt, player, localPlayer))
                    AddPending(stream, nt);
            }

            return stream;
        }

        private void QueueChangedStates(List<NetworkTransform> changed, PlayerID localPlayer)
        {
            using var _ = _queueChangedMarker.Auto();

            if (_global != null && changed.Count > 0)
            {
                var additions = ListPool<NetworkTransform>.Instantiate();
                for (int i = 0; i < changed.Count; i++)
                {
                    if (!_global.IsPending(changed[i]))
                        additions.Add(changed[i]);
                }

                if (additions.Count > 0)
                    MergePending(_global, additions);
                ListPool<NetworkTransform>.Destroy(additions);

                QueueChangedExceptions(changed);
            }

            foreach (var (player, stream) in _sendStreams)
            {
                if (!stream.pendingInitialized || stream.leader != null)
                    continue;

                // Binary insertion is cheaper for tiny changesets. Larger changesets are
                // already NetworkID-sorted by the gather loop, so merge them in linear time.
                if (changed.Count <= 4)
                {
                    for (int i = 0; i < changed.Count; i++)
                    {
                        var nt = changed[i];
                        if (IsSendCandidate(nt, player, localPlayer))
                            AddPending(stream, nt);
                    }
                    continue;
                }

                // A continuously moving transform normally remains pending while its previous
                // revision is in flight. In that common benchmark/gameplay case every changed ID
                // is already present, so avoid allocating and copying the same full list again.
                if (stream.pending.Count >= _networkTransforms.Count ||
                    !HasMissingSendCandidate(stream, changed, player, localPlayer))
                    continue;

                var additions = ListPool<NetworkTransform>.Instantiate();
                try
                {
                    for (int i = 0; i < changed.Count; i++)
                    {
                        var nt = changed[i];
                        if (IsSendCandidate(nt, player, localPlayer))
                            additions.Add(nt);
                    }

                    MergePending(stream, additions);
                }
                finally
                {
                    ListPool<NetworkTransform>.Destroy(additions);
                }
            }
        }

        private bool HasMissingSendCandidate(NTUnreliableSendStream stream, List<NetworkTransform> changed,
            PlayerID player, PlayerID localPlayer)
        {
            for (int changedIndex = 0; changedIndex < changed.Count; changedIndex++)
            {
                var nt = changed[changedIndex];
                if (!stream.IsPending(nt) && IsSendCandidate(nt, player, localPlayer))
                    return true;
            }

            return false;
        }

        internal static void MergePending(NTUnreliableSendStream stream, List<NetworkTransform> additions)
        {
            if (additions.Count == 0)
                return;

            for (int i = 0; i < additions.Count; i++)
                stream.SetPending(additions[i], true);

            if (stream.pending.Count == 0)
            {
                stream.pending.AddRange(additions);
                return;
            }

            var merged = ListPool<NetworkTransform>.Instantiate();
            try
            {
                int pendingIndex = 0;
                int additionIndex = 0;

                while (pendingIndex < stream.pending.Count && additionIndex < additions.Count)
                {
                    var pending = stream.pending[pendingIndex];
                    var addition = additions[additionIndex];
                    var pendingId = pending.id!.Value;
                    var additionId = addition.id!.Value;

                    if (pendingId.Equals(additionId))
                    {
                        merged.Add(addition);
                        pendingIndex++;
                        additionIndex++;
                    }
                    else if (pendingId < additionId)
                    {
                        merged.Add(pending);
                        pendingIndex++;
                    }
                    else
                    {
                        merged.Add(addition);
                        additionIndex++;
                    }
                }

                while (pendingIndex < stream.pending.Count)
                    merged.Add(stream.pending[pendingIndex++]);
                while (additionIndex < additions.Count)
                    merged.Add(additions[additionIndex++]);

                stream.pending.Clear();
                stream.pending.AddRange(merged);
            }
            finally
            {
                ListPool<NetworkTransform>.Destroy(merged);
            }
        }

        // Worst-case bits for one entry's framing (len delta + nid delta).
        private const int ENTRY_HEADER_BITS = 128;
        // Wrapper overhead: broadcast type hash + scene + seq + ByteData length prefix.
        private const int UNRELIABLE_PACKET_OVERHEAD = 32;

        internal static long CalculateBudgetBits(int transportMtu)
        {
            long payloadBytes = (long)transportMtu - UNRELIABLE_PACKET_OVERHEAD;
            if (payloadBytes < 128)
                payloadBytes = 128;
            return payloadBytes * 8L;
        }

        private struct NTEntryPlan
        {
            public bool absolute;
            public bool sameDist;
            public int dist;
            public byte gen;
            public BitPacker stateBits;
            public int bits;
        }

        private static void WriteEntry(BitPacker packer, in NTEntryPlan plan)
        {
            if (plan.absolute)
            {
                packer.WriteBits(1, 1);
                Packer<byte>.Write(packer, plan.gen);
            }
            else
            {
                packer.WriteBits(0, 1);

                if (plan.sameDist)
                {
                    packer.WriteBits(1, 1);
                }
                else
                {
                    packer.WriteBits(0, 1);
                    packer.WriteBits((ulong)(plan.dist - 1), NTUnreliable.DISTANCE_BITS);
                }
            }

            packer.WriteBitsWithoutConsumingIt(plan.stateBits, plan.stateBits.positionInBits);
        }

        private static NTWriteResult TryWriteEntry(NetworkTransform nt, NTUnreliableSendStream stream,
            ushort currentTick, int lastDist, out NTEntryPlan plan, out int newLastDist,
            out NetworkTransformVelocity velocity, out byte gen, out uint genEpoch, bool force = false)
        {
            plan = default;
            newLastDist = lastDist;
            velocity = default;
            var nid = nt.ntNid;
            var generation = stream.generationOverrides.Count > 0 &&
                             stream.generationOverrides.TryGetValue(nid, out var overridden)
                ? overridden
                : new NTUnreliableGeneration { gen = nt.sendGen, epoch = nt.sendGenEpoch };
            gen = generation.gen;
            genEpoch = generation.epoch;

            ref var baseline = ref stream.baselines[nt.GetNTIndex(stream.asServer)];
            bool hasAcked = baseline.has && baseline.genEpoch == genEpoch;

            ref readonly var current = ref nt.capturedState;
            NTLastAdaptiveWrite lastWrite = null;
            bool hasLastWrite = nt.hasSyncStrategy && (lastWrite = stream.GetAdaptive(nt)) != null;

            // Suppression must not depend on baseline age — a resting object's baseline never
            // refreshes, and re-sending absolutes for it every 32 packets floods static scenes.
            if (!force && hasAcked && baseline.revision == nt.capturedRevision &&
                (!nt.hasSyncStrategy || !hasLastWrite ||
                 (lastWrite.restConfirmed && lastWrite.redundancy == 0)))
                return NTWriteResult.SkipAcked;

            if (nt.hasSyncStrategy)
            {
                bool breakWrite = false;
                bool brokeEarly = false;
                bool scheduledProbe = false;
                byte breakRedundancy = nt.activeStrategy?.breakRedundancy ?? NTUnreliable.BREAK_REDUNDANCY;

                if (hasLastWrite)
                {
                    int sinceLastWrite = (short)(currentTick - lastWrite.tick);
                    bool resting = lastWrite.revision == nt.capturedRevision;
                    bool restGate = !resting || lastWrite.restConfirmed;

                    if (sinceLastWrite >= 1 && restGate)
                    {
                        int spacing = nt.adaptiveSendSpacing;
                        int refreshInterval = lastWrite.refreshInterval >= 2 && lastWrite.refreshInterval < spacing
                            ? lastWrite.refreshInterval
                            : spacing;

                        bool canSkip = sinceLastWrite < spacing &&
                                       nt.CanSkipCached(lastWrite, currentTick, current);
                        bool refreshDue = ((uint)currentTick + (uint)nid.GetHashCode()) %
                            (uint)refreshInterval == 0;

                        // Baselines only move on anchor ticks, so an anchor must carry a resting
                        // transform that has not completed yet, or a baseline that is getting old.
                        bool anchorDue = NTUnreliable.IsAnchorTick(currentTick) &&
                                         (!hasAcked || resting ||
                                          (short)(currentTick - baseline.tick) >= NTUnreliable.ANCHOR_REFRESH_AGE);

                        if (!force && canSkip && !refreshDue && !anchorDue && (lastWrite.redundancy == 0 ||
                                                       sinceLastWrite < NTUnreliable.REDUNDANCY_INTERVAL))
                            return NTWriteResult.Hold;

                        breakWrite = !canSkip && lastWrite.redundancy == 0 &&
                                     sinceLastWrite < spacing &&
                                     sinceLastWrite > breakRedundancy;

                        brokeEarly = !canSkip && sinceLastWrite < spacing;
                        scheduledProbe = canSkip && refreshDue;
                    }
                }

                if (!hasLastWrite)
                {
                    lastWrite = NTLastAdaptiveWrite.Rent();
                    lastWrite.tick = currentTick;
                    lastWrite.state = current;
                    lastWrite.revision = nt.capturedRevision;
                    stream.SetAdaptive(nt, lastWrite);
                }
                else if (lastWrite.tick != currentTick)
                {
                    lastWrite = Own(stream, nt, lastWrite);
                    bool sameAsPrev = lastWrite.revision == nt.capturedRevision;
                    bool restBreak = sameAsPrev && !lastWrite.restConfirmed;
                    int spacing = nt.adaptiveSendSpacing;

                    if (brokeEarly)
                    {
                        int observed = (short)(currentTick - lastWrite.tick) - 1;
                        lastWrite.refreshInterval = (byte)(observed < 2 ? 2 : observed > spacing ? spacing : observed);
                    }
                    else if (scheduledProbe && lastWrite.refreshInterval >= 2 && lastWrite.refreshInterval < spacing)
                    {
                        lastWrite.refreshInterval++;
                    }

                    lastWrite.prevPrevTick = lastWrite.prevTick;
                    lastWrite.prevPrevState = lastWrite.prevState;
                    lastWrite.hasPrevPrev = lastWrite.hasPrev;

                    lastWrite.prevTick = lastWrite.tick;
                    lastWrite.prevState = lastWrite.state;
                    lastWrite.hasPrev = true;

                    lastWrite.tick = currentTick;
                    lastWrite.state = current;
                    lastWrite.revision = nt.capturedRevision;
                    lastWrite.restConfirmed = sameAsPrev;
                    lastWrite.redundancy = breakWrite || restBreak
                        ? breakRedundancy
                        : (byte)(lastWrite.redundancy > 0 ? lastWrite.redundancy - 1 : 0);
                }
            }

            int tickDist = hasAcked ? (short)(currentTick - baseline.tick) : 0;
            int dist = tickDist;
            bool canDelta = hasAcked && tickDist >= 1 && tickDist < NTUnreliable.MAX_BASELINE_AGE &&
                            nt.CanDeltaAgainst(baseline.state);

            var cache = nt.unreliableEncodeCache ??= new NTEncodeCache();
            cache.BeginTick(currentTick);

            if (canDelta)
            {
                gen = baseline.gen;
                genEpoch = baseline.genEpoch;
                plan.sameDist = dist == lastDist;
                plan.dist = dist;

                if (!plan.sameDist)
                    newLastDist = dist;

                if (!cache.TryGetDelta(baseline.tick, baseline.velocity, out var stateBits, out velocity))
                {
                    var predicted = NTUnreliable.GetDeltaPrediction(baseline.state, baseline.velocity, tickDist);
                    stateBits = cache.ClaimDeltaSlot(baseline.tick, baseline.velocity);
                    nt.WriteDeltaState(stateBits, baseline.state, predicted);
                    velocity = NetworkTransformVelocity.Derive(baseline.state, current, tickDist);
                    cache.CompleteDeltaSlot(velocity);
                }

                plan.stateBits = stateBits;
                plan.bits = 2 + (plan.sameDist ? 0 : NTUnreliable.DISTANCE_BITS) + stateBits.positionInBits;
            }
            else
            {
                plan.absolute = true;
                plan.gen = gen;
                var stateBits = cache.GetAbsolute(nt);
                plan.stateBits = stateBits;
                plan.bits = 9 + stateBits.positionInBits;

                if (nt.hasSyncStrategy && lastWrite != null)
                {
                    lastWrite = Own(stream, nt, lastWrite);
                    lastWrite.tick = currentTick;
                    lastWrite.state = current;
                    lastWrite.revision = nt.capturedRevision;
                    lastWrite.hasPrev = false;
                    lastWrite.hasPrevPrev = false;
                    lastWrite.restConfirmed = true;
                    lastWrite.redundancy = 0;
                    lastWrite.refreshInterval = 0;
                }
            }

            return NTWriteResult.Written;
        }

        internal static NTLastAdaptiveWrite Own(NTUnreliableSendStream stream, NetworkTransform nt,
            NTLastAdaptiveWrite write)
        {
            if (write.refs <= 1)
                return write;

            var owned = NTLastAdaptiveWrite.Rent();
            owned.CopyFrom(write);
            stream.SetAdaptive(nt, owned);
            return owned;
        }

        private void FlushUnreliablePacket(PlayerID player, NTUnreliableSendStream stream, BitPacker packer,
            List<NTUnreliableEntry> pending, int countPos, int writtenCount)
        {
            var lastPos = packer.positionInBits;
            packer.SetBitPosition(countPos);
            Packer<int>.Write(packer, writtenCount);
            packer.SetBitPosition(lastPos);

            CommitPacket(player, stream, packer, pending);
            packer.Dispose();
        }

        private void CommitPacket(PlayerID player, NTUnreliableSendStream stream, BitPacker packer,
            List<NTUnreliableEntry> pending, List<NetworkID> excluded = null, bool patch = false)
        {
            using var _ = _commitMarker.Auto();
            ushort seq = stream.nextSeq;
            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (slot.entries != null)
                NTSharedEntries.Release(slot.entries);
            if (slot.excluded != null)
                ListPool<NetworkID>.Destroy(slot.excluded);
            bool keepEntries = stream != _global;
            if (keepEntries)
                NTSharedEntries.Retain(pending);
            slot = new NTUnreliableSlot
            {
                used = true,
                anchor = NTUnreliable.IsAnchorTick(_currentTick),
                seq = seq,
                order = stream.nextOrder,
                tick = _currentTick,
                entries = keepEntries ? pending : null,
                patch = patch,
                excluded = CopyExcluded(excluded)
            };

            if (slot.anchor && stream.asServer)
            {
                NTSharedEntries.Retain(pending);
                stream.deferredAnchors.Add(new NTDeferredAnchor
                {
                    entries = pending,
                    patch = patch,
                    excluded = CopyExcluded(excluded),
                    order = stream.nextOrder,
                    seq = seq,
                    tick = _currentTick
                });
            }

            stream.nextSeq += 1;
            stream.nextOrder += 1;

            // The shared stream only encodes; peers send its packets under their own sequence.
            if (stream == _global)
                return;

            var delta = new NetworkTransformUnreliableDelta(_scene, seq, _currentTick, packer) { patch = patch };

            if (_recvStreams.TryGetValue(player, out var recv) && recv.ackInit && recv.ackDirty)
            {
                recv.ackDirty = false;
                recv.anchorUnacked = false;
                recv.ackDelayTicks = 0;
                recv.packetsSinceAck = 0;
                delta.ack = new NetworkTransformUnreliableAckHeader
                {
                    seq = recv.latestSeq,
                    ackBits = recv.ackBits
                };
            }

            _broadcaster.Send(player, delta, Channel.Unreliable);
        }

        private static List<NetworkID> CopyExcluded(List<NetworkID> excluded)
        {
            if (excluded == null || excluded.Count == 0)
                return null;

            var copy = ListPool<NetworkID>.Instantiate();
            copy.AddRange(excluded);
            return copy;
        }

        private struct NTShareKey
        {
            public NetworkID nid;
            public ushort tick;
            public byte gen;
            public byte mode;
            public uint genEpoch;
            public NetworkTransformVelocity velocity;
            public byte writeFlags;
            public byte writeRefresh;
            public byte writeRedundancy;
            public ushort writeTick;
            public ushort writePrevTick;
            public ushort writePrevPrevTick;
            public uint writeRevision;
        }

        private sealed class NTSharePacket
        {
            public long budgetBits;
            public int keyCount;
            public int next;
            public NTShareKey[] keys = new NTShareKey[64];
            public BitPacker packet;
            public List<NTUnreliableEntry> entries;
            public NTUnreliableSendStream origin;
            public int index;
            public readonly List<(int index, NTLastAdaptiveWrite write)> adaptive = new();
        }

        private readonly List<NTSharePacket> _sharePackets = new();
        private int _sharePacketCount;
        private NTShareKey[] _shareKeys = new NTShareKey[64];
        private int[] _shareHead = new int[64];
        private int[] _shareHeadPass = new int[64];
        private int _sharePass = 1;

        private static bool KeyEquals(in NTShareKey a, in NTShareKey b)
        {
            return a.mode == b.mode && a.tick == b.tick && a.gen == b.gen &&
                   a.genEpoch == b.genEpoch && a.nid.Equals(b.nid) &&
                   a.velocity.posX == b.velocity.posX && a.velocity.posY == b.velocity.posY &&
                   a.velocity.posZ == b.velocity.posZ && a.velocity.rotX == b.velocity.rotX &&
                   a.velocity.rotY == b.velocity.rotY && a.velocity.rotZ == b.velocity.rotZ &&
                   a.velocity.rotW == b.velocity.rotW && a.velocity.scaleX == b.velocity.scaleX &&
                   a.velocity.scaleY == b.velocity.scaleY && a.velocity.scaleZ == b.velocity.scaleZ &&
                   a.writeFlags == b.writeFlags && a.writeTick == b.writeTick &&
                   a.writePrevTick == b.writePrevTick && a.writePrevPrevTick == b.writePrevPrevTick &&
                   a.writeRevision == b.writeRevision && a.writeRefresh == b.writeRefresh &&
                   a.writeRedundancy == b.writeRedundancy;
        }

        private bool TryBuildShareKeys(NTUnreliableSendStream stream, ushort currentTick, out int keyCount)
        {
            keyCount = 0;

            if (stream.generationOverrides.Count > 0)
                return false;

            var pending = stream.pending;
            if (_shareKeys.Length < pending.Count)
                System.Array.Resize(ref _shareKeys, System.Math.Max(pending.Count, _shareKeys.Length * 2));

            for (int i = 0; i < pending.Count;)
            {
                var nt = pending[i];
                if (!IsLive(nt))
                {
                    pending.RemoveAt(i);
                    continue;
                }

                if (nt.adaptiveDebugDumpEnabled)
                    return false;

                var nid = nt.ntNid;
                ref var baseline = ref stream.baselines[nt.GetNTIndex(stream.asServer)];
                bool hasAcked = baseline.has && baseline.genEpoch == nt.sendGenEpoch;

                NTLastAdaptiveWrite lastWrite = null;
                bool hasLastWrite = nt.hasSyncStrategy && (lastWrite = stream.GetAdaptive(nt)) != null;

                if (hasAcked && baseline.revision == nt.capturedRevision &&
                    (!nt.hasSyncStrategy || !hasLastWrite ||
                     (lastWrite.restConfirmed && lastWrite.redundancy == 0)))
                {
                    pending.RemoveAt(i);
                    stream.SetPending(nt, false);
                    continue;
                }

                int tickDist = hasAcked ? (short)(currentTick - baseline.tick) : 0;
                bool canDelta = hasAcked && tickDist >= 1 && tickDist < NTUnreliable.MAX_BASELINE_AGE &&
                                nt.CanDeltaAgainst(baseline.state);

                ref var key = ref _shareKeys[keyCount++];
                key.nid = nid;

                if (canDelta)
                {
                    key.mode = 1;
                    key.tick = baseline.tick;
                    key.gen = baseline.gen;
                    key.genEpoch = baseline.genEpoch;
                    key.velocity = baseline.velocity;
                }
                else
                {
                    key.mode = 2;
                    key.tick = 0;
                    key.gen = nt.sendGen;
                    key.genEpoch = nt.sendGenEpoch;
                    key.velocity = default;
                }

                if (hasLastWrite)
                {
                    key.writeFlags = (byte)(1 | (lastWrite.hasPrev ? 2 : 0) | (lastWrite.hasPrevPrev ? 4 : 0) |
                                            (lastWrite.restConfirmed ? 8 : 0));
                    key.writeTick = lastWrite.tick;
                    key.writePrevTick = lastWrite.prevTick;
                    key.writePrevPrevTick = lastWrite.prevPrevTick;
                    key.writeRevision = lastWrite.revision;
                    key.writeRefresh = lastWrite.refreshInterval;
                    key.writeRedundancy = lastWrite.redundancy;
                }
                else
                {
                    key.writeFlags = 0;
                    key.writeTick = 0;
                    key.writePrevTick = 0;
                    key.writePrevPrevTick = 0;
                    key.writeRevision = 0;
                    key.writeRefresh = 0;
                    key.writeRedundancy = 0;
                }

                i++;
            }

            return true;
        }

        // A packet is self-contained (its dist/nid/length chains restart) and names baselines by
        // tick, so any packet another stream wrote this tick from the same entry keys is
        // byte-identical for this stream.
        private NTSharePacket FindSharePacket(NTUnreliableSendStream stream, int start, int keyCount)
        {
            int head = stream.pending[start].GetNTIndex(_asServer);
            if (head >= _shareHeadPass.Length || _shareHeadPass[head] != _sharePass)
                return null;

            for (int p = _shareHead[head]; p >= 0;)
            {
                var record = _sharePackets[p];
                p = record.next;

                if (record.budgetBits != stream.budgetBits || start + record.keyCount > keyCount)
                    continue;

                bool equal = true;
                for (int i = 0; i < record.keyCount && equal; i++)
                    equal = KeyEquals(record.keys[i], _shareKeys[start + i]);

                if (equal)
                    return record;
            }

            return null;
        }

        private void RecordSharePacket(NTUnreliableSendStream stream, int start, int end,
            BitPacker packet, List<NTUnreliableEntry> entries, List<(int index, NTLastAdaptiveWrite write)> adaptive)
        {
            if (_sharePacketCount == _sharePackets.Count)
                _sharePackets.Add(new NTSharePacket());

            int index = _sharePacketCount++;
            var record = _sharePackets[index];
            record.origin = stream;
            record.index = index;
            stream.tickPackets.Add(index);
            stream.tickFresh = true;
            int keyCount = end - start;
            record.budgetBits = stream.budgetBits;
            record.keyCount = keyCount;
            record.packet = packet;
            record.entries = entries;
            NTSharedEntries.Retain(entries);
            record.adaptive.AddRange(adaptive);

            if (record.keys.Length < keyCount)
                record.keys = new NTShareKey[System.Math.Max(keyCount, record.keys.Length * 2)];

            System.Array.Copy(_shareKeys, start, record.keys, 0, keyCount);

            int head = stream.pending[start].GetNTIndex(_asServer);
            if (head >= _shareHeadPass.Length)
            {
                int size = System.Math.Max(head + 1, _shareHeadPass.Length * 2);
                System.Array.Resize(ref _shareHead, size);
                System.Array.Resize(ref _shareHeadPass, size);
            }

            record.next = _shareHeadPass[head] == _sharePass ? _shareHead[head] : -1;
            _shareHead[head] = index;
            _shareHeadPass[head] = _sharePass;
        }

        private void ReleaseShareGroups()
        {
            for (int p = 0; p < _sharePacketCount; p++)
            {
                var record = _sharePackets[p];
                record.packet.Dispose();
                record.packet = null;
                NTSharedEntries.Release(record.entries);
                record.entries = null;
                record.origin = null;
                record.adaptive.Clear();
            }

            _sharePacketCount = 0;
            _sharePass++;
        }

        private void ReplaySharePacket(PlayerID player, NTUnreliableSendStream stream, NTSharePacket record)
        {
            using var _ = _replayMarker.Auto();

            entriesWrittenCount += record.entries.Count;
            sharedPacketCount++;
            CommitPacket(player, stream, record.packet, record.entries);

            int capacity = stream.adaptiveByIndex.Length;
            for (int a = 0; a < record.adaptive.Count; a++)
            {
                var (index, write) = record.adaptive[a];
                if (index >= 0 && index < capacity)
                    stream.SetAdaptiveAt(index, write);
            }
        }

        private readonly List<(int index, NTLastAdaptiveWrite write)> _packetAdaptive = new();

        // Writes one packet starting at pending[start]. Stops when the budget is full or when the
        // next entry starts a packet another stream already wrote with matching keys, so streams
        // that diverged on a few entries fall back into step with the shared packets.
        private int EncodeSharePacket(PlayerID player, NTUnreliableSendStream stream, int start, int keyCount)
        {
            _prepareUnreliableMarker.Begin();

            long budgetBits = stream.budgetBits;
            BitPacker packer = null;
            List<NTUnreliableEntry> pending = null;
            int countPos = 0;
            int writtenCount = 0;
            int lastDist = 0;
            NetworkID lastNid = default;
            PackedInt lastLen = default;
            _packetAdaptive.Clear();

            int i = start;
            for (; i < keyCount; i++)
            {
                if (i > start && FindSharePacket(stream, i, keyCount) != null)
                    break;

                var nt = stream.pending[i];
                var nid = nt.ntNid;
                var writeResult = TryWriteEntry(nt, stream, _currentTick, lastDist, out var plan, out var newLastDist,
                    out var velocity, out var wireGen, out var wireGenEpoch);

                if (writeResult == NTWriteResult.Hold)
                {
                    adaptiveHoldCount++;
                    continue;
                }

                if (writeResult != NTWriteResult.Written)
                    continue;

                int entryBits = plan.bits;

                // Re-encoded as the first entry of the next packet; TryWriteEntry is idempotent within a tick.
                if (writtenCount > 0 && packer!.positionInBits + entryBits + ENTRY_HEADER_BITS > budgetBits)
                    break;

                entriesWrittenCount++;

                if (packer == null)
                {
                    packer = BitPackerPool.Get();
                    pending = NTSharedEntries.Rent();
                    countPos = packer.positionInBits;
                    Packer<int>.Write(packer, 0);
                }

                PackedInt length = entryBits;

                DeltaPacker<PackedInt>.Write(packer, lastLen, length);
                lastLen = length;
                DeltaPacker<NetworkID>.Write(packer, lastNid, nid);
                WriteEntry(packer, plan);

                lastNid = nid;
                lastDist = newLastDist;
                writtenCount += 1;
                if (plan.absolute)
                    ((NTSharedEntries)pending).hasAbsolute = true;
                pending.Add(new NTUnreliableEntry
                {
                    nid = lastNid,
                    state = nt.capturedState,
                    velocity = velocity,
                    tick = _currentTick,
                    gen = wireGen,
                    genEpoch = wireGenEpoch,
                    revision = nt.capturedRevision,
                    life = nt.ntLife,
                    transform = nt
                });

                if (nt.hasSyncStrategy && stream.GetAdaptive(nt) is { } written)
                    _packetAdaptive.Add((nt.GetNTIndex(stream.asServer), written));
            }

            if (writtenCount > 0)
            {
                var lastPos = packer.positionInBits;
                packer.SetBitPosition(countPos);
                Packer<int>.Write(packer, writtenCount);
                packer.SetBitPosition(lastPos);

                CommitPacket(player, stream, packer, pending);
                RecordSharePacket(stream, start, i, packer, pending, _packetAdaptive);
            }

            _prepareUnreliableMarker.End();
            return i;
        }

        // ---- Followers --------------------------------------------------------------------
        // A peer whose whole send state equals another peer's (baselines, adaptive history,
        // pending set, deferred anchors) follows it: it keeps no per-transform state of its own
        // and resends the leader's packets each tick under its own seq/ack window. Anything that
        // would make the two differ first copies the leader's state back into the follower.

        private const int FOLLOW_RETRY_TICKS = 32;

        private void Detach(NTUnreliableSendStream stream)
        {
            if (stream.leader != null)
            {
                MaterializeFollower(stream);
                return;
            }

            if (stream.followers.Count == 0)
                return;

            // The leader is about to diverge: hand its state to one follower and let the rest
            // follow that one, instead of copying the state into every follower.
            var heir = stream.followers[0];
            MaterializeFollower(heir);
            for (int i = stream.followers.Count - 1; i >= 0; i--)
            {
                var follower = stream.followers[i];
                follower.leader = heir;
                heir.followers.Add(follower);
            }

            stream.followers.Clear();
        }

        private void DetachAll()
        {
            foreach (var stream in _sendStreams.Values)
            {
                if (stream.leader != null)
                    MaterializeFollower(stream);
            }
        }

        private void MaterializeFollower(NTUnreliableSendStream follower)
        {
            if (follower.leader == _global && _global != null)
            {
                for (int i = 0; i < _byIndex.Count; i++)
                    AddException(follower, i);
                return;
            }

            var leader = follower.leader;
            leader.followers.Remove(follower);
            follower.leader = null;

            follower.EnsureBaselineCapacity(leader.baselines.Length);
            int n = leader.baselines.Length;
            System.Array.Copy(leader.baselines, follower.baselines, n);
            System.Array.Copy(leader.pendingByIndex, follower.pendingByIndex, n);
            for (int i = n; i < follower.baselines.Length; i++)
            {
                follower.baselines[i] = default;
                follower.pendingByIndex[i] = false;
            }

            for (int i = 0; i < follower.adaptiveByIndex.Length; i++)
                follower.SetAdaptiveAt(i, i < n ? leader.adaptiveByIndex[i] : null);

            follower.pending.Clear();
            follower.pending.AddRange(leader.pending);
            follower.pendingInitialized = leader.pendingInitialized;
            follower.followRetryTick = (ushort)(_currentTick + FOLLOW_RETRY_TICKS);
        }

        // Membership changes are identical for a follower and its leader unless the peers see the
        // transform differently (ownership, visibility); then the follower must split first.
        private bool KeepsFollowing(NetworkTransform nt, PlayerID player, PlayerID leaderPlayer,
            PlayerID localPlayer)
        {
            return IsSendCandidate(nt, player, localPlayer) == IsSendCandidate(nt, leaderPlayer, localPlayer);
        }

        private static bool CanShareState(NTUnreliableSendStream stream)
        {
            return stream.generationOverrides.Count == 0 && stream.pendingInitialized;
        }

        private void TryFollow(NTUnreliableSendStream stream)
        {
            var leader = stream.tickOrigin;
            if (leader == null || leader == stream || stream.tickFresh || stream.tickMixed || !stream.tickShareable ||
                stream.leader != null || stream.followers.Count > 0 || leader.leader != null ||
                !leader.tickShareable || stream.budgetBits != leader.budgetBits ||
                (short)(_currentTick - stream.followRetryTick) < 0 || !CanShareState(stream) || !CanShareState(leader))
                return;

            if (!SameSendState(stream, leader))
            {
                stream.followRetryTick = (ushort)(_currentTick + FOLLOW_RETRY_TICKS);
                return;
            }

            stream.leader = leader;
            leader.followers.Add(stream);
        }

        private static bool SameSendState(NTUnreliableSendStream a, NTUnreliableSendStream b)
        {
            if (a.pending.Count != b.pending.Count || a.deferredAnchors.Count != b.deferredAnchors.Count)
                return false;

            for (int i = 0; i < a.pending.Count; i++)
            {
                if (!ReferenceEquals(a.pending[i], b.pending[i]))
                    return false;
            }

            for (int i = 0; i < a.deferredAnchors.Count; i++)
            {
                if (!ReferenceEquals(a.deferredAnchors[i].entries, b.deferredAnchors[i].entries) ||
                    a.deferredAnchors[i].tick != b.deferredAnchors[i].tick)
                    return false;
            }

            int n = System.Math.Max(a.baselines.Length, b.baselines.Length);
            for (int i = 0; i < n; i++)
            {
                var x = i < a.baselines.Length ? a.baselines[i] : default;
                var y = i < b.baselines.Length ? b.baselines[i] : default;
                if (x.has != y.has || x.hasFloor != y.hasFloor || (x.hasFloor && x.floorTick != y.floorTick))
                    return false;
                if (x.has && (x.tick != y.tick || x.gen != y.gen || x.genEpoch != y.genEpoch ||
                              x.revision != y.revision || !VelocityEquals(x.velocity, y.velocity) ||
                              !x.state.Equals(y.state)))
                    return false;

                bool px = i < a.pendingByIndex.Length && a.pendingByIndex[i];
                bool py = i < b.pendingByIndex.Length && b.pendingByIndex[i];
                if (px != py)
                    return false;

                var wx = i < a.adaptiveByIndex.Length ? a.adaptiveByIndex[i] : null;
                var wy = i < b.adaptiveByIndex.Length ? b.adaptiveByIndex[i] : null;
                if (!ReferenceEquals(wx, wy) && (wx == null || wy == null || !wx.ContentEquals(wy)))
                    return false;
            }

            return true;
        }

        private static bool VelocityEquals(in NetworkTransformVelocity a, in NetworkTransformVelocity b)
        {
            return a.posX == b.posX && a.posY == b.posY && a.posZ == b.posZ &&
                   a.rotX == b.rotX && a.rotY == b.rotY && a.rotZ == b.rotZ && a.rotW == b.rotW &&
                   a.scaleX == b.scaleX && a.scaleY == b.scaleY && a.scaleZ == b.scaleZ;
        }

        // A follower keeps following only if exactly the same anchors become due for it as for its
        // leader this tick; it then drops them, since the leader's adoption already covers it.
        private bool SameDueAnchors(NTUnreliableSendStream follower, NTUnreliableSendStream leader)
        {
            int f = 0;
            int l = 0;
            while (true)
            {
                while (f < follower.deferredAnchors.Count &&
                       (short)(_currentTick - follower.deferredAnchors[f].tick) < _anchorAdoptDelay)
                    f++;
                while (l < leader.deferredAnchors.Count &&
                       (short)(_currentTick - leader.deferredAnchors[l].tick) < _anchorAdoptDelay)
                    l++;

                bool fe = f >= follower.deferredAnchors.Count;
                bool le = l >= leader.deferredAnchors.Count;
                if (fe || le)
                    return fe && le;

                var fa = follower.deferredAnchors[f];
                var la = leader.deferredAnchors[l];
                if (!ReferenceEquals(fa.entries, la.entries))
                    return false;

                var fd = DecideAnchor(follower, fa);
                var ld = DecideAnchor(leader, la);
                if (fd != ld && (fd == NTAnchorDecision.Lost || ld == NTAnchorDecision.Lost ||
                                 AnyCompletes(leader, la.entries)))
                    return false;

                f++;
                l++;
            }
        }

        private void DropDueAnchors(NTUnreliableSendStream stream)
        {
            var deferred = stream.deferredAnchors;
            int writeIndex = 0;
            for (int i = 0; i < deferred.Count; i++)
            {
                var anchor = deferred[i];
                if ((short)(_currentTick - anchor.tick) < _anchorAdoptDelay)
                {
                    deferred[writeIndex++] = anchor;
                    continue;
                }

                if (DecideAnchor(stream, anchor) == NTAnchorDecision.Optimistic)
                    stream.ring[anchor.seq % NTUnreliable.RING_SIZE].optimistic = true;

                NTSharedEntries.Release(anchor.entries);
            }

            deferred.RemoveRange(writeIndex, deferred.Count - writeIndex);
        }

        private void ReplayLeader(PlayerID player, NTUnreliableSendStream stream)
        {
            DropDueAnchors(stream);
            stream.tickPackets.Clear();
            stream.lastSendTick = _currentTick;
            stream.hasSent = true;

            var leader = stream.leader;
            for (int i = 0; i < leader.tickPackets.Count; i++)
            {
                var record = _sharePackets[leader.tickPackets[i]];
                entriesWrittenCount += record.entries.Count;
                sharedPacketCount++;
                CommitPacket(player, stream, record.packet, record.entries);
            }
        }

        // ---- Global fan-out (server) ----------------------------------------------------------
        // One shared stream encodes every transform once per tick and never loses a packet: it
        // adopts each anchor at the common delay. Every peer sends the shared packets under its
        // own sequence. A transform whose baseline a peer lacks (it lost the anchor, NACKed it,
        // just started observing it) is an exception for that peer: excluded from the shared
        // packets' adoption, encoded against the peer's own baseline in a patch packet, and handed
        // back to the shared stream once both baselines match again.

        private NTUnreliableSendStream _global;
        private readonly Dictionary<List<NTUnreliableEntry>, (ushort tick, byte[] bytes, int length)> _repairs = new();
        private readonly List<List<NTUnreliableEntry>> _repairsStale = new();
        private readonly Stack<byte[]> _repairBuffers = new();

        private void KeepRepairs(NTUnreliableSendStream g)
        {
            _repairsStale.Clear();
            foreach (var (entries, repair) in _repairs)
            {
                if ((short)(_currentTick - repair.tick) >= NTUnreliable.MAX_BASELINE_AGE)
                    _repairsStale.Add(entries);
            }

            for (int i = 0; i < _repairsStale.Count; i++)
            {
                _repairBuffers.Push(_repairs[_repairsStale[i]].bytes);
                _repairs.Remove(_repairsStale[i]);
            }
            _repairsStale.Clear();

            for (int p = 0; p < g.tickPackets.Count; p++)
            {
                var record = _sharePackets[g.tickPackets[p]];
                var span = record.packet.ToByteData().span;
                if (_repairs.TryGetValue(record.entries, out var previous))
                    _repairBuffers.Push(previous.bytes);
                var bytes = _repairBuffers.Count > 0 ? _repairBuffers.Pop() : null;
                if (bytes == null || bytes.Length < span.Length)
                    bytes = new byte[System.Math.Max(span.Length, 1500)];
                span.CopyTo(bytes);
                _repairs[record.entries] = (_currentTick, bytes, span.Length);
            }
        }

        // Resends a shared anchor the peer lost, as it was: its deltas are against the shared
        // stream's previous anchor, which the peer holds, so the peer gets the shared baseline back
        // instead of taking every transform in it over.
        private bool TrySendRepair(PlayerID player, NTUnreliableSendStream peer, in NTDeferredAnchor anchor)
        {
            if (anchor.repaired || !_repairs.TryGetValue(anchor.entries, out var repair) || repair.tick != anchor.tick)
                return false;

            ushort seq = peer.nextSeq;
            ref var slot = ref peer.ring[seq % NTUnreliable.RING_SIZE];
            if (slot.entries != null)
                NTSharedEntries.Release(slot.entries);
            if (slot.excluded != null)
                ListPool<NetworkID>.Destroy(slot.excluded);
            slot = new NTUnreliableSlot
            {
                used = true,
                seq = seq,
                order = peer.nextOrder,
                tick = _currentTick
            };

            peer.deferredAnchors.Add(new NTDeferredAnchor
            {
                entries = anchor.entries,
                excluded = CopyExcluded(anchor.excluded),
                order = anchor.order,
                seq = seq,
                tick = anchor.tick,
                repaired = true
            });

            peer.nextSeq += 1;
            peer.nextOrder += 1;

            var delta = new NetworkTransformUnreliableDelta
            {
                scene = _scene,
                seq = seq,
                tick = anchor.tick,
                repair = true,
                packet = new ByteData(repair.bytes, 0, repair.length)
            };
            _broadcaster.Send(player, delta, Channel.Unreliable);
            return true;
        }
        private readonly List<int> _globalForced = new();
        private readonly HashSet<int> _globalForcedSet = new();
        private readonly List<NetworkID> _globalExcluded = new();
        private readonly List<(NetworkTransform nt, bool force)> _globalPatch = new();

        private NTUnreliableSendStream GetGlobalStream()
        {
            if (_global != null)
                return _global;

            _global = new NTUnreliableSendStream { asServer = _asServer, prevBaselines = new NTBaselineSlot[0] };
            _global.EnsureBaselineCapacity(_byIndex.Count);
            _global.pendingInitialized = true;
            for (int i = 0; i < _networkTransforms.Count; i++)
            {
                var nt = _networkTransforms[i];
                if (IsLive(nt) && nt.IsSpawned(_asServer))
                    AddPending(_global, nt);
            }

            return _global;
        }

        // The peer takes the transform over from the shared stream, starting from its state.
        private void AddException(NTUnreliableSendStream peer, int index, in NTBaselineSlot? baseline = null)
        {
            var g = _global;
            if (index < 0 || index >= _byIndex.Count || _byIndex[index] is not { } nt)
                return;

            peer.EnsureBaselineCapacity(_byIndex.Count);
            g.EnsureBaselineCapacity(_byIndex.Count);
            if (!peer.overrideByIndex[index])
            {
                peer.overrideByIndex[index] = true;
                peer.overrides.Add(index);
                peer.SetAdaptiveAt(index, g.adaptiveByIndex[index]);
            }

            peer.baselines[index] = baseline ?? g.baselines[index];
            if (!peer.pendingByIndex[index])
                AddPending(peer, nt);
        }

        private static void RemoveException(NTUnreliableSendStream peer, int index, NetworkID nid)
        {
            int slot = peer.overrides.IndexOf(index);
            if (slot < 0)
                return;

            peer.overrides[slot] = peer.overrides[peer.overrides.Count - 1];
            peer.overrides.RemoveAt(peer.overrides.Count - 1);
            peer.overrideByIndex[index] = false;
            if (peer.pendingByIndex[index] && !RemovePending(peer, nid))
                peer.pendingByIndex[index] = false;
        }

        private void ConvergeExceptions(NTUnreliableSendStream peer)
        {
            var g = _global;
            for (int o = peer.overrides.Count - 1; o >= 0; o--)
            {
                int i = peer.overrides[o];
                if (i >= g.baselines.Length || i >= _byIndex.Count || _byIndex[i] is not { } nt)
                    continue;

                var x = peer.baselines[i];
                var y = g.baselines[i];
                if (!x.has || !y.has || x.tick != y.tick || x.gen != y.gen || x.genEpoch != y.genEpoch ||
                    x.revision != y.revision || !VelocityEquals(x.velocity, y.velocity) || !x.state.Equals(y.state))
                    continue;
                if (peer.generationOverrides.Count > 0 && peer.generationOverrides.ContainsKey(nt.ntNid))
                    continue;
                if (peer.pendingByIndex[i] && !g.pendingByIndex[i])
                    continue;

                int last = peer.overrides.Count - 1;
                peer.overrides[o] = peer.overrides[last];
                peer.overrides.RemoveAt(last);
                peer.overrideByIndex[i] = false;
                if (peer.pendingByIndex[i] && !RemovePending(peer, nt.ntNid))
                    peer.pendingByIndex[i] = false;
            }
        }

        private bool[] _globalChanged = new bool[0];
        private readonly List<NetworkTransform> _globalDemoted = new();

        // Exceptions are the peer's own transforms: their changes are its own pending work.
        private void QueueChangedExceptions(List<NetworkTransform> changed)
        {
            bool any = false;
            foreach (var peer in _global.followers)
            {
                if (peer.overrides.Count > 0)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
                return;

            if (_globalChanged.Length < _byIndex.Count)
                System.Array.Resize(ref _globalChanged, _byIndex.Count);
            for (int i = 0; i < changed.Count; i++)
            {
                int index = changed[i].GetNTIndex(_asServer);
                if (index >= 0 && index < _globalChanged.Length)
                    _globalChanged[index] = true;
            }

            foreach (var peer in _global.followers)
            {
                var overrides = peer.overrides;
                if (overrides.Count == 0)
                    continue;

                if (overrides.Count <= changed.Count)
                {
                    for (int o = 0; o < overrides.Count; o++)
                    {
                        int index = overrides[o];
                        if (index < _globalChanged.Length && _globalChanged[index] && !peer.pendingByIndex[index] &&
                            _byIndex[index] is { } nt)
                            AddPending(peer, nt);
                    }
                }
                else
                {
                    for (int i = 0; i < changed.Count; i++)
                    {
                        var nt = changed[i];
                        int index = nt.GetNTIndex(_asServer);
                        if (peer.IsOverridden(index) && !peer.pendingByIndex[index])
                            AddPending(peer, nt);
                    }
                }
            }

            for (int i = 0; i < changed.Count; i++)
            {
                int index = changed[i].GetNTIndex(_asServer);
                if (index >= 0 && index < _globalChanged.Length)
                    _globalChanged[index] = false;
            }
        }

        private bool ObservedByAllPeers(NetworkTransform nt, PlayerID localPlayer, int peerCount)
        {
            if (nt.HasRemoteController || !nt.id.HasValue || !nt.IsSpawned(_asServer))
                return false;

            var observers = nt.observers;
            int count = observers?.Count ?? 0;
            if (count != peerCount && localPlayer != PlayerID.Server)
            {
                for (int i = 0; i < count; i++)
                {
                    if (observers[i] == localPlayer)
                    {
                        count--;
                        break;
                    }
                }
            }

            return count == peerCount;
        }

        // The shared stream only carries transforms every peer observes and none controls; the
        // others are each candidate peer's own.
        private void DemoteMixedTransforms(PlayerID localPlayer, int peerCount)
        {
            var g = _global;
            _globalDemoted.Clear();
            for (int i = 0; i < g.pending.Count; i++)
            {
                var nt = g.pending[i];
                if (IsLive(nt) && !ObservedByAllPeers(nt, localPlayer, peerCount))
                    _globalDemoted.Add(nt);
            }

            if (_globalDemoted.Count == 0)
                return;

            for (int d = 0; d < _globalDemoted.Count; d++)
            {
                var nt = _globalDemoted[d];
                int index = nt.GetNTIndex(_asServer);
                foreach (var (player, peer) in _sendStreams)
                {
                    if (peer.leader != g)
                        continue;
                    if (!IsSendCandidate(nt, player, localPlayer))
                    {
                        RemoveException(peer, index, nt.ntNid);
                        continue;
                    }

                    if (!peer.IsOverridden(index))
                        AddException(peer, index);
                    else if (!peer.pendingByIndex[index])
                        AddPending(peer, nt);
                }

                RemovePending(g, nt.ntNid);
                if (index < g.baselines.Length)
                {
                    g.baselines[index] = default;
                    g.prevBaselines[index] = default;
                    g.SetAdaptiveAt(index, null);
                }
            }

            _globalDemoted.Clear();
        }

        private NTUnreliableSendStream PrepareGlobalPeer(PlayerID player, PlayerID localPlayer)
        {
            var g = _global;
            var peer = GetSendStream(player);
            if (peer.leader == g)
                return peer;

            if (peer.leader != null)
                MaterializeFollower(peer);
            for (int i = peer.followers.Count - 1; i >= 0; i--)
                MaterializeFollower(peer.followers[i]);

            // A new peer has no baseline of its own yet: everything it observes starts as its own
            // and joins the shared stream as its anchors are acknowledged.
            peer.pending.Clear();
            peer.EnsureBaselineCapacity(_byIndex.Count);
            for (int i = 0; i < peer.baselines.Length; i++)
            {
                peer.pendingByIndex[i] = false;
                peer.overrideByIndex[i] = false;
            }
            peer.overrides.Clear();

            bool hadState = peer.pendingInitialized;
            peer.pendingInitialized = true;
            peer.leader = g;
            g.followers.Add(peer);

            for (int i = 0; i < _networkTransforms.Count; i++)
            {
                var nt = _networkTransforms[i];
                if (!IsSendCandidate(nt, player, localPlayer))
                    continue;
                int index = nt.GetNTIndex(_asServer);
                AddException(peer, index, hadState ? peer.baselines[index] : default(NTBaselineSlot));
            }

            return peer;
        }

        // Due anchors of a peer: its own patch packets carry its exceptions' baselines; a shared
        // anchor it lost leaves it on the baseline the shared stream had before adopting it.
        private void ProcessGlobalPeerAnchors(PlayerID player, NTUnreliableSendStream peer)
        {
            var g = _global;
            var deferred = peer.deferredAnchors;
            List<int> completed = null;
            int writeIndex = 0;
            for (int i = 0; i < deferred.Count; i++)
            {
                var anchor = deferred[i];
                if ((short)(_currentTick - anchor.tick) < _anchorAdoptDelay)
                {
                    deferred[writeIndex++] = anchor;
                    continue;
                }

                var decision = DecideAnchor(peer, anchor);
                if (decision == NTAnchorDecision.Optimistic)
                    peer.ring[anchor.seq % NTUnreliable.RING_SIZE].optimistic = true;

                if (anchor.patch)
                {
                    if (decision != NTAnchorDecision.Lost)
                        AdoptEntries(peer, anchor.entries, anchor.order, true, decision == NTAnchorDecision.Acked,
                            ref completed);
                    else
                        NTSharedEntries.Release(anchor.entries);
                }
                else
                {
                    if (decision == NTAnchorDecision.Optimistic)
                    {
                        deferred[writeIndex++] = anchor;
                        continue;
                    }

                    if (decision == NTAnchorDecision.Lost && TrySendRepair(player, peer, anchor))
                    {
                        if (anchor.excluded != null)
                            ListPool<NetworkID>.Destroy(anchor.excluded);
                        continue;
                    }

                    if (decision == NTAnchorDecision.Lost)
                    {
                        var entries = anchor.entries;
                        for (int e = 0; e < entries.Count; e++)
                        {
                            var nt = entries[e].transform;
                            if (!IsLive(nt) || !nt.ntNid.Equals(entries[e].nid) || entries[e].life != nt.ntLife ||
                                (anchor.excluded != null && anchor.excluded.Contains(entries[e].nid)))
                                continue;
                            int index = nt.GetNTIndex(_asServer);
                            if (peer.IsOverridden(index) || index >= g.baselines.Length)
                                continue;
                            var current = g.baselines[index];
                            if (current.has && current.tick == anchor.tick)
                                AddException(peer, index, g.prevBaselines[index]);
                            else if (current.has && (short)(current.tick - anchor.tick) < 0)
                                AddException(peer, index, current);
                            else
                                AddException(peer, index, default(NTBaselineSlot));
                        }
                    }

                    NTSharedEntries.Release(anchor.entries);
                }

                if (anchor.excluded != null)
                    ListPool<NetworkID>.Destroy(anchor.excluded);
            }

            deferred.RemoveRange(writeIndex, deferred.Count - writeIndex);

            if (completed != null)
            {
                RemovePendingCompleted(peer, completed);
                ListPool<int>.Destroy(completed);
            }
        }

        private void SendGlobalFanout(IReadOnlyList<PlayerID> players, PlayerID localPlayer)
        {
            var g = GetGlobalStream();

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == localPlayer)
                    continue;
                var peer = PrepareGlobalPeer(player, localPlayer);
                if (peer.budgetBits == 0)
                    peer.budgetBits = CalculateBudgetBits(_manager.GetMTU(player, Channel.Unreliable, _asServer));
                if (g.budgetBits == 0 || peer.budgetBits < g.budgetBits)
                    g.budgetBits = peer.budgetBits;
            }

            if (g.budgetBits == 0)
                return;

            // Observer changes of this tick: a peer that starts observing a transform takes it over
            // until it has the shared baseline; one that stops observing simply stops getting it.
            foreach (var (player, peer) in _sendStreams)
            {
                if (peer.leader != g || peer.tickInvalidations.Count == 0)
                    continue;

                for (int i = 0; i < peer.tickInvalidations.Count; i++)
                {
                    var nid = peer.tickInvalidations[i];
                    if (!TryGetRegisteredTransform(nid, out var nt))
                        continue;
                    int index = nt.GetNTIndex(_asServer);
                    if (IsSendCandidate(nt, player, localPlayer))
                    {
                        AddException(peer, index);
                        ClearBaseline(peer, nid);
                        ClearAdaptive(peer, nid);
                    }
                    else
                    {
                        RemoveException(peer, index, nid);
                    }
                }

                peer.tickInvalidations.Clear();
            }

            DemoteMixedTransforms(localPlayer, g.followers.Count);

            // The shared encode.
            g.tickPackets.Clear();
            g.lastSendTick = _currentTick;
            g.hasSent = true;
            AdoptDeferredAnchors(g);

            if (g.pending.Count > 0)
            {
                _shareKeysMarker.Begin();
                bool shareable = TryBuildShareKeys(g, _currentTick, out int keyCount);
                _shareKeysMarker.End();

                if (shareable)
                {
                    for (int i = 0; i < keyCount;)
                        i = EncodeSharePacket(default, g, i, keyCount);
                }
            }

            if (NTUnreliable.IsAnchorTick(_currentTick))
                KeepRepairs(g);

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == localPlayer || !_sendStreams.TryGetValue(player, out var peer) || peer.leader != g)
                    continue;

                SendGlobalPeer(player, peer, localPlayer);
            }
        }

        private void SendGlobalPeer(PlayerID player, NTUnreliableSendStream peer, PlayerID localPlayer)
        {
            var g = _global;
            ProcessGlobalPeerAnchors(player, peer);
            if (peer.overrides.Count > 0)
                ConvergeExceptions(peer);

            peer.tickPackets.Clear();
            peer.lastSendTick = _currentTick;
            peer.hasSent = true;

            _globalForced.Clear();
            _globalForcedSet.Clear();
            for (int p = 0; p < g.tickPackets.Count; p++)
            {
                var record = _sharePackets[g.tickPackets[p]];
                entriesWrittenCount += record.entries.Count;
                sharedPacketCount++;

                List<NetworkID> excluded = null;
                _globalExcluded.Clear();
                var entries = record.entries;
                for (int e = 0; e < entries.Count && peer.overrides.Count > 0; e++)
                {
                    var nt = entries[e].transform;
                    int index = nt is null ? -1 : nt.GetNTIndex(_asServer);
                    if (peer.IsOverridden(index))
                    {
                        _globalExcluded.Add(entries[e].nid);
                        if (_globalForcedSet.Add(index))
                            _globalForced.Add(index);
                    }
                }

                if (_globalExcluded.Count > 0)
                {
                    // Every entry excluded: nothing in it is for this peer.
                    if (_globalExcluded.Count == entries.Count)
                        continue;
                    excluded = _globalExcluded;
                }

                CommitPacket(player, peer, record.packet, record.entries, excluded);
            }

            if (peer.overrides.Count > 0)
                EncodeGlobalPatches(player, peer, localPlayer);
        }

        // The peer's own transforms: the ones it still has to send, and the ones the shared
        // packets carried this tick, which its client must take from the patch instead.
        private void EncodeGlobalPatches(PlayerID player, NTUnreliableSendStream peer, PlayerID localPlayer)
        {
            _globalPatch.Clear();
            for (int i = 0; i < peer.pending.Count; i++)
            {
                var nt = peer.pending[i];
                if (!IsLive(nt))
                    continue;
                int index = nt.GetNTIndex(_asServer);
                if (!peer.IsOverridden(index))
                    continue;
                _globalPatch.Add((nt, _globalForcedSet.Remove(index)));
            }

            for (int f = 0; f < _globalForced.Count; f++)
            {
                int index = _globalForced[f];
                if (_globalForcedSet.Contains(index) && index < _byIndex.Count && _byIndex[index] is { } nt)
                    _globalPatch.Add((nt, true));
            }

            _globalForced.Clear();
            _globalForcedSet.Clear();
            if (_globalPatch.Count == 0)
                return;

            _prepareUnreliableMarker.Begin();

            long budgetBits = peer.budgetBits;
            int p = 0;
            while (p < _globalPatch.Count)
            {
                BitPacker packer = null;
                List<NTUnreliableEntry> pending = null;
                int countPos = 0;
                int writtenCount = 0;
                int lastDist = 0;
                NetworkID lastNid = default;
                PackedInt lastLen = default;

                for (; p < _globalPatch.Count; p++)
                {
                    var (nt, force) = _globalPatch[p];
                    if (!IsSendCandidate(nt, player, localPlayer))
                        continue;

                    var nid = nt.ntNid;
                    var writeResult = TryWriteEntry(nt, peer, _currentTick, lastDist, out var plan,
                        out var newLastDist, out var velocity, out var wireGen, out var wireGenEpoch, force);

                    if (writeResult == NTWriteResult.SkipAcked)
                    {
                        RemovePending(peer, nid);
                        continue;
                    }

                    if (writeResult != NTWriteResult.Written)
                        continue;

                    int entryBits = plan.bits;
                    if (writtenCount > 0 && packer!.positionInBits + entryBits + ENTRY_HEADER_BITS > budgetBits)
                        break;

                    entriesWrittenCount++;

                    if (packer == null)
                    {
                        packer = BitPackerPool.Get();
                        pending = NTSharedEntries.Rent();
                        countPos = packer.positionInBits;
                        Packer<int>.Write(packer, 0);
                    }

                    PackedInt length = entryBits;
                    DeltaPacker<PackedInt>.Write(packer, lastLen, length);
                    lastLen = length;
                    DeltaPacker<NetworkID>.Write(packer, lastNid, nid);
                    WriteEntry(packer, plan);

                    lastNid = nid;
                    lastDist = newLastDist;
                    writtenCount += 1;
                    if (plan.absolute)
                        ((NTSharedEntries)pending).hasAbsolute = true;
                    pending.Add(new NTUnreliableEntry
                    {
                        nid = lastNid,
                        state = nt.capturedState,
                        velocity = velocity,
                        tick = _currentTick,
                        gen = wireGen,
                        genEpoch = wireGenEpoch,
                        revision = nt.capturedRevision,
                        life = nt.ntLife,
                        transform = nt
                    });
                }

                if (writtenCount == 0)
                    break;

                var lastPos = packer.positionInBits;
                packer.SetBitPosition(countPos);
                Packer<int>.Write(packer, writtenCount);
                packer.SetBitPosition(lastPos);

                CommitPacket(player, peer, packer, pending, null, true);
                packer.Dispose();
            }

            _globalPatch.Clear();
            _prepareUnreliableMarker.End();
        }

        private float[] _ackLags = new float[16];

        // Every peer adopts a sent anchor at the same delay. Waiting for the median ack lag means
        // most acks (and so most losses) are known by then; the rest adopt optimistically.
        private void UpdateAnchorAdoptDelay()
        {
            int count = 0;
            foreach (var stream in _sendStreams.Values)
            {
                if (stream.anchorAckLag <= 0)
                    continue;
                if (count == _ackLags.Length)
                    System.Array.Resize(ref _ackLags, count * 2);
                int j = count++;
                for (; j > 0 && _ackLags[j - 1] > stream.anchorAckLag; j--)
                    _ackLags[j] = _ackLags[j - 1];
                _ackLags[j] = stream.anchorAckLag;
            }

            if (count == 0)
                return;

            float median = _ackLags[(count - 1) / 2];
            _anchorAdoptDelay = System.Math.Clamp((int)System.Math.Ceiling(median) + 1, 2, NTUnreliable.ANCHOR_REFRESH_AGE);
        }

        private void SendStatesShared(PlayerID player, PlayerID localPlayer)
        {
            var stream = PrepareSendStream(player, localPlayer);
            stream.lastSendTick = _currentTick;
            stream.hasSent = true;
            stream.tickPackets.Clear();
            stream.tickFresh = false;
            stream.tickMixed = false;
            stream.tickOrigin = null;
            stream.tickShareable = true;

            AdoptDeferredAnchors(stream);
            if (stream.pending.Count == 0)
                return;

            if (stream.budgetBits == 0)
                stream.budgetBits = CalculateBudgetBits(_manager.GetMTU(player, Channel.Unreliable, _asServer));

            _shareKeysMarker.Begin();
            bool shareable = TryBuildShareKeys(stream, _currentTick, out int keyCount);
            _shareKeysMarker.End();

            if (!shareable)
            {
                stream.tickShareable = false;
                Detach(stream);
                if (stream.pending.Count > 0)
                    SendUnreliableStates(player, stream);
                return;
            }

            for (int i = 0; i < keyCount;)
            {
                var record = FindSharePacket(stream, i, keyCount);

                if (record != null)
                {
                    ReplaySharePacket(player, stream, record);
                    stream.tickPackets.Add(record.index);
                    if (stream.tickOrigin == null)
                        stream.tickOrigin = record.origin;
                    else if (stream.tickOrigin != record.origin)
                        stream.tickMixed = true;
                    i += record.keyCount;
                    continue;
                }

                i = EncodeSharePacket(player, stream, i, keyCount);
            }
        }

        private void SendUnreliableStates(PlayerID player, NTUnreliableSendStream stream)
        {
            _prepareUnreliableMarker.Begin();

            if (stream.budgetBits == 0)
                stream.budgetBits = CalculateBudgetBits(_manager.GetMTU(player, Channel.Unreliable, _asServer));

            long budgetBits = stream.budgetBits;

            BitPacker packer = null;
            List<NTUnreliableEntry> pending = null;
            int countPos = 0;
            int writtenCount = 0;
            int lastDist = 0;
            NetworkID lastNid = default;
            PackedInt lastLen = default;

            for (var i = 0; i < stream.pending.Count;)
            {
                var nt = stream.pending[i];

                // Observer, ownership, registration, and reset callbacks maintain membership.
                // Keep only a defensive destroyed/unregistered guard in the hot send loop.
                if (!IsLive(nt))
                {
                    stream.pending.RemoveAt(i);
                    continue;
                }

                var nid = nt.ntNid;
                var writeResult = TryWriteEntry(nt, stream, _currentTick, lastDist, out var plan, out var newLastDist,
                    out var velocity, out var wireGen, out var wireGenEpoch);

                if (nt.adaptiveDebugDumpEnabled)
                    nt.DebugDumpLine($"send,tick={_currentTick},to={player},result={writeResult}," +
                                     $"pos={NetworkTransform.DebugPos(nt.capturedState)}");

                if (writeResult == NTWriteResult.SkipAcked)
                {
                    stream.pending.RemoveAt(i);
                    stream.SetPending(nt, false);
                    continue;
                }

                if (writeResult == NTWriteResult.Hold)
                {
                    adaptiveHoldCount++;
                    i++;
                    continue;
                }

                entriesWrittenCount++;

                int entryBits = plan.bits;

                if (writtenCount > 0 && packer!.positionInBits + entryBits + ENTRY_HEADER_BITS > budgetBits)
                {
                    FlushUnreliablePacket(player, stream, packer, pending, countPos, writtenCount);
                    packer = null;
                    pending = null;
                    writtenCount = 0;
                    lastDist = 0;
                    lastNid = default;
                    lastLen = default;

                    // the new packet restarts the dist chain, so re-encode
                    if (TryWriteEntry(nt, stream, _currentTick, lastDist, out plan, out newLastDist, out velocity,
                            out wireGen, out wireGenEpoch) != NTWriteResult.Written)
                        continue;
                    entryBits = plan.bits;
                }

                if (packer == null)
                {
                    packer = BitPackerPool.Get();
                    pending = NTSharedEntries.Rent();
                    countPos = packer.positionInBits;
                    Packer<int>.Write(packer, 0);
                }

                PackedInt length = entryBits;

                DeltaPacker<PackedInt>.Write(packer, lastLen, length);
                lastLen = length;
                DeltaPacker<NetworkID>.Write(packer, lastNid, nid);
                WriteEntry(packer, plan);

                lastNid = nid;
                lastDist = newLastDist;
                writtenCount += 1;
                if (plan.absolute)
                    ((NTSharedEntries)pending).hasAbsolute = true;
                pending.Add(new NTUnreliableEntry
                {
                    nid = lastNid,
                    state = nt.capturedState,
                    velocity = velocity,
                    tick = _currentTick,
                    gen = wireGen,
                    genEpoch = wireGenEpoch,
                    revision = nt.capturedRevision,
                    life = nt.ntLife,
                    transform = nt
                });

                i++;
            }

            if (writtenCount > 0)
                FlushUnreliablePacket(player, stream, packer, pending, countPos, writtenCount);

            _prepareUnreliableMarker.End();
        }

        private void SendStatesTo(PlayerID player, PlayerID localPlayer)
        {
            var stream = PrepareSendStream(player, localPlayer);
            if (stream.pending.Count > 0)
                SendUnreliableStates(player, stream);
        }

        public void Register(NetworkTransform networkTransform)
        {
            if (!networkTransform.id.HasValue)
                return;
            AddTrs(networkTransform);

            int registeredIndex = networkTransform.GetNTIndex(_asServer);
            if (registeredIndex < 0)
            {
                int index;
                if (_freeIndices.Count > 0)
                {
                    index = _freeIndices.Pop();
                    _byIndex[index] = networkTransform;
                }
                else
                {
                    index = _byIndex.Count;
                    _byIndex.Add(networkTransform);
                    foreach (var stream in _sendStreams.Values)
                        stream.EnsureBaselineCapacity(_byIndex.Count);
                }

                networkTransform.SetNTIndex(_asServer, index);
                networkTransform.ntNid = networkTransform.id.Value;
                networkTransform.ntRegistered = true;
                networkTransform.ntLife++;
                if (_asServer)
                    networkTransform.ntServerModule = this;
                else
                    networkTransform.ntClientModule = this;
            }

            if (_global != null && _global.pendingInitialized)
            {
                _global.EnsureBaselineCapacity(_byIndex.Count);
                AddPending(_global, networkTransform);
            }

            if (_sendStreams.Count == 0)
                return;

            var localPlayer = GetLocalPlayer();
            SplitDivergingFollowers(networkTransform, localPlayer);
            foreach (var (player, stream) in _sendStreams)
            {
                if (!stream.pendingInitialized || stream.leader != null)
                    continue;

                if (IsSendCandidate(networkTransform, player, localPlayer))
                    AddPending(stream, networkTransform);
            }
        }

        private void SplitDivergingFollowers(NetworkTransform nt, PlayerID localPlayer)
        {
            foreach (var (player, stream) in _sendStreams)
            {
                if (stream.leader == null || stream.leader == _global)
                    continue;

                if (!_streamPlayers.TryGetValue(stream.leader, out var leaderPlayer) ||
                    !KeepsFollowing(nt, player, leaderPlayer, localPlayer))
                    _splitScratch.Add(stream);
            }

            for (int i = 0; i < _splitScratch.Count; i++)
            {
                if (_splitScratch[i].leader != null)
                    MaterializeFollower(_splitScratch[i]);
            }

            _splitScratch.Clear();
        }

        private readonly List<NTUnreliableSendStream> _splitScratch = new();

        private void AddTrs(NetworkTransform networkTransform)
        {
            if (networkTransform.GetNTIndex(_asServer) < 0 && networkTransform.id.HasValue)
            {
                var id = networkTransform.id.Value;
                int lo = 0, hi = _networkTransforms.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    var midId = _networkTransforms[mid].id;
                    if (midId != null && midId.Value > id)
                        hi = mid;
                    else
                        lo = mid + 1;
                }

                _networkTransforms.Insert(lo, networkTransform);
                return;
            }

            if (_networkTransforms.Contains(networkTransform))
                return;

            for (int i = 0; i < _networkTransforms.Count; i++)
            {
                var networkID = _networkTransforms[i].id;
                if (networkID != null && networkTransform.id != null &&
                    networkID.Value > networkTransform.id.Value)
                {
                    _networkTransforms.Insert(i, networkTransform);
                    return;
                }
            }

            _networkTransforms.Add(networkTransform);
        }

        public void Unregister(NetworkTransform networkTransform)
        {
            _networkTransforms.Remove(networkTransform);

            networkTransform.unreliableEncodeCache?.Dispose();
            networkTransform.unreliableEncodeCache = null;
            networkTransform.unreliableRecvHistory?.Clear();

            int index = networkTransform.GetNTIndex(_asServer);
            if (index >= _byIndex.Count || (index >= 0 && !ReferenceEquals(_byIndex[index], networkTransform)))
                index = -1;
            bool wasRegistered = index >= 0;
            var nid = wasRegistered ? networkTransform.ntNid : networkTransform.id.GetValueOrDefault();

            if (_global != null && (wasRegistered || networkTransform.id.HasValue))
            {
                if (wasRegistered && index < _global.baselines.Length)
                {
                    _global.baselines[index] = default;
                    _global.prevBaselines[index] = default;
                    _global.SetAdaptiveAt(index, null);
                }
                RemovePending(_global, nid);
            }

            if (wasRegistered || networkTransform.id.HasValue)
            {
                // Entries still in flight carry the registration's life, so their acks are
                // ignored after this; no need to scan every stream's send history here.
                foreach (var stream in _sendStreams.Values)
                {
                    // Followers share the leader's per-transform state, which is cleared below.
                    if (stream.leader != null)
                    {
                        if (wasRegistered && stream.IsOverridden(index))
                            RemoveException(stream, index, nid);
                        continue;
                    }

                    if (wasRegistered && index < stream.baselines.Length)
                    {
                        stream.baselines[index] = default;
                        stream.SetAdaptiveAt(index, null);
                    }
                    stream.generationOverrides.Remove(nid);
                    RemovePending(stream, nid);
                }
            }

            if (wasRegistered)
            {
                _byIndex[index] = null;
                _freeIndices.Push(index);
                networkTransform.SetNTIndex(_asServer, -1);
                if (_asServer)
                {
                    if (networkTransform.ntServerModule == this)
                        networkTransform.ntServerModule = null;
                }
                else if (networkTransform.ntClientModule == this)
                    networkTransform.ntClientModule = null;
                networkTransform.ntRegistered = networkTransform.ntIndex >= 0 || networkTransform.ntServerIndex >= 0;
                if (!networkTransform.ntRegistered)
                    networkTransform.ntNid = default;
            }
        }

        private bool TryGetRegisteredTransform(NetworkID nid, out NetworkTransform result)
        {
            int lo = 0;
            int hi = _networkTransforms.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var nt = _networkTransforms[mid];
                if (!nt || !nt.id.HasValue)
                {
                    result = null;
                    return false;
                }

                var midNid = nt.id.Value;
                if (midNid.Equals(nid))
                {
                    result = nt;
                    return true;
                }

                if (midNid < nid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            result = null;
            return false;
        }

        internal void InvalidateSendBaseline(PlayerID player, NetworkID nid, bool enqueue = true)
        {
            if (!_sendStreams.TryGetValue(player, out var stream))
                return;

            // Observer changes usually hit every peer alike (spawn, despawn). A follower records
            // its invalidations and only splits off if they differ from its leader's this tick.
            if (stream.leader != null)
            {
                stream.tickInvalidations.Add(nid);
                return;
            }

            if (stream.followers.Count > 0)
                stream.tickInvalidations.Add(nid);

            ApplyInvalidation(player, stream, nid, enqueue);
        }

        // Per-observer fast path: the transform is in hand, so no registry searches, and the
        // pending list is only touched when membership actually changes.
        internal void InvalidateSendBaseline(PlayerID player, NetworkTransform nt, bool enqueue)
        {
            if (!_sendStreams.TryGetValue(player, out var stream))
                return;

            int index = nt.GetNTIndex(_asServer);
            if (index < 0 || !nt.ntNid.Equals(nt.id.GetValueOrDefault()))
            {
                InvalidateSendBaseline(player, nt.id.GetValueOrDefault(), enqueue);
                return;
            }

            var nid = nt.ntNid;
            if (stream.leader != null)
            {
                stream.tickInvalidations.Add(nid);
                return;
            }

            if (stream.followers.Count > 0)
                stream.tickInvalidations.Add(nid);

            if (index < stream.baselines.Length)
            {
                stream.baselines[index] = new NTBaselineSlot
                {
                    hasFloor = true,
                    floorTick = _currentTick
                };

                if (stream.adaptiveByIndex[index] != null)
                    stream.SetAdaptiveAt(index, null);
            }

            if (stream.generationOverrides.Count > 0)
                stream.generationOverrides.Remove(nid);

            bool pending = index < stream.pendingByIndex.Length && stream.pendingByIndex[index];
            bool candidate = enqueue && stream.pendingInitialized && IsSendCandidate(nt, player, GetLocalPlayer());

            if (candidate)
            {
                if (!pending)
                    AddPending(stream, nt);
            }
            else if (pending)
            {
                RemovePending(stream, nid);
            }
        }

        private void ApplyInvalidation(PlayerID player, NTUnreliableSendStream stream, NetworkID nid, bool enqueue)
        {
            ClearBaseline(stream, nid);
            ClearAdaptive(stream, nid);
            stream.generationOverrides.Remove(nid);

            if (enqueue && stream.pendingInitialized && TryGetRegisteredTransform(nid, out var nt) &&
                IsSendCandidate(nt, player, GetLocalPlayer()))
                AddPending(stream, nt);
            else
                RemovePending(stream, nid);
        }

        // Followers whose invalidations differ from their leader's copy the leader's state and
        // then re-apply every transform either of them touched for themselves: over-invalidating
        // costs an absolute, under-invalidating would keep sending a transform it can't see.
        private void ReconcileInvalidations()
        {
            foreach (var (player, stream) in _sendStreams)
            {
                var leader = stream.leader;
                if (leader == null || SameInvalidations(stream.tickInvalidations, leader.tickInvalidations))
                    continue;

                MaterializeFollower(stream);
                for (int i = 0; i < leader.tickInvalidations.Count; i++)
                    ReapplyMembership(player, stream, leader.tickInvalidations[i]);
                for (int i = 0; i < stream.tickInvalidations.Count; i++)
                    ReapplyMembership(player, stream, stream.tickInvalidations[i]);
            }

            foreach (var stream in _sendStreams.Values)
                stream.tickInvalidations.Clear();
        }

        private void ReapplyMembership(PlayerID player, NTUnreliableSendStream stream, NetworkID nid)
        {
            bool candidate = TryGetRegisteredTransform(nid, out var nt) &&
                             IsSendCandidate(nt, player, GetLocalPlayer());
            ApplyInvalidation(player, stream, nid, candidate);
        }

        private static bool SameInvalidations(List<NetworkID> a, List<NetworkID> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }

            return true;
        }

        internal void PrepareTargetedReset(PlayerID target, NetworkID nid, byte gen, uint genEpoch)
        {
            if (_global != null && TryGetRegisteredTransform(nid, out var resetNt))
            {
                int index = resetNt.GetNTIndex(_asServer);
                foreach (var stream in _sendStreams.Values)
                {
                    if (stream.leader == _global)
                        AddException(stream, index);
                }
            }

            DetachAll();
            foreach (var (player, stream) in _sendStreams)
            {
                if (player == target)
                {
                    InvalidateSendBaseline(target, nid, true);
                    continue;
                }

                if (!stream.generationOverrides.ContainsKey(nid))
                    stream.generationOverrides[nid] = new NTUnreliableGeneration { gen = gen, epoch = genEpoch };
            }
        }

        internal void ClearGenerationOverrides(NetworkID nid)
        {
            bool hasTransform = TryGetRegisteredTransform(nid, out var nt);
            var localPlayer = hasTransform ? GetLocalPlayer() : default;

            if (hasTransform)
                SplitDivergingFollowers(nt, localPlayer);

            foreach (var (player, stream) in _sendStreams)
            {
                stream.generationOverrides.Remove(nid);

                if (!hasTransform || !stream.pendingInitialized || stream.leader != null)
                    continue;

                if (IsSendCandidate(nt, player, localPlayer))
                    AddPending(stream, nt);
                else
                    RemovePending(stream, nid);
            }
        }

        private int _vouchBucketTicks;

        private void UpdateOffsetEstimate(NTUnreliableRecvStream stream, ushort senderTick)
        {
            var tickManager = _manager ? _manager.tickModule : null;
            if (tickManager == null)
                return;

            uint localTick = tickManager.localTick;
            ushort off = (ushort)((ushort)localTick - senderTick);

            if (!stream.offsetInit)
            {
                stream.offsetInit = true;
                stream.offsetRef = off;
                stream.devMaxA = 0;
                stream.devMaxB = short.MinValue;
                stream.bucketStart = localTick;
                return;
            }

            short dev = (short)(off - stream.offsetRef);
            if (stream.devMaxA == short.MinValue || dev > stream.devMaxA)
                stream.devMaxA = dev;
        }

        private void UpdateVouchedTick(NTUnreliableRecvStream stream, uint localTick)
        {
            if (!stream.offsetInit)
            {
                stream.vouchedValid = false;
                return;
            }

            if (_vouchBucketTicks == 0)
                _vouchBucketTicks = System.Math.Max(_manager.tickModule.tickRate, 16);

            uint elapsed = localTick - stream.bucketStart;
            if (elapsed >= (uint)(_vouchBucketTicks * 2))
            {
                stream.devMaxA = short.MinValue;
                stream.devMaxB = short.MinValue;
                stream.bucketStart = localTick;
            }
            else if (elapsed >= (uint)_vouchBucketTicks)
            {
                stream.devMaxB = stream.devMaxA;
                stream.devMaxA = short.MinValue;
                stream.bucketStart += (uint)_vouchBucketTicks;
            }

            int devMax = stream.devMaxA == short.MinValue ? int.MinValue : stream.devMaxA;
            if (stream.devMaxB != short.MinValue && stream.devMaxB > devMax)
                devMax = stream.devMaxB;

            if (devMax == int.MinValue)
            {
                if (!stream.hasFrozenDev)
                {
                    stream.vouchedValid = false;
                    return;
                }

                devMax = stream.frozenDevMax;
            }
            else
            {
                stream.frozenDevMax = (short)devMax;
                stream.hasFrozenDev = true;
            }

            stream.vouchedTick = (ushort)((ushort)localTick -
                                          (ushort)(stream.offsetRef + devMax + NTUnreliable.VOUCH_SLACK_TICKS));
            stream.vouchedValid = true;
        }

        private void RenderAdaptiveStates(uint localTick)
        {
            foreach (var stream in _recvStreams.Values)
                UpdateVouchedTick(stream, localTick);

            for (var i = 0; i < _networkTransforms.Count; i++)
            {
                var nt = _networkTransforms[i];
                if (!IsLive(nt) || !nt.needsAdaptiveRender)
                    continue;

                ushort vouchedTick = 0;
                bool hasVouched = false;
                var sender = _asServer ? nt.owner.GetValueOrDefault() : PlayerID.Server;

                if (_recvStreams.TryGetValue(sender, out var stream) && stream.vouchedValid)
                {
                    vouchedTick = stream.vouchedTick;
                    hasVouched = true;
                }

                if (!nt.TryTickAdaptiveRender(localTick, vouchedTick, hasVouched, out var state))
                    continue;

                NetworkIdentity frameParent = null;
                if (state.frame == NetworkTransformFrame.LocalIdentity &&
                    (!_factory.TryGetIdentity(_scene, state.parentId, out frameParent) || !frameParent))
                    continue;

                nt.ApplyAdaptiveSample(state, frameParent);
            }
        }

        public void PostFixedUpdate()
        {
            using var _ = _postFixedUpdateMarker.Auto();

            var localPlayer = GetLocalPlayer();
            uint localTick = _manager.tickModule.localTick;
            _currentTick = (ushort)localTick;
            if (_asServer)
                UpdateAnchorAdoptDelay();

            RenderAdaptiveStates(localTick);

            // Catching up: only the frame's last tick sends; the earlier ones' states are
            // superseded within the same frame anyway.
            if (_manager.tickModule.hasQueuedTicks)
                return;

            int ntCount = _networkTransforms.Count;
            _changedTransforms.Clear();

            _gatherStateMarker.Begin();
            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                {
                    uint previousRevision = nt.capturedRevision;
                    nt.GatherAndCapture(_currentTick);

                    if (nt.capturedRevision != previousRevision && nt.id.HasValue)
                        _changedTransforms.Add(nt);
                }
            }
            _gatherStateMarker.End();

            QueueChangedStates(_changedTransforms, localPlayer);

            if (!_asServer)
            {
                SendStatesTo(PlayerID.Server, localPlayer);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_scene, out var globalPlayers))
            {
                SendGlobalFanout(globalPlayers, localPlayer);
                ReleaseShareGroups();
            }
            else if (_scenePlayers.TryGetPlayersInScene(_scene, out var players))
            {
                ReconcileInvalidations();

                // Followers whose due anchors differ from their leader's split off before the
                // leader adopts, so they copy the pre-adoption state.
                foreach (var stream in _sendStreams.Values)
                {
                    if (stream.leader != null && !SameDueAnchors(stream, stream.leader))
                        MaterializeFollower(stream);
                }

                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == localPlayer)
                        continue;

                    if (!_sendStreams.TryGetValue(player, out var stream) || stream.leader == null)
                        SendStatesShared(player, localPlayer);
                }

                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == localPlayer || !_sendStreams.TryGetValue(player, out var stream))
                        continue;

                    if (stream.leader == null)
                    {
                        // Split from its leader during this tick's sends: send on its own.
                        if (!stream.hasSent || stream.lastSendTick != _currentTick)
                            SendStatesShared(player, localPlayer);
                        continue;
                    }

                    if (stream.leader.tickShareable && stream.leader.hasSent &&
                        stream.leader.lastSendTick == _currentTick)
                    {
                        ReplayLeader(player, stream);
                    }
                    else
                    {
                        MaterializeFollower(stream);
                        SendStatesShared(player, localPlayer);
                    }
                }

                foreach (var stream in _sendStreams.Values)
                {
                    if (stream.leader == null)
                        TryFollow(stream);
                }

                ReleaseShareGroups();
            }

            FlushAcks();

        }
    }
}
