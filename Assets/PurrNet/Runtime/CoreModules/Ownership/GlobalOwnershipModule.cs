using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Pooling;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet.Modules
{
    internal struct OwnershipInfo
    {
        public NetworkID identity;
        public PlayerID player;
    }

    internal struct OwnershipChangeBatch : IDisposable
    {
        public SceneID scene;
        public DisposableList<OwnershipInfo> state;

        public void Dispose()
        {
            state.Dispose();
        }
    }

    internal struct OwnershipSnapshot : IDisposable
    {
        public SceneID scene;
        public string sessionId;
        public uint epoch;
        public DisposableList<OwnershipInfo> state;

        public void Dispose()
        {
            state.Dispose();
        }
    }

    internal struct OwnershipCallback
    {
        public PlayerID? oldOwner;
        public PlayerID? newOwner;
        public NetworkIdentity identity;
        public bool isSpawner;
    }

    internal struct OwnershipChange : IDisposable
    {
        public SceneID sceneId;
        public DisposableList<NetworkID> identities;
        public bool isAdding;
        public PlayerID player;
        public bool isSpawner;

        public void Dispose()
        {
            identities.Dispose();
        }
    }

    public class GlobalOwnershipModule : INetworkModule, IFixedUpdate, IPreFixedUpdate, IPromoteToServerModule,
        ITransferToNewServer, IPostTransferToNewServer
    {
        readonly PlayersManager _playersManager;
        readonly ScenePlayersModule _scenePlayers;
        readonly HierarchyFactory _hierarchy;
        readonly NetworkManager _manager;

        readonly ScenesModule _scenes;
        readonly Dictionary<SceneID, SceneOwnership> _sceneOwnerships = new Dictionary<SceneID, SceneOwnership>();

        private bool _asServer;

        readonly HashSet<PlayerID> _exactOwnershipReboundPlayers = new HashSet<PlayerID>();
        readonly HashSet<SceneID> _pendingExactOwnershipScenes = new HashSet<SceneID>();
        private HostMigrationTransitionOptions _transferReconciliationTransition;
        private string _transferReconciliationFailure;

        internal bool isTransferReconciliationComplete =>
            !_transferReconciliationTransition.canReconcile ||
            (string.IsNullOrEmpty(_transferReconciliationFailure) &&
             _pendingExactOwnershipScenes.Count == 0);

        internal bool TryGetTransferReconciliationFailure(
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (transition.canReconcile && transition == _transferReconciliationTransition &&
                !string.IsNullOrEmpty(_transferReconciliationFailure))
            {
                failure = _transferReconciliationFailure;
                return true;
            }

            failure = null;
            return false;
        }

        public GlobalOwnershipModule(NetworkManager manager, HierarchyFactory hierarchy,
            PlayersManager players, ScenePlayersModule scenePlayers, ScenesModule scenes)
        {
            _manager = manager;
            _hierarchy = hierarchy;
            _scenes = scenes;
            _playersManager = players;
            _scenePlayers = scenePlayers;
        }

        public void PromoteToServerModule()
        {
            ResetAuthorityState();
            _asServer = true;
            foreach (var (scene, ownershipsValue) in _sceneOwnerships)
            {
                if (_hierarchy.TryGetHierarchy(scene, out var hierarchy))
                    ownershipsValue.PromoteToServerModule(hierarchy);
            }
        }

        public void PostPromoteToServerModule()
        {

        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            var scenes = _scenes.sceneStates;

            foreach (var (id, sceneState) in scenes)
            {
                if (sceneState.scene.isLoaded)
                    OnSceneLoaded(id, asServer);
            }

            _scenes.onSceneRegistrationAdded += OnSceneLoaded;
            _scenes.onSceneUnloaded += OnSceneUnloaded;
            _scenes.onSceneRegistrationRemoved += OnSceneUnloaded;
            _scenes.onPreRetainedSceneRebound += OnSceneRebound;

            _hierarchy.onIdentityRemoved += OnIdentityDespawned;
            _hierarchy.onObserverAdded += OnPlayerObserverAdded;
            _hierarchy.onPreFinishSpawn += HandlePendingChanges;

            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            _scenePlayers.onPlayerLoadedScene += OnPlayerLoadedScene;
            _scenePlayers.onPlayerSceneReboundInternal += OnPlayerLoadedScene;

            _playersManager.onPlayerLeft += OnPlayerLeft;
            _playersManager.onPlayerJoined += OnPlayerJoined;
            _playersManager.onPreHostMigrationConnectionRebound += OnHostMigrationConnectionRebound;
            _manager.onHostMigrationPlayerReady += OnHostMigrationPlayerReady;

            _playersManager.Subscribe<OwnershipSnapshot>(OnOwnershipSnapshot);
            _playersManager.Subscribe<OwnershipChangeBatch>(OnOwnershipChange);
            _playersManager.Subscribe<OwnershipChange>(OnOwnershipChange);
        }

        public void Disable(bool asServer)
        {
            _scenes.onSceneRegistrationAdded -= OnSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;
            _scenes.onSceneRegistrationRemoved -= OnSceneUnloaded;
            _scenes.onPreRetainedSceneRebound -= OnSceneRebound;

            _hierarchy.onIdentityRemoved -= OnIdentityDespawned;
            _hierarchy.onObserverAdded -= OnPlayerObserverAdded;
            _hierarchy.onPreFinishSpawn -= HandlePendingChanges;

            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            _scenePlayers.onPlayerLoadedScene -= OnPlayerLoadedScene;
            _scenePlayers.onPlayerSceneReboundInternal -= OnPlayerLoadedScene;

            _playersManager.onPlayerLeft -= OnPlayerLeft;
            _playersManager.onPlayerJoined -= OnPlayerJoined;
            _playersManager.onPreHostMigrationConnectionRebound -= OnHostMigrationConnectionRebound;
            _manager.onHostMigrationPlayerReady -= OnHostMigrationPlayerReady;

            _playersManager.Unsubscribe<OwnershipSnapshot>(OnOwnershipSnapshot);
            _playersManager.Unsubscribe<OwnershipChangeBatch>(OnOwnershipChange);
            _playersManager.Unsubscribe<OwnershipChange>(OnOwnershipChange);

            ResetAuthorityState();
        }

        public void TransferToNewServer()
        {
            ResetAuthorityState();
            _transferReconciliationTransition = _manager.expectedHostMigrationSession;
        }

        public void PostTransferToNewServer()
        {
            _pendingExactOwnershipScenes.Clear();
            _transferReconciliationTransition = default;
            _transferReconciliationFailure = null;
        }

        private void ResetAuthorityState()
        {
            foreach (var changes in _pendingOwnershipChanges.Values)
                changes.Dispose();

            _pendingOwnershipChanges.Clear();
            _pendingOwnership.Clear();
            _exactOwnershipReboundPlayers.Clear();
            _pendingExactOwnershipScenes.Clear();
            _transferReconciliationTransition = default;
            _transferReconciliationFailure = null;
        }

        private void RecordTransferReconciliationFailure(string failure)
        {
            if (!_transferReconciliationTransition.canReconcile ||
                !string.IsNullOrEmpty(_transferReconciliationFailure))
                return;

            _transferReconciliationFailure = failure;
        }

        /// <summary>
        /// Gets all the objects owned by the given player.
        /// This creates a new list every time it's called.
        /// So it's recommended to cache the result if you're going to use it multiple times.
        /// </summary>
        public List<NetworkIdentity> GetAllPlayerOwnedIds(PlayerID player)
        {
            List<NetworkIdentity> ids = new List<NetworkIdentity>();

            foreach (var (scene, owned) in _sceneOwnerships)
            {
                if (!_hierarchy.TryGetHierarchy(scene, out var hierarchy))
                    continue;

                var ownedIds = owned.TryGetOwnedObjects(player);
                foreach (var id in ownedIds)
                {
                    if (hierarchy.TryGetIdentity(id, out var identity))
                        ids.Add(identity);
                }
            }

            return ids;
        }

        public bool PlayerOwnsSomething(PlayerID player)
        {
            foreach (var (_, owned) in _sceneOwnerships)
            {
                var ownedIds = owned.TryGetOwnedObjects(player);
                if (ownedIds.Count > 0)
                    return true;
            }

            return false;
        }

        public void GetAllPlayerOwnedIds(PlayerID player, List<NetworkIdentity> ids)
        {
            foreach (var (scene, owned) in _sceneOwnerships)
            {
                if (!_hierarchy.TryGetHierarchy(scene, out var hierarchy))
                    continue;

                var ownedIds = owned.TryGetOwnedObjects(player);
                foreach (var id in ownedIds)
                {
                    if (hierarchy.TryGetIdentity(id, out var identity))
                        ids.Add(identity);
                }
            }
        }

        public IEnumerable<NetworkIdentity> EnumerateAllPlayerOwnedIds(PlayerID player)
        {
            foreach (var (scene, owned) in _sceneOwnerships)
            {
                if (!_hierarchy.TryGetHierarchy(scene, out var hierarchy))
                    continue;

                var ownedIds = owned.TryGetOwnedObjects(player);
                foreach (var id in ownedIds)
                {
                    if (hierarchy.TryGetIdentity(id, out var identity))
                        yield return identity;
                }
            }
        }

        private void OnIdentityDespawned(NetworkIdentity identity)
        {
            if (!identity.id.HasValue)
                return;

            if (_sceneOwnerships.TryGetValue(identity.sceneId, out var module))
                module.RemoveOwnership(identity);

            for (var i = 0; i < _pendingOwnership.Count; i++)
            {
                var pendingOp = _pendingOwnership[i];
                if (pendingOp.change.identity == identity.id.Value)
                {
                    _pendingOwnership.RemoveAt(i--);
                }
            }
        }

        struct PlayerSceneID : IEquatable<PlayerSceneID>
        {
            public PlayerID player;
            public SceneID scene;

            public bool Equals(PlayerSceneID other)
            {
                return player.Equals(other.player) && scene.Equals(other.scene);
            }

            public override bool Equals(object obj)
            {
                return obj is PlayerSceneID other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(player, scene);
            }
        }

        readonly Dictionary<PlayerSceneID, DisposableList<OwnershipInfo>> _pendingOwnershipChanges =
            new Dictionary<PlayerSceneID, DisposableList<OwnershipInfo>>();

        private void OnPlayerObserverAdded(PlayerID player, NetworkIdentity target)
        {
            if (!target.id.HasValue)
                return;

            if (!_sceneOwnerships.TryGetValue(target.sceneId, out var ownerships))
                return;

            if (!_asServer)
                return;

            if (!ownerships.TryGetOwner(target, out _) &&
                (_exactOwnershipReboundPlayers.Count == 0 ||
                 !_exactOwnershipReboundPlayers.Contains(player)))
                return;

            // Owner is intentionally not captured here; it is re-queried at flush time
            // (HandlePendingChanges) to avoid sending a stale snapshot when ownership
            // mutates between OnObserverAdded and the flush (e.g. user code calling
            // GiveOwnership from inside OnObserverAdded).
            var info = new OwnershipInfo
            {
                identity = target.id.Value
            };

            var key = new PlayerSceneID
            {
                player = player,
                scene = target.sceneId
            };

            if (_pendingOwnershipChanges.TryGetValue(key, out var list))
            {
                list.Add(info);
            }
            else
            {
                list = DisposableList<OwnershipInfo>.Create(16);
                list.Add(info);
                _pendingOwnershipChanges.Add(key, list);
            }
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!_sceneOwnerships.TryGetValue(scene, out var ownerships)) return;

            if (_asServer)
            {
                if (_exactOwnershipReboundPlayers.Count > 0 &&
                    _exactOwnershipReboundPlayers.Contains(player))
                    SendExactOwnershipSnapshot(player, scene, ownerships);
                else
                    SendOwnershipSnapshot(player, scene, ownerships);
            }

            var owned = ownerships.TryGetOwnedObjects(player);

            foreach (var id in owned)
            {
                if (_hierarchy.TryGetIdentity(scene, id, out var identity))
                    identity.TriggerOnOwnerReconnected(player, asServer);
            }
        }

        private void SendOwnershipSnapshot(PlayerID player, SceneID scene, SceneOwnership ownerships)
        {
            var state = ownerships.GetState();
            if (state.Count == 0)
                return;

            using var snapshot = DisposableList<OwnershipInfo>.Create(state.Count);
            snapshot.AddRange(state);

            _playersManager.Send(player, new OwnershipChangeBatch
            {
                scene = scene,
                state = snapshot
            });
        }

        private void SendExactOwnershipSnapshot(PlayerID player, SceneID scene, SceneOwnership ownerships)
        {
            var state = ownerships.GetState();
            using var snapshot = DisposableList<OwnershipInfo>.Create(state.Count);
            snapshot.AddRange(state);

            var transition = _manager.hostMigrationSession;
            var snapshotPacket = new OwnershipSnapshot
            {
                scene = scene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                state = snapshot
            };
            if (!_playersManager.SendExactBarrierBypass(
                    player, transition, snapshotPacket))
                _playersManager.Send(player, snapshotPacket);
        }

        private void OnHostMigrationConnectionRebound(PlayerID player, bool isReconnect, bool asServer)
        {
            if (asServer)
                _exactOwnershipReboundPlayers.Add(player);
        }

        private void OnHostMigrationPlayerReady(PlayerID player, HostMigrationTransitionOptions transition)
        {
            _exactOwnershipReboundPlayers.Remove(player);
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            _exactOwnershipReboundPlayers.Remove(player);

            if (asServer)
                return;

            foreach (var (scene, ownerships) in _sceneOwnerships)
            {
                var owned = ownerships.TryGetOwnedObjects(player);
                if (owned.Count == 0)
                    continue;

                var ownedCache = ListPool<NetworkID>.Instantiate();
                ownedCache.AddRange(owned);

                for (var i = 0; i < ownedCache.Count; i++)
                {
                    var id = ownedCache[i];
                    if (_hierarchy.TryGetIdentity(scene, id, out var identity))
                        identity.TriggerOnOwnerDisconnected(player);
                }

                ListPool<NetworkID>.Destroy(ownedCache);
            }
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (asServer)
                return;

            foreach (var (scene, ownerships) in _sceneOwnerships)
            {
                var owned = ownerships.TryGetOwnedObjects(player);
                if (owned.Count == 0)
                    continue;

                var ownedCache = ListPool<NetworkID>.Instantiate();
                ownedCache.AddRange(owned);

                for (var i = 0; i < ownedCache.Count; i++)
                {
                    var id = ownedCache[i];
                    if (_hierarchy.TryGetIdentity(scene, id, out var identity))
                        identity.TriggerOnOwnerReconnected(player, false);
                }

                ListPool<NetworkID>.Destroy(ownedCache);
            }
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!_sceneOwnerships.TryGetValue(scene, out var ownerships)) return;

            var preOwned = ownerships.TryGetOwnedObjects(player);
            var ownedCache = ListPool<NetworkID>.Instantiate();
            ownedCache.AddRange(preOwned);

            for (var i = 0; i < ownedCache.Count; i++)
            {
                var id = ownedCache[i];
                if (_hierarchy.TryGetIdentity(scene, id, out var identity))
                    identity.TriggerOnOwnerDisconnected(player);
            }

            ListPool<NetworkID>.Destroy(ownedCache);

            OnOwnerDisconnect(player, scene, ownerships, asServer);
        }

        private void OnOwnerDisconnect(PlayerID player, SceneID scene, SceneOwnership ownerships, bool asServer)
        {
            if (!asServer)
                return;

            var owned = ownerships.TryGetOwnedObjects(player);
            var toDestroy = HashSetPool<GameObject>.Instantiate();

            foreach (var id in owned)
            {
                if (_hierarchy.TryGetIdentity(scene, id, out var identity))
                {
                    if (identity.ShouldDespawnOnOwnerDisconnect() && !identity.isSceneObject && !identity.isManualSpawn)
                        toDestroy.Add(identity.gameObject);
                }
            }

            foreach (var go in toDestroy)
            {
                if (go)
                    Object.Destroy(go);
            }

            HashSetPool<GameObject>.Destroy(toDestroy);
        }

        private void OnOwnershipChange(PlayerID player, OwnershipChangeBatch data, bool asServer)
        {
            try
            {
                var stateCount = data.state.Count;

                for (var j = 0; j < stateCount; j++)
                    HandleOwnershipBatch(data.scene, data.state[j], true);

                _manager.FlushBatchedRPCs();
                if (asServer && _scenePlayers.TryGetPlayersInScene(data.scene, out var players))
                {
                    using var copy = DisposableList<PlayerID>.Create(players.Count);
                    copy.AddRange(players);
                    copy.Remove(player);
                    _playersManager.Send(copy, data);
                }
            }
            finally
            {
                data.Dispose();
            }
        }

        private void OnOwnershipSnapshot(PlayerID player, OwnershipSnapshot data, bool asServer)
        {
            try
            {
                if (asServer)
                {
                    PurrLogger.LogWarning(
                        $"Player {player} attempted to send an authoritative ownership snapshot; ignoring it.");
                    return;
                }

                var transition = new HostMigrationTransitionOptions(data.sessionId, data.epoch);
                if (!_manager.isHostMigrationSessionValidated ||
                    transition != _manager.expectedHostMigrationSession)
                {
                    var failure = $"Received an authoritative ownership snapshot for {transition}; " +
                                  $"the active transfer is {_manager.expectedHostMigrationSession}.";
                    RecordTransferReconciliationFailure(failure);
                    PurrLogger.LogError(failure);
                    return;
                }

                if (!_sceneOwnerships.TryGetValue(data.scene, out var ownerships))
                {
                    var failure =
                        $"Failed to find ownership module for scene {data.scene} when applying an authoritative snapshot.";
                    RecordTransferReconciliationFailure(failure);
                    PurrLogger.LogError(failure);
                    return;
                }

                var exactState = new Dictionary<NetworkID, PlayerID>(data.state.Count);
                for (var i = 0; i < data.state.Count; i++)
                {
                    var info = data.state[i];
                    if (!exactState.TryAdd(info.identity, info.player))
                    {
                        var failure =
                            $"Received an authoritative ownership snapshot with duplicate identity {info.identity}.";
                        RecordTransferReconciliationFailure(failure);
                        PurrLogger.LogError(failure);
                        return;
                    }
                }

                for (var i = _pendingOwnership.Count - 1; i >= 0; i--)
                {
                    if (_pendingOwnership[i].scene == data.scene)
                        _pendingOwnership.RemoveAt(i);
                }

                var previousState = ownerships.GetState();
                using var previous = DisposableList<OwnershipInfo>.Create(previousState.Count);
                previous.AddRange(previousState);

                for (var i = 0; i < previous.Count; i++)
                {
                    var old = previous[i];
                    if (exactState.ContainsKey(old.identity))
                        continue;

                    if (_hierarchy.TryGetIdentity(data.scene, old.identity, out var identity))
                    {
                        var oldOwner = identity.GetOwner(false);
                        if (ownerships.RemoveOwnership(identity))
                            identity.TriggerOnOwnerChanged(oldOwner, null, false, false);
                    }
                    else
                    {
                        ownerships.RemoveOwnership(old.identity);
                    }
                }

                for (var i = 0; i < data.state.Count; i++)
                {
                    if (HandleOwnershipBatch(data.scene, data.state[i], true))
                        continue;

                    var failure = $"Failed to apply authoritative ownership for identity " +
                                  $"{data.state[i].identity} in scene {data.scene}.";
                    RecordTransferReconciliationFailure(failure);
                    PurrLogger.LogError(failure);
                    return;
                }

                _manager.FlushBatchedRPCs();
                _pendingExactOwnershipScenes.Remove(data.scene);
            }
            catch (Exception e)
            {
                RecordTransferReconciliationFailure(
                    $"Applying the authoritative ownership snapshot failed: {e.Message}");
                PurrLogger.LogException(e);
            }
            finally
            {
                data.Dispose();
            }
        }

        private void OnOwnershipChange(PlayerID player, OwnershipChange change, bool asServer)
        {
            try
            {
                var idCount = change.identities.Count;

                for (var j = 0; j < idCount; j++)
                {
                    if (!HandleOwnershipChange(player, change, change.identities[j], true))
                    {
                        change.identities.RemoveAt(j--);
                        idCount--;
                    }
                }

                _manager.FlushBatchedRPCs();

                if (asServer && _scenePlayers.TryGetPlayersInScene(change.sceneId, out var players))
                {
                    using var copy = DisposableList<PlayerID>.Create(players.Count);
                    copy.AddRange(players);
                    copy.Remove(player);
                    _playersManager.Send(copy, change);
                }
            }
            finally
            {
                change.Dispose();
            }
        }

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            _sceneOwnerships.Remove(scene);
            _pendingExactOwnershipScenes.Remove(scene);
        }

        private void OnSceneRebound(SceneID scene, bool asServer)
        {
            if (asServer || !_transferReconciliationTransition.canReconcile)
                return;

            if (!_sceneOwnerships.ContainsKey(scene))
                OnSceneLoaded(scene, false);
            else
                _pendingExactOwnershipScenes.Add(scene);
        }

        public void GiveOwnership(NetworkIdentity nid, PlayerID player, bool? propagateToChildren = null,
            bool? overrideExistingOwners = null, bool silent = false, bool isSpawner = false)
        {
            if (!nid)
                return;

            if (!nid.id.HasValue)
            {
                if (!silent)
                    PurrLogger.LogError(
                        $"Failed to give ownership of '{nid.gameObject.name}' to {player} because it isn't spawned.");
                return;
            }

            bool hadOwnerPreviously = nid.HasOwner(_asServer);

            switch (hadOwnerPreviously)
            {
                case true when nid.GetOwner(_asServer) == player:
                {
                    return;
                }
                case true when !nid.HasTransferOwnershipAuthority(_asServer):
                case false when !nid.HasGiveOwnershipAuthority(_asServer):
                {
                    if (!silent)
                        PurrLogger.LogError(
                            $"Failed to give ownership of '{nid.gameObject.name}' to {player} because of missing authority.");
                    return;
                }
            }

            /*if (hadOwnerPreviously)
                RemoveOwnership(nid);*/

            if (!_sceneOwnerships.TryGetValue(nid.sceneId, out var module))
            {
                if (!silent)
                    PurrLogger.LogError(
                        $"No ownership module avaible for scene {nid.sceneId} '{nid.gameObject.scene.name}'");
                return;
            }

            var shouldOverride = overrideExistingOwners ?? nid.ShouldOverrideExistingOwnership(_asServer);
            var affectedIds = ListPool<NetworkIdentity>.Instantiate();
            GetAllChildrenOrSelf(nid, affectedIds, propagateToChildren);

            using var _idsCache = DisposableList<NetworkID>.Create();
            var callbacks = ListPool<OwnershipCallback>.Instantiate();

            for (var i = 0; i < affectedIds.Count; i++)
            {
                var identity = affectedIds[i];

                if (!identity.id.HasValue) continue;

                if (identity.HasOwner(_asServer))
                {
                    if (!shouldOverride)
                        continue;

                    if (!identity.HasTransferOwnershipAuthority(_asServer))
                    {
                        if (!silent)
                            PurrLogger.LogError(
                                $"Failed to override ownership of '{identity.gameObject.name}' because of missing authority.");
                        continue;
                    }
                }

                var oldOwner = identity.GetOwner(_asServer);

                bool addedCb = module.GiveOwnership(identity, player);
                if (addedCb)
                {
                    callbacks.Add(new OwnershipCallback
                    {
                        oldOwner = oldOwner,
                        newOwner = player,
                        identity = identity,
                        isSpawner = isSpawner
                    });
                }
                _idsCache.Add(identity.id.Value);
            }

            if (_idsCache.Count == 0)
            {
                if (!silent)
                    PurrLogger.LogError(
                        $"Failed to give ownership of '{nid.gameObject.name}' to {player} because no identities were affected.");

                ListPool<NetworkIdentity>.Destroy(affectedIds);
                return;
            }

            var data = new OwnershipChange
            {
                sceneId = nid.sceneId,
                identities = _idsCache,
                isAdding = true,
                player = player,
                isSpawner = isSpawner
            };

            _manager.FlushBatchedRPCs();

            if (_asServer)
            {
                if (_scenePlayers.TryGetPlayersInScene(nid.sceneId, out var players))
                    _playersManager.Send(players, data);
            }
            else
            {
                _playersManager.SendToServer(data);
            }

            for (var i = 0; i < callbacks.Count; i++)
            {
                var info = callbacks[i];
                if (info.identity)
                    info.identity.TriggerOnOwnerChanged(info.oldOwner, info.newOwner, _asServer, info.isSpawner);
            }

            ListPool<OwnershipCallback>.Destroy(callbacks);
            ListPool<NetworkIdentity>.Destroy(affectedIds);
        }

        /// <summary>
        /// Clears all ownerships of the given identity and its children.
        /// </summary>
        public void ClearOwnerships(NetworkIdentity id, bool supressErrorMessages = false)
        {
            if (!id.id.HasValue)
            {
                PurrLogger.LogError($"Failed to remove ownership of '{id.gameObject.name}' because it isn't spawned.");
                return;
            }

            if (!id.HasOwner(_asServer))
                return;

            if (!id.HasTransferOwnershipAuthority(_asServer))
            {
                PurrLogger.LogError(
                    $"Failed to remove ownership of '{id.gameObject.name}' because of missing authority.");
                return;
            }

            if (!_sceneOwnerships.TryGetValue(id.sceneId, out var module))
            {
                PurrLogger.LogError($"No ownership module avaible for scene {id.sceneId} '{id.gameObject.scene.name}'");
                return;
            }

            var children = ListPool<NetworkIdentity>.Instantiate();
            GetAllChildrenOrSelf(id, children, true);

            using var _idsCache = DisposableList<NetworkID>.Create();

            for (var i = 0; i < children.Count; i++)
            {
                var identity = children[i];

                if (!identity.id.HasValue) continue;
                if (!identity.HasOwner(_asServer)) continue;
                if (!identity.HasTransferOwnershipAuthority(_asServer))
                {
                    if (!supressErrorMessages)
                        PurrLogger.LogError(
                            $"Failed to override ownership of '{identity.gameObject.name}' because of missing authority.");
                    continue;
                }

                _idsCache.Add(identity.id.Value);

                var oldOwner = identity.GetOwner(_asServer);

                if (module.RemoveOwnership(identity))
                    identity.TriggerOnOwnerChanged(oldOwner, null, _asServer, false);
            }

            //TODO: compress _idsCache using RLE
            var data = new OwnershipChange
            {
                sceneId = id.sceneId,
                identities = _idsCache,
                isAdding = false,
                player = default
            };

            _manager.FlushBatchedRPCs();

            if (_asServer)
            {
                if (_scenePlayers.TryGetPlayersInScene(id.sceneId, out var players))
                    _playersManager.Send(players, data);
            }
            else _playersManager.SendToServer(data);

            ListPool<NetworkIdentity>.Destroy(children);
        }

        /// <summary>
        /// Only removes ownership for the existing owner.
        /// This won't remove ownership of children with different owners.
        /// </summary>
        public void RemoveOwnership(NetworkIdentity id, bool? propagateToChildren = null,
            bool supressErrorMessages = false)
        {
            if (!id.id.HasValue)
            {
                if (!supressErrorMessages)
                    PurrLogger.LogError(
                        $"Failed to remove ownership of '{id.gameObject.name}' because it isn't spawned.");
                return;
            }

            if (!id.HasOwner(_asServer))
                return;

            if (!id.HasTransferOwnershipAuthority(_asServer))
            {
                if (!supressErrorMessages)
                    PurrLogger.LogError(
                        $"Failed to remove ownership of '{id.gameObject.name}' because of missing authority.");
                return;
            }

            if (!_sceneOwnerships.TryGetValue(id.sceneId, out var module))
            {
                if (!supressErrorMessages)
                    PurrLogger.LogError(
                        $"No ownership module avaible for scene {id.sceneId} '{id.gameObject.scene.name}'");
                return;
            }

            var originalOwner = id.GetOwner(_asServer).Value;
            var children = ListPool<NetworkIdentity>.Instantiate();
            GetAllChildrenOrSelf(id, children, propagateToChildren);

            using var _idsCache = DisposableList<NetworkID>.Create();

            for (var i = 0; i < children.Count; i++)
            {
                var identity = children[i];

                if (!identity.id.HasValue) continue;
                if (!module.TryGetOwner(identity, out var player) || player != originalOwner) continue;
                if (!identity.HasTransferOwnershipAuthority(_asServer))
                {
                    if (!supressErrorMessages)
                        PurrLogger.LogError(
                            $"Failed to override ownership of '{identity.gameObject.name}' because of missing authority.");
                    continue;
                }

                var oldOwner = identity.GetOwner(_asServer);

                if (module.RemoveOwnership(identity))
                {
                    identity.TriggerOnOwnerChanged(oldOwner, null, _asServer, false);
                    _idsCache.Add(identity.id.Value);
                }
            }

            //TODO: compress _idsCache using RLE
            var data = new OwnershipChange
            {
                sceneId = id.sceneId,
                identities = _idsCache,
                isAdding = false,
                player = default
            };

            _manager.FlushBatchedRPCs();

            if (_asServer)
            {
                if (_scenePlayers.TryGetPlayersInScene(id.sceneId, out var players))
                    _playersManager.Send(players, data);
            }
            else _playersManager.SendToServer(data);

            ListPool<NetworkIdentity>.Destroy(children);
        }

        public bool TryGetOwner(NetworkIdentity id, out PlayerID player)
        {
            if (_sceneOwnerships.TryGetValue(id.sceneId, out var module) && module.TryGetOwner(id, out player))
                return true;

            player = default;
            return false;
        }

        private void OnSceneLoaded(SceneID scene, bool asServer)
        {
            _sceneOwnerships[scene] = new SceneOwnership(asServer);

            if (!asServer && _transferReconciliationTransition.canReconcile)
                _pendingExactOwnershipScenes.Add(scene);
        }

        private void HandlePendingChanges()
        {
            HandlePendingChangesInternal(scopeScene: null);
        }

        private void HandlePendingChanges(SceneID scene)
        {
            HandlePendingChangesInternal(scopeScene: scene);
        }

        private void HandlePendingChangesInternal(SceneID? scopeScene)
        {
            if (_pendingOwnershipChanges.Count == 0)
                return;

            _manager.FlushBatchedRPCs();

            using var keysToRemove = scopeScene.HasValue
                ? DisposableList<PlayerSceneID>.Create(_pendingOwnershipChanges.Count)
                : default;

            var hasExactOwnershipRebounds = _exactOwnershipReboundPlayers.Count > 0;
            foreach (var (player, changes) in _pendingOwnershipChanges)
            {
                if (scopeScene.HasValue && player.scene != scopeScene.Value)
                    continue;

                // TODO: ACTUAL RLE HERE

                if (!_sceneOwnerships.TryGetValue(player.scene, out var ownerships))
                {
                    changes.Dispose();
                    if (scopeScene.HasValue) keysToRemove.Add(player);
                    continue;
                }

                if (hasExactOwnershipRebounds &&
                    _exactOwnershipReboundPlayers.Contains(player.player))
                {
                    changes.Dispose();
                    if (scopeScene.HasValue) keysToRemove.Add(player);
                    SendExactOwnershipSnapshot(player.player, player.scene, ownerships);
                    continue;
                }

                using var resolved = DisposableList<OwnershipInfo>.Create(changes.Count);

                for (var i = 0; i < changes.Count; i++)
                {
                    var id = changes[i].identity;
                    if (!_hierarchy.TryGetIdentity(player.scene, id, out var identity))
                        continue;
                    if (!ownerships.TryGetOwner(identity, out var currentOwner))
                        continue;
                    resolved.Add(new OwnershipInfo { identity = id, player = currentOwner });
                }

                changes.Dispose();

                if (scopeScene.HasValue) keysToRemove.Add(player);

                if (resolved.Count == 0)
                    continue;

                _playersManager.Send(player.player, new OwnershipChangeBatch
                {
                    scene = player.scene,
                    state = resolved
                });
            }

            if (scopeScene.HasValue)
            {
                for (int i = 0; i < keysToRemove.Count; i++)
                    _pendingOwnershipChanges.Remove(keysToRemove[i]);
            }
            else
            {
                _pendingOwnershipChanges.Clear();
            }
        }

        struct PendingOwnershipChanges
        {
            public SceneID scene;
            public OwnershipInfo change;
            public float timeAdded;
        }

        readonly List<PendingOwnershipChanges> _pendingOwnership = new ();

        private bool HandleOwnershipBatch(SceneID scene, OwnershipInfo change, bool addToPending)
        {
            if (!_hierarchy.TryGetIdentity(scene, change.identity, out var identity))
            {
                if (addToPending)
                {
                    _pendingOwnership.Add(new PendingOwnershipChanges
                    {
                        scene = scene,
                        change = change,
                        timeAdded = Time.time
                    });
                }
                return addToPending;
            }

            if (!identity.id.HasValue)
                return false;

            if (!identity.HasGiveOwnershipAuthority(!_asServer))
            {
                PurrLogger.LogError(
                    $"Failed to give ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.");
                return false;
            }

            if (!_sceneOwnerships.TryGetValue(scene, out var module))
            {
                PurrLogger.LogError(
                    $"Failed to find ownership module for scene {scene} when applying ownership change for identity {change.identity}");
                return false;
            }

            var oldOwner = identity.GetOwner(_asServer);

            if (oldOwner == change.player)
                return true;

            if (module.GiveOwnership(identity, change.player))
            {
                identity.TriggerOnOwnerChanged(oldOwner, change.player, _asServer, false);
                return true;
            }

            return false;
        }

        private bool HandleOwnershipChange(PlayerID actor, OwnershipChange change, NetworkID id, bool addToPending)
        {
            string verb = change.isAdding ? "give" : "remove";

            if (!_hierarchy.TryGetIdentity(change.sceneId, id, out var identity))
            {
                if (addToPending)
                {
                    _pendingOwnership.Add(new PendingOwnershipChanges
                    {
                        scene = change.sceneId,
                        change = new OwnershipInfo { identity = id, player = change.player },
                        timeAdded = Time.time
                    });
                }
                return false;
            }

            if (!_sceneOwnerships.TryGetValue(change.sceneId, out var module))
            {
                PurrLogger.LogError(
                    $"Failed to find ownership module for scene {change.sceneId} when applying ownership change for identity {id}");
                return false;
            }

            if (identity.HasOwner(_asServer))
            {
                if (!identity.HasTransferOwnershipAuthority(actor, !_asServer))
                {
                    PurrLogger.LogError(
                        $"Failed to {verb} (transfer) ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.",
                        identity);
                    return false;
                }
            }
            else if (!identity.HasGiveOwnershipAuthority(!_asServer))
            {
                PurrLogger.LogError(
                    $"Failed to {verb} ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.",
                    identity);
                return false;
            }

            var oldOwner = identity.GetOwner(_asServer);

            if (change.isAdding)
            {
                if (module.GiveOwnership(identity, change.player) && oldOwner != change.player)
                    identity.TriggerOnOwnerChanged(oldOwner, change.player, _asServer, change.isSpawner);
            }
            else
            {
                if (!identity.HasRemoveOwnershipAuthority(actor, !_asServer))
                {
                    PurrLogger.LogError(
                        $"Failed to remove ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.",
                        identity);
                }
                else if (module.RemoveOwnership(identity))
                {
                    identity.TriggerOnOwnerChanged(oldOwner, null, _asServer, false);
                }
            }

            return true;
        }

        static void GetAllChildrenOrSelf(NetworkIdentity id, List<NetworkIdentity> result, bool? propagateToChildren)
        {
            if (!id)
                return;

            bool shouldPropagate = propagateToChildren ?? id.ShouldPropagateToChildren();

            if (shouldPropagate && id.HasPropagateOwnershipAuthority())
            {
                HierarchyV2.GetComponentsInChildren(id.gameObject, result);
                for (int i = result.Count - 1; i >= 0; i--)
                {
                    if (!result[i])
                        result.RemoveAt(i);
                }
            }
            else
            {
                if (propagateToChildren == true)
                    PurrLogger.LogError(
                        $"Failed to propagate ownership of '{id.gameObject.name}' because of missing authority, assigning only to the identity.");

                result.Add(id);
            }
        }

        private void HandleAsyncPendingChanges()
        {
            const float TIMEOUT = 5f;

            for (var i = 0; i < _pendingOwnership.Count; ++i)
            {
                var change = _pendingOwnership[i];

                if (Time.time - change.timeAdded > TIMEOUT)
                {
                    _pendingOwnership.RemoveAt(i--);
                    continue;
                }

                if (!_hierarchy.TryGetIdentity(change.scene, change.change.identity, out _))
                    continue;

                HandleOwnershipBatch(change.scene, change.change, false);
                _pendingOwnership.RemoveAt(i--);
            }
        }

        public void FixedUpdate()
        {
            HandlePendingChanges();
            HandleAsyncPendingChanges();
        }

        public void PreFixedUpdate()
        {
            HandlePendingChanges();
            HandleAsyncPendingChanges();
        }
    }
}
