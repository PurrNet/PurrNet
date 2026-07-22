using System;
using System.Collections.Generic;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    public class NetworkLODModule : INetworkModule, IPromoteToServerModule
    {
        private sealed class TargetRegistration
        {
            public readonly Dictionary<PlayerID, byte> tiers = new Dictionary<PlayerID, byte>();
            public NetworkLODProfile profile;

            public TargetRegistration(NetworkLODProfile profile)
            {
                this.profile = profile;
            }
        }

        private readonly List<ILODTarget> _targets = new List<ILODTarget>();
        private readonly Dictionary<ILODTarget, TargetRegistration> _registrations =
            new Dictionary<ILODTarget, TargetRegistration>();
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

            for (var i = 0; i < _targets.Count; i++)
            {
                if (_registrations.TryGetValue(_targets[i], out var registration))
                    registration.tiers.Remove(player);
            }

            _factory.RemovePlayerAnchors(player);
        }

        /// <summary>
        /// Registers a reference-type LOD target with this scene's LOD sweep.
        /// </summary>
        public bool Register(ILODTarget target, NetworkLODProfile profile)
        {
            ValidateTarget(target);

            if (_registrations.TryGetValue(target, out var registration))
            {
                registration.profile = profile;
                return false;
            }

            _targets.Add(target);
            _registrations.Add(target, new TargetRegistration(profile));
            return true;
        }

        /// <summary>
        /// Stops sweeping a previously registered LOD target and clears its tier state.
        /// </summary>
        public bool Unregister(ILODTarget target)
        {
            if (ReferenceEquals(target, null) || !_registrations.Remove(target))
                return false;

            int index = _targets.IndexOf(target);
            if (index >= 0)
            {
                _targets.RemoveAt(index);
                if (index < _sweepCursor)
                    _sweepCursor--;
                if (_sweepCursor >= _targets.Count)
                    _sweepCursor = 0;
            }

            return true;
        }

        /// <summary>
        /// Updates the profile used by a registered LOD target without resetting its resolved tiers.
        /// </summary>
        public bool UpdateProfile(ILODTarget target, NetworkLODProfile profile)
        {
            if (ReferenceEquals(target, null) || !_registrations.TryGetValue(target, out var registration))
                return false;

            registration.profile = profile;
            return true;
        }

        /// <summary>
        /// Gets the current resolved tier for a registered target and player.
        /// </summary>
        public byte GetTier(ILODTarget target, PlayerID player)
        {
            if (ReferenceEquals(target, null) || !_registrations.TryGetValue(target, out var registration))
                return 0;

            return registration.tiers.GetValueOrDefault(player, (byte)0);
        }

        internal bool SetTier(ILODTarget target, PlayerID player, byte tier)
        {
            if (!_registrations.TryGetValue(target, out var registration))
                return false;

            byte previousTier = registration.tiers.GetValueOrDefault(player, (byte)0);
            if (previousTier == tier)
                return true;

            registration.tiers[player] = tier;

            if (target is ILODDetailedTierTarget detailedTarget)
                detailedTarget.ApplyTier(player, previousTier, tier);
            else
                target.ApplyTier(player, tier);

            return true;
        }

        internal void Sweep()
        {
            if (!_asServer || _targets.Count == 0)
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

            int budget = Mathf.Min(_factory.sweepBudgetPerTick, _targets.Count);

            for (var i = 0; i < budget; i++)
            {
                if (_targets.Count == 0)
                    break;

                if (_sweepCursor >= _targets.Count)
                    _sweepCursor = 0;

                var target = _targets[_sweepCursor++];

                if (!IsAlive(target))
                {
                    Unregister(target);
                    continue;
                }

                if (_registrations.TryGetValue(target, out var registration) && registration.profile)
                    Evaluate(target, registration, anchorPlayers, anchorPositions);
            }

            for (var i = 0; i < anchorPositions.Count; i++)
                ListPool<Vector3>.Destroy(anchorPositions[i]);

            ListPool<List<Vector3>>.Destroy(anchorPositions);
            ListPool<PlayerID>.Destroy(anchorPlayers);
        }

        private void Evaluate(ILODTarget target, TargetRegistration registration, List<PlayerID> players,
            List<List<Vector3>> positionsPerPlayer)
        {
            var targetPosition = target.position;
            var profile = registration.profile;

            for (var p = 0; p < players.Count; p++)
            {
                var player = players[p];

                if (target is ILODOwnedTarget ownedTarget && ownedTarget.IsOwnedBy(player))
                    continue;

                var positions = positionsPerPlayer[p];
                if (positions.Count == 0)
                    continue;

                float sqrMin = float.MaxValue;
                for (var i = 0; i < positions.Count; i++)
                {
                    float sqr = (positions[i] - targetPosition).sqrMagnitude;
                    if (sqr < sqrMin)
                        sqrMin = sqr;
                }

                byte current = registration.tiers.GetValueOrDefault(player, (byte)0);
                byte next = profile.ResolveTier(sqrMin, current);

                if (next != current)
                    SetTier(target, player, next);
            }
        }

        private static bool IsAlive(ILODTarget target)
        {
            return !ReferenceEquals(target, null) && (!(target is UnityEngine.Object unityObject) || unityObject);
        }

        private static void ValidateTarget(ILODTarget target)
        {
            if (ReferenceEquals(target, null))
                throw new ArgumentNullException(nameof(target));
            if (target.GetType().IsValueType)
                throw new ArgumentException("LOD targets must be reference types.", nameof(target));
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
