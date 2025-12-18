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

            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            _scenePlayers.onPlayerLoadedScene += OnPlayerLoadedScene;

            _playersManager.onPlayerLeft += OnPlayerLeft;

            _playersManager.Subscribe<OwnershipChangeBatch>(OnOwnershipChange);
            _playersManager.Subscribe<OwnershipChange>(OnOwnershipChange);
        }

        public void Disable(bool asServer)
        {
            _scenes.onPreSceneLoaded -= OnSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;

            _hierarchy.onIdentityRemoved -= OnIdentityDespawned;
            _hierarchy.onObserverAdded -= OnPlayerObserverAdded;

            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            _scenePlayers.onPlayerLoadedScene -= OnPlayerLoadedScene;

            _playersManager.onPlayerLeft -= OnPlayerLeft;

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

            if (!ownerships.TryGetOwner(target, out var owner))
                return;

            var info = new OwnershipInfo
            {
                identity = target.id.Value,
                player = owner
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

            var owned = ownerships.TryGetOwnedObjects(player);

            foreach (var id in owned)
            {
                if (_hierarchy.TryGetIdentity(scene, id, out var identity))
                    identity.TriggerOnOwnerReconnected(player, asServer);
            }
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            foreach (var (scene, ownerships) in _sceneOwnerships)
                OnOwnerDisconnect(player, scene, ownerships, asServer);
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!_sceneOwnerships.TryGetValue(scene, out var ownerships)) return;

            OnOwnerDisconnect(player, scene, ownerships, asServer);

            var owned = ownerships.TryGetOwnedObjects(player);
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
                    bool shouldDespawn = identity.ShouldDespawnOnOwnerDisconnect();

                    if (shouldDespawn && !identity.isSceneObject && !identity.isManualSpawn)
                        toDestroy.Add(identity.gameObject);
                }
            }

            foreach (var go in toDestroy)
            {
                if (go)
                {
                    Object.Destroy(go);
                }
            }

            HashSetPool<GameObject>.Destroy(toDestroy);
        }

        private void OnOwnershipChange(PlayerID player, OwnershipChangeBatch data, bool asServer)
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

        private void OnOwnershipChange(PlayerID player, OwnershipChange change, bool asServer)
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

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            _sceneOwnerships.Remove(scene);
        }

        public void GiveOwnership(NetworkIdentity nid, PlayerID player, bool? propagateToChildren = null,
            bool? overrideExistingOwners = null, bool silent = false, bool isSpawner = false)
        {
            if (!nid.id.HasValue)
            {
                if (!silent)
                    PurrLogger.LogError(
                        $"Failed to give ownership of '{nid.gameObject.name}' to {player} because it isn't spawned.");
                return;
            }

            bool hadOwnerPreviously = nid.hasOwner;

            switch (hadOwnerPreviously)
            {
                case true when nid.owner == player:
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

                if (identity.hasOwner)
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

                if (module.GiveOwnership(identity, player))
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

            if (!id.owner.HasValue)
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
                if (!identity.hasOwner) continue;
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

            if (!id.owner.HasValue)
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

            var originalOwner = id.owner.Value;
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
        }

        private void HandlePendingChanges()
        {
            if (_pendingOwnershipChanges.Count == 0)
                return;

            _manager.FlushBatchedRPCs();

            foreach (var (player, changes) in _pendingOwnershipChanges)
            {
                // TODO: ACTUAL RLE HERE

                _playersManager.Send(player.player, new OwnershipChangeBatch
                {
                    scene = player.scene,
                    state = changes
                });

                changes.Dispose();
            }

            _pendingOwnershipChanges.Clear();
        }

        struct PendingOwnershipChanges
        {
            public SceneID scene;
            public OwnershipInfo change;
            public float timeAdded;
        }

        readonly List<PendingOwnershipChanges> _pendingOwnership = new ();

        private void HandleOwnershipBatch(SceneID scene, OwnershipInfo change, bool addToPending)
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
                return;
            }

            if (!identity.id.HasValue)
                return;

            if (!identity.HasGiveOwnershipAuthority(!_asServer))
            {
                PurrLogger.LogError(
                    $"Failed to give ownership of '{identity.gameObject.name}' to {change.player} because of missing authority.");
                return;
            }

            if (!_sceneOwnerships.TryGetValue(scene, out var module))
            {
                PurrLogger.LogError(
                    $"Failed to find ownership module for scene {scene} when applying ownership change for identity {change.identity}");
                return;
            }

            var oldOwner = identity.GetOwner(_asServer);

            if (oldOwner == change.player)
                return;

            if (module.GiveOwnership(identity, change.player))
                identity.TriggerOnOwnerChanged(oldOwner, change.player, _asServer, false);
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

            if (identity.hasOwner)
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
                module.GiveOwnership(identity, change.player);

                if (oldOwner != change.player)
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
            bool shouldPropagate = propagateToChildren ?? id.ShouldPropagateToChildren();

            if (shouldPropagate && id.HasPropagateOwnershipAuthority())
            {
                HierarchyV2.GetComponentsInChildren(id.gameObject, result);
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
