using System.Collections.Generic;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    public class NetworkLODModule : INetworkModule, IPromoteToServerModule
    {
        private readonly List<NetworkLOD> _lods = new List<NetworkLOD>();
        private readonly NetworkManager _manager;
        private readonly NetworkLODFactory _factory;
        private readonly ScenesModule _scenes;
        private readonly ScenePlayersModule _scenePlayers;
        private readonly SceneID _scene;

        private PlayersManager _players;
        private bool _asServer;
        private int _sweepCursor;

        public NetworkLODModule(NetworkManager manager, NetworkLODFactory factory, ScenesModule scenes,
            ScenePlayersModule scenePlayers, SceneID scene)
        {
            _manager = manager;
            _factory = factory;
            _scenes = scenes;
            _scenePlayers = scenePlayers;
            _scene = scene;
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            if (_manager.TryGetModule<PlayersManager>(asServer, out _players))
                _players.onPlayerLeft += OnPlayerLeft;
        }

        public void Disable(bool asServer)
        {
            if (_players != null)
                _players.onPlayerLeft -= OnPlayerLeft;
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
        }

        public void PostPromoteToServerModule()
        {
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!asServer)
                return;

            for (var i = 0; i < _lods.Count; i++)
            {
                if (_lods[i])
                    _lods[i].RemovePlayer(player);
            }

            _factory.RemovePlayerAnchors(player);
        }

        public void Register(NetworkLOD lod)
        {
            if (!_lods.Contains(lod))
                _lods.Add(lod);
        }

        public void Unregister(NetworkLOD lod)
        {
            _lods.Remove(lod);
        }

        internal void Sweep()
        {
            if (!_asServer || _lods.Count == 0)
                return;

            if (!_scenePlayers.TryGetPlayersInScene(_scene, out var players) || players.Count == 0)
                return;

            var anchorPlayers = ListPool<PlayerID>.Instantiate();
            var anchorPositions = ListPool<List<Vector3>>.Instantiate();

            for (var i = 0; i < players.Count; i++)
            {
                var positions = ListPool<Vector3>.Instantiate();
                GatherAnchorPositions(players[i], positions);
                anchorPlayers.Add(players[i]);
                anchorPositions.Add(positions);
            }

            int budget = Mathf.Min(_factory.sweepBudgetPerTick, _lods.Count);

            for (var i = 0; i < budget; i++)
            {
                if (_sweepCursor >= _lods.Count)
                    _sweepCursor = 0;

                var lod = _lods[_sweepCursor++];

                if (lod && lod.profile)
                    Evaluate(lod, anchorPlayers, anchorPositions);
            }

            for (var i = 0; i < anchorPositions.Count; i++)
                ListPool<Vector3>.Destroy(anchorPositions[i]);

            ListPool<List<Vector3>>.Destroy(anchorPositions);
            ListPool<PlayerID>.Destroy(anchorPlayers);
        }

        private static void Evaluate(NetworkLOD lod, List<PlayerID> players, List<List<Vector3>> positionsPerPlayer)
        {
            var lodPos = lod.transform.position;
            var profile = lod.profile;

            for (var p = 0; p < players.Count; p++)
            {
                var player = players[p];

                if (lod.owner == player)
                    continue;

                var positions = positionsPerPlayer[p];
                if (positions.Count == 0)
                    continue;

                float sqrMin = float.MaxValue;
                for (var i = 0; i < positions.Count; i++)
                {
                    float sqr = (positions[i] - lodPos).sqrMagnitude;
                    if (sqr < sqrMin)
                        sqrMin = sqr;
                }

                byte current = lod.GetTier(player);
                byte next = profile.ResolveTier(sqrMin, current);

                if (next != current)
                    lod.ApplyTier(player, current, next);
            }
        }

        private void GatherAnchorPositions(PlayerID player, List<Vector3> positions)
        {
            bool hasScene = _scenes.TryGetSceneState(_scene, out var sceneState);

            if (_factory.TryGetAnchors(player, out var anchors))
            {
                for (var i = 0; i < anchors.Count; i++)
                {
                    var anchor = anchors[i];
                    if (anchor && (!hasScene || anchor.gameObject.scene == sceneState.scene))
                        positions.Add(anchor.position);
                }

                if (positions.Count > 0)
                    return;
            }

            foreach (var owned in _manager.EnumerateAllPlayerOwnedIds(player, true))
            {
                if (owned && owned.isActiveAndEnabled && owned.sceneId == _scene)
                    positions.Add(owned.transform.position);
            }
        }
    }
}
