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

    public class GlobalOwnershipModule : INetworkModule, IFixedUpdate, IPreFixedUpdate, IPromoteToServerModule
    {
        readonly PlayersManager _playersManager;
        readonly ScenePlayersModule _scenePlayers;
        readonly HierarchyFactory _hierarchy;
        readonly NetworkManager _manager;

        readonly ScenesModule _scenes;
        readonly Dictionary<SceneID, SceneOwnership> _sceneOwnerships = new Dictionary<SceneID, SceneOwnership>();

        private bool _asServer;

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

            _scenes.onPreSceneLoaded += OnSceneLoaded;
            _scenes.onSceneUnloaded += OnSceneUnloaded;

            _hierarchy.onIdentityRemoved += OnIdentityDespawned;
            _hierarchy.onObserverAdded += OnPlayerObserverAdded;
            _hierarchy.onPreFinishSpawn += HandlePendingChanges;

            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            _scenePlayers.onPlayerLoadedScene += OnPlayerLoadedScene;

            _playersManager.onPlayerLeft += OnPlayerLeft;
            _playersManager.onPlayerJoined += OnPlayerJoined;

            _playersManager.Subscribe<OwnershipChangeBatch>(OnOwnershipChange);
            _playersManager.Subscribe<OwnershipChange>(OnOwnershipChange);
        }

        public void Disable(bool asServer)
        {
            _scenes.onPreSceneLoaded -= OnSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;

            _hierarchy.onIdentityRemoved -= OnIdentityDespawned;
            _hierarchy.onObserverAdded -= OnPlayerObserverAdded;
            _hierarchy.onPreFinishSpawn -= HandlePendingChanges;

            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            _scenePlayers.onPlayerLoadedScene -= OnPlayerLoadedScene;

            _playersManager.onPlayerLeft -= OnPlayerLeft;
            _playersManager.onPlayerJoined -= OnPlayerJoined;

            _playersManager.Unsubscribe<OwnershipChangeBatch>(OnOwnershipChange);
            _playersManager.Unsubscribe<OwnershipChange>(OnOwnershipChange);
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

            RemovePendingOwnership(identity.sceneId, identity.id.Value);
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

            if (!ownerships.TryGetOwner(target, out _))
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
                SendOwnershipSnapshot(player, scene, ownerships);

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

        private void OnPlayerLeft(PlayerID player, bool asServer)
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
            if (asServer)
                return;

            var stateCount = data.state.Count;

            for (var j = 0; j < stateCount; j++)
                HandleOwnershipBatch(data.scene, data.state[j], true);

            _manager.FlushBatchedRPCs();
        }

        private void OnOwnershipChange(PlayerID player, OwnershipChange change, bool asServer)
        {
            var idCount = change.identities.Count;
            var accepted = 0;
            using var rejected = asServer ? DisposableList<NetworkID>.Create() : default;

            for (var j = 0; j < idCount; j++)
            {
                var id = change.identities[j];
                if (HandleOwnershipChange(player, change, id, true, asServer))
                    change.identities[accepted++] = id;
                else if (asServer)
                    rejected.Add(id);
            }

            change.identities.RemoveRange(accepted, idCount - accepted);

            _manager.FlushBatchedRPCs();

            if (_asServer && accepted > 0)
                RelayOwnershipChanges(player, change.sceneId, change.identities, change);

            if (asServer && rejected.Count > 0)
                SendOwnershipState(player, change.sceneId, rejected);
        }

        private void RelayOwnershipChanges(PlayerID actor, SceneID scene, DisposableList<NetworkID> identities,
            OwnershipChange change)
        {
            if (!_sceneOwnerships.TryGetValue(scene, out var ownerships) ||
                !_scenePlayers.TryGetPlayersInScene(scene, out var players))
                return;

            using var state = DisposableList<OwnershipInfo>.Create();
            using var removed = DisposableList<NetworkID>.Create();
            using var unchanged = DisposableList<NetworkID>.Create(identities.Count);

            for (var i = 0; i < identities.Count; i++)
            {
                var id = identities[i];
                if (!_hierarchy.TryGetIdentity(scene, id, out var identity))
                    continue;

                bool hasOwner = ownerships.TryGetOwner(identity, out var owner);
                if (hasOwner == change.isAdding && (!hasOwner || owner == change.player))
                {
                    unchanged.Add(id);
                }
                else if (hasOwner)
                {
                    state.Add(new OwnershipInfo { identity = id, player = owner });
                }
                else
                {
                    removed.Add(id);
                }
            }

            if (unchanged.Count > 0)
            {
                change.identities = unchanged;
                using var copy = DisposableList<PlayerID>.Create(players.Count);
                copy.AddRange(players);
                copy.Remove(actor);
                _playersManager.Send(copy, change);
            }

            if (state.Count > 0)
                _playersManager.Send(players, new OwnershipChangeBatch { scene = scene, state = state });

            if (removed.Count > 0)
                _playersManager.Send(players, new OwnershipChange
                {
                    sceneId = scene,
                    identities = removed,
                    isAdding = false
                });
        }

        private void SendOwnershipState(PlayerID player, SceneID scene, DisposableList<NetworkID> identities)
        {
            if (!_sceneOwnerships.TryGetValue(scene, out var ownerships) ||
                !_playersManager.IsValidPlayer(player) || !_scenePlayers.IsPlayerInScene(player, scene))
                return;

            using var state = DisposableList<OwnershipInfo>.Create();
            using var removed = DisposableList<NetworkID>.Create();

            for (var i = 0; i < identities.Count; i++)
            {
                var id = identities[i];
                if (!_hierarchy.TryGetIdentity(scene, id, out var identity))
                    continue;

                if (ownerships.TryGetOwner(identity, out var owner))
                    state.Add(new OwnershipInfo { identity = id, player = owner });
                else removed.Add(id);
            }

            if (state.Count > 0)
                _playersManager.Send(player, new OwnershipChangeBatch { scene = scene, state = state });

            if (removed.Count > 0)
                _playersManager.Send(player, new OwnershipChange
                {
                    sceneId = scene,
                    identities = removed,
                    isAdding = false
                });
        }

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            _sceneOwnerships.Remove(scene);
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
            public PlayerID actor;
            public SceneID scene;
            public OwnershipInfo change;
            public bool isAdding;
            public bool isSpawner;
            public bool fromClient;
            public float timeAdded;
        }

        const int MAX_PENDING_CLIENT_CHANGES = 1024;

        readonly List<PendingOwnershipChanges> _pendingOwnership = new ();

        private void RemovePendingOwnership(SceneID scene, NetworkID id)
        {
            for (var i = 0; i < _pendingOwnership.Count; i++)
            {
                var pending = _pendingOwnership[i];
                if (pending.scene == scene && pending.change.identity == id)
                    _pendingOwnership.RemoveAt(i--);
            }
        }

        private void HandleOwnershipBatch(SceneID scene, OwnershipInfo change, bool addToPending)
        {
            HandleOwnershipChange(default, new OwnershipChange
            {
                sceneId = scene,
                player = change.player,
                isAdding = true
            }, change.identity, addToPending, false);
        }

        private bool HandleOwnershipChange(PlayerID actor, OwnershipChange change, NetworkID id, bool addToPending,
            bool fromClient)
        {
            string verb = change.isAdding ? "give" : "remove";

            if (fromClient && (!_playersManager.IsValidPlayer(actor) ||
                               !_scenePlayers.IsPlayerInScene(actor, change.sceneId)))
                return false;

            if (!_hierarchy.TryGetIdentity(change.sceneId, id, out var identity))
            {
                if (addToPending && (!fromClient || _pendingOwnership.Count < MAX_PENDING_CLIENT_CHANGES))
                {
                    _pendingOwnership.Add(new PendingOwnershipChanges
                    {
                        actor = actor,
                        scene = change.sceneId,
                        change = new OwnershipInfo { identity = id, player = change.player },
                        isAdding = change.isAdding,
                        isSpawner = change.isSpawner,
                        fromClient = fromClient,
                        timeAdded = Time.time
                    });
                }
                return false;
            }

            if (!identity.id.HasValue)
                return false;

            if (!_sceneOwnerships.TryGetValue(change.sceneId, out var module))
            {
                PurrLogger.LogError(
                    $"Failed to find ownership module for scene {change.sceneId} when applying ownership change for identity {id}");
                return false;
            }

            if (fromClient && identity.HasOwner(_asServer))
            {
                if (!identity.HasTransferOwnershipAuthority(actor, false))
                {
                    PurrLogger.LogError(
                        $"Failed to {verb} (transfer) ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.",
                        identity);
                    return false;
                }
            }
            else if (fromClient && !identity.HasGiveOwnershipAuthority(false))
            {
                PurrLogger.LogError(
                    $"Failed to {verb} ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.",
                    identity);
                return false;
            }

            if (fromClient && !change.isAdding && !identity.HasRemoveOwnershipAuthority(actor, false))
            {
                PurrLogger.LogError(
                    $"Failed to remove ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.",
                    identity);
                return false;
            }

            if (addToPending)
                RemovePendingOwnership(change.sceneId, id);

            var oldOwner = identity.GetOwner(_asServer);

            if (change.isAdding)
            {
                if (module.GiveOwnership(identity, change.player) && oldOwner != change.player)
                    identity.TriggerOnOwnerChanged(oldOwner, change.player, _asServer, change.isSpawner);
            }
            else
            {
                if (module.RemoveOwnership(identity))
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

                _pendingOwnership.RemoveAt(i--);

                var resolved = new OwnershipChange
                {
                    sceneId = change.scene,
                    player = change.change.player,
                    isAdding = change.isAdding,
                    isSpawner = change.isSpawner
                };
                if (!HandleOwnershipChange(change.actor, resolved, change.change.identity, false, change.fromClient))
                    continue;

                if (_asServer)
                {
                    using var identities = DisposableList<NetworkID>.Create(1);
                    identities.Add(change.change.identity);
                    _manager.FlushBatchedRPCs();
                    RelayOwnershipChanges(change.actor, change.scene, identities, resolved);
                }
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
