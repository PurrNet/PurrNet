using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    public struct NetworkTransformDelta : IPackedAuto
    {
        public SceneID scene;
        public readonly ByteData packet;

        public NetworkTransformDelta(SceneID context, BitPacker packer)
        {
            scene = context;
            packet = packer.ToByteData();
        }
    }

    public class NetworkTransformModule : INetworkModule, IPromoteToServerModule
    {
        private readonly List<NetworkTransform> _networkTransforms = new();
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
        }

        public void PostPromoteToServerModule()
        {
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            _broadcaster.Subscribe<NetworkTransformDelta>(OnNetworkTransformDelta);
        }

        public void Disable(bool asServer)
        {
            _broadcaster.Unsubscribe<NetworkTransformDelta>(OnNetworkTransformDelta);
        }

        private void OnNetworkTransformDelta(PlayerID player, NetworkTransformDelta data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            using var packet = BitPackerPool.Get(data.packet);

            packet.ResetPositionAndMode(true);

            int ntCount = default;
            NetworkID lastNid = default;
            PackedInt lastLen = default;

            Packer<int>.Read(packet, ref ntCount);

            for (var i = 0; i < ntCount; i++)
            {
                PackedInt length = default;
                DeltaPacker<PackedInt>.Read(packet, lastLen, ref length);
                lastLen = length;
                DeltaPacker<NetworkID>.Read(packet, lastNid, ref lastNid);

                if (_factory.TryGetIdentity(_scene, lastNid, out var identity) && identity is NetworkTransform nt &&
                    (!asServer || nt.IsControlling(player, false)))
                {
                    nt.DeltaRead(packet);
                }
                else packet.SkipBits(length);
            }
        }

        private PlayerID GetLocalPlayer()
        {
            if (_manager.TryGetModule<PlayersManager>(false, out var _players))
                return _players.localPlayerId.GetValueOrDefault();
            return PlayerID.Server;
        }

        private bool PrepareDeltaState(BitPacker packer, PlayerID player)
        {
            var localPlayer = GetLocalPlayer();
            int ntCount = _networkTransforms.Count;
            bool anyWritten = false;

            var controlled = ListPool<NetworkTransform>.Instantiate();

            GatherCandidates(player, ntCount, localPlayer, controlled);

            NetworkID lastNid = default;
            int count = controlled.Count;
            var countPos = packer.positionInBits;

            int writtenCount = 0;
            PackedInt lastLen = default;
            Packer<int>.Write(packer, 0);

            using var tmp = BitPackerPool.Get();

            for (var i = 0; i < count; i++)
            {
                var nt = controlled[i];
                nt.DeltaWrite(tmp);
                anyWritten = true;

                PackedInt length = tmp.positionInBits;

                tmp.ResetPositionAndMode(true);

                DeltaPacker<PackedInt>.Write(packer, lastLen, length);
                lastLen = length;
                DeltaPacker<NetworkID>.Write(packer, lastNid, nt.id!.Value);
                packer.WriteBits(tmp, length);

                tmp.ResetPositionAndMode(false);

                lastNid = nt.id.Value;
                writtenCount += 1;
            }

            var lastPos = packer.positionInBits;
            packer.SetBitPosition(countPos);
            Packer<int>.Write(packer, writtenCount);
            packer.SetBitPosition(lastPos);

            ListPool<NetworkTransform>.Destroy(controlled);

            return anyWritten;
        }

        private void GatherCandidates(PlayerID player, int ntCount, PlayerID localPlayer, List<NetworkTransform> controlled)
        {
            if (player == PlayerID.Server)
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];

                    if (!nt.IsSpawned(_asServer) || !nt.id.HasValue)
                        continue;

                    if (nt.IsControlling(localPlayer, false) && nt.HasChanges())
                        controlled.Add(nt);
                }
            }
            else
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];

                    if (!nt.IsSpawned(_asServer) || !nt.id.HasValue)
                        continue;

                    if (!nt.IsControlling(player, false) && nt.IsObserver(player) && nt.HasChanges())
                        controlled.Add(nt);
                }
            }
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
        }

        public void PostFixedUpdate()
        {
            var localPlayer = GetLocalPlayer();

            int ntCount = _networkTransforms.Count;

            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                    nt.GatherState();
            }

            if (!_asServer)
            {
                using var packer = BitPackerPool.Get();

                if (PrepareDeltaState(packer, PlayerID.Server) && packer.positionInBits > 0)
                    _broadcaster.SendToServer(new NetworkTransformDelta(_scene, packer));
            }
            else if (_scenePlayers.TryGetPlayersInScene(_scene, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == localPlayer)
                        continue;

                    using var packer = BitPackerPool.Get();

                    if (PrepareDeltaState(packer, player) && packer.positionInBits > 0)
                        _broadcaster.Send(player, new NetworkTransformDelta(_scene, packer));
                }
            }

            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                    nt.DeltaSave();
            }
        }
    }
}
