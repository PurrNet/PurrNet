using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    public class NetworkTransformModule : INetworkModule, IPromoteToServerModule
    {
        static readonly ProfilerMarker _postFixedUpdateMarker = new ProfilerMarker("NetworkTransform.PostFixedUpdate");
        static readonly ProfilerMarker _gatherStateMarker = new ProfilerMarker("NetworkTransform.GatherState");
        static readonly ProfilerMarker _prepareUnreliableMarker = new ProfilerMarker("NetworkTransform.PrepareState");

        private readonly List<NetworkTransform> _networkTransforms = new();
        private readonly Dictionary<PlayerID, NTUnreliableSendStream> _sendStreams = new();
        private readonly Dictionary<PlayerID, NTUnreliableRecvStream> _recvStreams = new();
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersBroadcaster _broadcaster;
        private readonly NetworkManager _manager;
        private readonly SceneID _scene;
        private readonly HierarchyFactory _factory;
        private bool _asServer;

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
            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
        }

        public void Disable(bool asServer)
        {
            _broadcaster.Unsubscribe<NetworkTransformUnreliableDelta>(OnUnreliableDelta);
            _broadcaster.Unsubscribe<NetworkTransformUnreliableAck>(OnUnreliableAck);
            _broadcaster.Unsubscribe<NetworkTransformUnreliableNack>(OnUnreliableNack);
            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            ReleaseAllStreams();
        }

        private void ReleaseAllStreams()
        {
            foreach (var stream in _sendStreams.Values)
                NTUnreliable.Release(stream.ring);
            foreach (var stream in _recvStreams.Values)
                NTUnreliable.Release(stream.ring);
            _sendStreams.Clear();
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

            if (_sendStreams.Remove(player, out var send))
                NTUnreliable.Release(send.ring);
            if (_recvStreams.Remove(player, out var recv))
                NTUnreliable.Release(recv.ring);
        }

        private NTUnreliableSendStream GetSendStream(PlayerID player)
        {
            if (!_sendStreams.TryGetValue(player, out var stream))
            {
                stream = new NTUnreliableSendStream();
                _sendStreams.Add(player, stream);
            }

            return stream;
        }

        private NTUnreliableRecvStream GetRecvStream(PlayerID player)
        {
            if (!_recvStreams.TryGetValue(player, out var stream))
            {
                stream = new NTUnreliableRecvStream();
                _recvStreams.Add(player, stream);
            }

            return stream;
        }

        private static bool MarkReceived(NTUnreliableRecvStream stream, ushort seq)
        {
            if (!stream.ackInit)
            {
                stream.ackInit = true;
                stream.latestSeq = seq;
                stream.ackBits = 0;
                return true;
            }

            var diff = (short)(seq - stream.latestSeq);

            if (diff > 0)
            {
                if (diff >= 33)
                    stream.ackBits = 0;
                else if (diff == 32)
                    stream.ackBits = 1u << 31;
                else
                    stream.ackBits = (stream.ackBits << diff) | (1u << (diff - 1));

                stream.latestSeq = seq;
                return true;
            }

            if (diff == 0)
                return false;

            int d = -diff;
            if (d > 32)
                return false;

            uint mask = 1u << (d - 1);
            if ((stream.ackBits & mask) != 0)
                return false;

            stream.ackBits |= mask;
            return true;
        }

        private static bool TryGetRecvBaseline(NTUnreliableRecvStream stream, ushort seq, NetworkID nid,
            out NetworkTransformState state, out NetworkTransformVelocity velocity, out byte gen)
        {
            state = default;
            velocity = default;
            gen = default;

            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (!slot.used || slot.seq != seq || slot.entries == null)
                return false;

            // Entries are ascending-nid by construction (packets are written from the id-sorted list).
            var list = slot.entries;
            int lo = 0, hi = list.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var midNid = list[mid].nid;

                if (midNid.Equals(nid))
                {
                    state = list[mid].state;
                    velocity = list[mid].velocity;
                    gen = list[mid].gen;
                    return true;
                }

                if (nid > midNid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return false;
        }

        private void OnUnreliableDelta(PlayerID player, NetworkTransformUnreliableDelta data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var sender = asServer ? player : PlayerID.Server;
            var stream = GetRecvStream(sender);

            if (data.ack.HasValue && _sendStreams.TryGetValue(sender, out var sendStream))
                ProcessAck(sendStream, (ushort)(data.ack.Value >> 32), (uint)data.ack.Value);

            if (!MarkReceived(stream, data.seq))
                return;

            stream.ackDirty = true;

            var decoded = ListPool<NTUnreliableEntry>.Instantiate();

            using var packet = BitPackerPool.Get(data.packet);
            packet.ResetPositionAndMode(true);

            int ntCount = default;
            int lastDist = 0;
            NetworkID lastNid = default;
            PackedInt lastLen = default;

            Packer<int>.Read(packet, ref ntCount);

            for (var i = 0; i < ntCount; i++)
            {
                PackedInt length = default;
                DeltaPacker<PackedInt>.Read(packet, lastLen, ref length);
                lastLen = length;
                DeltaPacker<NetworkID>.Read(packet, lastNid, ref lastNid);

                int bodyStart = packet.positionInBits;

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
                    dist = (int)packet.ReadBits(6) + 1;
                    lastDist = dist;
                }

                bool recorded = false;

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
                    else if (dist > 0 && TryGetRecvBaseline(stream, (ushort)(data.seq - dist), lastNid,
                                 out var baseline, out var baseVel, out gen))
                    {
                        var predicted = NetworkTransformVelocity.Predict(baseline, baseVel, dist);
                        state = nt.ReadDeltaState(packet, baseline, predicted);
                        velocity = NetworkTransformVelocity.Derive(baseline, state, dist);
                        ok = true;
                    }
                    else
                    {
                        ok = false;
                    }

                    if (ok)
                    {
                        NetworkIdentity frameParent = null;
                        if (state.frame == NetworkTransformFrame.LocalIdentity)
                            _factory.TryGetIdentity(_scene, state.parentId, out frameParent);

                        if (nt.TryApplyUnreliableState(state, gen, data.seq, frameParent, isAbsolute))
                        {
                            decoded.Add(new NTUnreliableEntry
                            {
                                nid = lastNid,
                                state = state,
                                velocity = velocity,
                                gen = gen
                            });
                            recorded = true;
                        }
                    }
                }

                // Anything not recorded must not become an acked baseline on the sender —
                // acks are packet-granular, so a NACK is the only per-entry signal.
                if (!recorded)
                    SendNack(sender, lastNid);

                packet.SetBitPosition(bodyStart + length.value);
            }

            ref var slot = ref stream.ring[data.seq % NTUnreliable.RING_SIZE];
            if (slot.entries != null)
                ListPool<NTUnreliableEntry>.Destroy(slot.entries);
            slot = new NTUnreliableSlot { used = true, seq = data.seq, entries = decoded };
        }

        private void OnUnreliableAck(PlayerID player, NetworkTransformUnreliableAck data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var key = asServer ? player : PlayerID.Server;
            if (_sendStreams.TryGetValue(key, out var stream))
                ProcessAck(stream, data.seq, data.ackBits);
        }

        private void ProcessAck(NTUnreliableSendStream stream, ushort seq, uint ackBits)
        {
            TryAdoptAck(stream, seq);
            for (int i = 0; i < 32; i++)
            {
                if ((ackBits & (1u << i)) != 0)
                    TryAdoptAck(stream, (ushort)(seq - 1 - i));
            }
        }

        private void TryAdoptAck(NTUnreliableSendStream stream, ushort seq)
        {
            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (!slot.used || slot.seq != seq || slot.acked)
                return;

            slot.acked = true;

            var list = slot.entries;
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];

                if (!_factory.TryGetIdentity(_scene, entry.nid, out _))
                    continue;

                if (stream.nackFloor.TryGetValue(entry.nid, out var floor))
                {
                    if (slot.order < floor)
                        continue;
                    stream.nackFloor.Remove(entry.nid);
                }

                if (!stream.acked.TryGetValue(entry.nid, out var baseline) || slot.order > baseline.order)
                {
                    stream.acked[entry.nid] = new NTUnreliableBaseline
                    {
                        state = entry.state,
                        velocity = entry.velocity,
                        gen = entry.gen,
                        genEpoch = entry.genEpoch,
                        order = slot.order
                    };
                }
            }
        }

        private void OnUnreliableNack(PlayerID player, NetworkTransformUnreliableNack data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var key = asServer ? player : PlayerID.Server;
            if (_sendStreams.TryGetValue(key, out var stream))
            {
                stream.acked.Remove(data.id);
                stream.nackFloor[data.id] = stream.nextOrder;
            }
        }

        private void SendNack(PlayerID sender, NetworkID nid)
        {
            // Reliable: the NACK is the only per-entry correction against packet-granular acks;
            // losing it while the ack lands wedges a resting object on a phantom baseline.
            var nack = new NetworkTransformUnreliableNack { scene = _scene, id = nid };
            _broadcaster.Send(sender, nack, Channel.ReliableUnordered);
        }

        private void FlushAcks()
        {
            foreach (var (sender, stream) in _recvStreams)
            {
                if (!stream.ackDirty)
                    continue;

                stream.ackDirty = false;

                var ack = new NetworkTransformUnreliableAck { scene = _scene, seq = stream.latestSeq, ackBits = stream.ackBits };
                _broadcaster.Send(sender, ack, Channel.Unreliable);
            }
        }

        private PlayerID GetLocalPlayer()
        {
            if (_manager.TryGetModule<PlayersManager>(false, out var _players))
                return _players.localPlayerId.GetValueOrDefault();
            return PlayerID.Server;
        }

        // Worst-case bits for one entry's framing (len delta + nid delta).
        private const int ENTRY_HEADER_BITS = 128;
        // Wrapper overhead: broadcast type hash + scene + seq + ByteData length prefix.
        private const int UNRELIABLE_PACKET_OVERHEAD = 32;

        private static bool TryWriteEntry(BitPacker tmp, NetworkTransform nt, NTUnreliableSendStream stream,
            int lastDist, out int newLastDist, out NetworkTransformVelocity velocity)
        {
            newLastDist = lastDist;
            velocity = default;
            tmp.ResetPositionAndMode(false);

            var nid = nt.id!.Value;
            var current = nt.capturedState;

            bool hasAcked = stream.acked.TryGetValue(nid, out var baseline) && baseline.genEpoch == nt.sendGenEpoch;

            // Suppression must not depend on baseline age — a resting object's baseline never
            // refreshes, and re-sending absolutes for it every 32 packets floods static scenes.
            if (hasAcked && baseline.state.Equals(current))
                return false;

            int dist = hasAcked ? (int)(stream.nextOrder - baseline.order) : 0;
            bool canDelta = hasAcked && dist >= 1 && dist <= NTUnreliable.MAX_BASELINE_AGE &&
                            nt.CanDeltaAgainst(baseline.state);

            if (canDelta)
            {
                tmp.WriteBits(0, 1);

                if (dist == lastDist)
                {
                    tmp.WriteBits(1, 1);
                }
                else
                {
                    tmp.WriteBits(0, 1);
                    tmp.WriteBits((ulong)(dist - 1), 6);
                    newLastDist = dist;
                }

                var predicted = NetworkTransformVelocity.Predict(baseline.state, baseline.velocity, dist);
                nt.WriteDeltaState(tmp, baseline.state, predicted);
                velocity = NetworkTransformVelocity.Derive(baseline.state, current, dist);
            }
            else
            {
                tmp.WriteBits(1, 1);
                Packer<byte>.Write(tmp, nt.sendGen);
                nt.WriteAbsoluteState(tmp);
            }

            return true;
        }

        private void FlushUnreliablePacket(PlayerID player, NTUnreliableSendStream stream, BitPacker packer,
            List<NTUnreliableEntry> pending, int countPos, int writtenCount)
        {
            var lastPos = packer.positionInBits;
            packer.SetBitPosition(countPos);
            Packer<int>.Write(packer, writtenCount);
            packer.SetBitPosition(lastPos);

            ushort seq = stream.nextSeq;
            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (slot.entries != null)
                ListPool<NTUnreliableEntry>.Destroy(slot.entries);
            slot = new NTUnreliableSlot { used = true, seq = seq, order = stream.nextOrder, entries = pending };
            stream.nextSeq += 1;
            stream.nextOrder += 1;

            var delta = new NetworkTransformUnreliableDelta(_scene, seq, packer);

            if (_recvStreams.TryGetValue(player, out var recv) && recv.ackInit && recv.ackDirty)
            {
                recv.ackDirty = false;
                delta.ack = ((ulong)recv.latestSeq << 32) | recv.ackBits;
            }

            _broadcaster.Send(player, delta, Channel.Unreliable);

            packer.Dispose();
        }

        private void SendUnreliableStates(PlayerID player, List<NetworkTransform> candidates)
        {
            _prepareUnreliableMarker.Begin();

            var stream = GetSendStream(player);

            if (stream.budgetBits == 0)
            {
                int mtu = _manager.GetMTU(player, Channel.Unreliable, _asServer) - UNRELIABLE_PACKET_OVERHEAD;
                if (mtu < 128)
                    mtu = 128;
                stream.budgetBits = mtu * 8;
            }

            int budgetBits = stream.budgetBits;

            BitPacker packer = null;
            List<NTUnreliableEntry> pending = null;
            int countPos = 0;
            int writtenCount = 0;
            int lastDist = 0;
            NetworkID lastNid = default;
            PackedInt lastLen = default;

            using var tmp = BitPackerPool.Get();

            int count = candidates.Count;
            for (var i = 0; i < count; i++)
            {
                var nt = candidates[i];

                if (!TryWriteEntry(tmp, nt, stream, lastDist, out var newLastDist, out var velocity))
                    continue;

                int entryBits = tmp.positionInBits;

                if (writtenCount > 0 && packer.positionInBits + entryBits + ENTRY_HEADER_BITS > budgetBits)
                {
                    FlushUnreliablePacket(player, stream, packer, pending, countPos, writtenCount);
                    packer = null;
                    pending = null;
                    writtenCount = 0;
                    lastDist = 0;
                    lastNid = default;
                    lastLen = default;

                    // the flush advanced nextOrder; baseline distances change, so re-encode
                    if (!TryWriteEntry(tmp, nt, stream, lastDist, out newLastDist, out velocity))
                        continue;
                    entryBits = tmp.positionInBits;
                }

                if (packer == null)
                {
                    packer = BitPackerPool.Get();
                    pending = ListPool<NTUnreliableEntry>.Instantiate();
                    countPos = packer.positionInBits;
                    Packer<int>.Write(packer, 0);
                }

                PackedInt length = entryBits;
                tmp.ResetPositionAndMode(true);

                DeltaPacker<PackedInt>.Write(packer, lastLen, length);
                lastLen = length;
                DeltaPacker<NetworkID>.Write(packer, lastNid, nt.id!.Value);
                packer.WriteBits(tmp, length);

                lastNid = nt.id.Value;
                lastDist = newLastDist;
                writtenCount += 1;
                pending.Add(new NTUnreliableEntry
                {
                    nid = nt.id.Value,
                    state = nt.capturedState,
                    velocity = velocity,
                    gen = nt.sendGen,
                    genEpoch = nt.sendGenEpoch
                });
            }

            if (writtenCount > 0)
                FlushUnreliablePacket(player, stream, packer, pending, countPos, writtenCount);

            _prepareUnreliableMarker.End();
        }

        private void GatherCandidates(PlayerID player, PlayerID localPlayer, List<NetworkTransform> candidates)
        {
            int ntCount = _networkTransforms.Count;

            if (player == PlayerID.Server)
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];

                    if (!nt.IsSpawned(_asServer) || !nt.id.HasValue)
                        continue;

                    if (nt.IsControlling(localPlayer, false))
                        candidates.Add(nt);
                }
            }
            else
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];

                    if (!nt.IsSpawned(_asServer) || !nt.id.HasValue)
                        continue;

                    if (!nt.IsControlling(player, false) && nt.IsObserver(player))
                        candidates.Add(nt);
                }
            }
        }

        private void SendStatesTo(PlayerID player, PlayerID localPlayer)
        {
            var candidates = ListPool<NetworkTransform>.Instantiate();

            GatherCandidates(player, localPlayer, candidates);

            if (candidates.Count > 0)
                SendUnreliableStates(player, candidates);

            ListPool<NetworkTransform>.Destroy(candidates);
        }

        public void Register(NetworkTransform networkTransform)
        {
            if (!networkTransform.id.HasValue)
                return;
            AddTrs(networkTransform);
        }

        private void AddTrs(NetworkTransform networkTransform)
        {
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

            if (networkTransform.id.HasValue)
            {
                var nid = networkTransform.id.Value;
                foreach (var stream in _sendStreams.Values)
                {
                    stream.acked.Remove(nid);
                    stream.nackFloor.Remove(nid);
                    PurgeRing(stream.ring, nid);
                }

                foreach (var stream in _recvStreams.Values)
                    PurgeRing(stream.ring, nid);
            }
        }

        // A late ack must not resurrect a despawned nid's baseline for a pooled object
        // that respawned with the same NetworkID.
        private static void PurgeRing(NTUnreliableSlot[] ring, NetworkID nid)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                var entries = ring[i].entries;
                if (entries == null)
                    continue;

                for (int e = entries.Count - 1; e >= 0; e--)
                {
                    if (entries[e].nid.Equals(nid))
                        entries.RemoveAt(e);
                }
            }
        }

        public void PostFixedUpdate()
        {
            using var _ = _postFixedUpdateMarker.Auto();

            var localPlayer = GetLocalPlayer();

            int ntCount = _networkTransforms.Count;

            _gatherStateMarker.Begin();
            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                {
                    nt.GatherState();
                    nt.CaptureUnreliableState();
                }
            }
            _gatherStateMarker.End();

            if (!_asServer)
            {
                SendStatesTo(PlayerID.Server, localPlayer);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_scene, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == localPlayer)
                        continue;

                    SendStatesTo(player, localPlayer);
                }
            }

            FlushAcks();
        }
    }
}
