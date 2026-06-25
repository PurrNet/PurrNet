using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public delegate void IdentityAction(NetworkIdentity identity);

    public delegate void ObserverAction(PlayerID player, NetworkIdentity identity);

    public delegate void SpawnedAction(PlayerID player, SceneID scene, NetworkID identity);

    public delegate bool ValidateSpawnAction(PlayerID player, SpawnPacket data);

    public delegate void SpawnDelegate(GameObject instance, bool isSceneObject);

    public class HierarchyV2 : IPromoteToServerModule, ITransferToNewServer
    {
        private bool _asServer;

        private readonly NetworkManager _manager;
        private readonly SceneID _sceneId;
        private readonly Scene _scene;
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersManager _playersManager;
        private readonly VisilityV2 _visibility;

        private readonly HierarchyPool _scenePool;
        private readonly HierarchyPool _prefabsPool;

        private readonly List<NetworkIdentity> _spawnedIdentities = new();
        private readonly Dictionary<NetworkID, NetworkIdentity> _spawnedIdentitiesMap = new();

        private ulong _nextId;

        private bool _areSceneObjectsReady;

        /// <summary>
        /// Invoked to validate the spawning of a client-side object before it is instantiated.
        /// This event allows implementing custom rules to determine whether the object spawn
        /// should proceed or be rejected.
        /// </summary>
        public event ValidateSpawnAction onClientSpawnValidate;

        /// <summary>
        /// Fired when a NetworkIdentity is added to the hierarchy early in its lifecycle,
        /// before the standard identity initialization or observer assignment processes occur.
        /// This event is typically leveraged to perform custom logic or setup on new identities
        /// before they are fully managed by the hierarchy.
        /// </summary>
        public event IdentityAction onEarlyIdentityAdded;

        /// <summary>
        /// Triggered when a new identity is added to the network hierarchy.
        /// This event is invoked after the identity has been initialized and is ready to participate
        /// in the network lifecycle, such as spawning, synchronization, or visibility evaluation.
        /// </summary>
        public event IdentityAction onIdentityAdded;

        /// <summary>
        /// Triggered when a network identity is removed from the hierarchy.
        /// This event provides an opportunity to handle cleanup or additional logic
        /// associated with the removal of a network identity from the system.
        /// </summary>
        public event IdentityAction onIdentityRemoved;

        /// <summary>
        /// Triggered whenever a new observer is added to a networked identity.
        /// This event allows for custom logic to be executed when an observer becomes associated
        /// with a specific networked object within the hierarchy.
        /// </summary>
        public event ObserverAction onObserverAdded;

        /// <summary>
        /// Triggered after an observer has been added to a networked entity during the late evaluation phase.
        /// This event allows for additional logic to be executed after the observer is linked to the entity,
        /// such as custom visibility or state synchronization actions.
        /// </summary>
        public event ObserverAction onLateObserverAdded;

        /// <summary>
        /// Triggered when an observer is removed from the system or process.
        /// This event can be used to handle any necessary cleanup or updates
        /// associated with the removal of the observer.
        /// </summary>
        public event ObserverAction onObserverRemoved;

        /// <summary>
        /// Triggered when a spawn packet is sent to a client. This event provides details about the player,
        /// the scene, and the spawned object's identifier, enabling the implementation of custom behavior
        /// upon the transmission of spawn data.
        /// </summary>
        public event SpawnedAction onSentSpawnPacket;

        /// <summary>
        /// Fired in PostNetworkMessages after observer-add state RPCs have been flushed but before
        /// FinishSpawnPacket ships. Modules with per-spawn data that piggybacks on observer adds
        /// (e.g. GlobalOwnershipModule's pending ownership changes) flush here so their packets
        /// arrive before the receiver fires OnSpawned.
        /// </summary>
        public event Action<SceneID> onPreFinishSpawn;

        private bool _isPlayerReady;

        public HierarchyV2(NetworkManager manager, SceneID sceneId, Scene scene,
            ScenePlayersModule players, PlayersManager playersManager, bool asServer)
        {
            isReadyToSpawn = asServer;
            _manager = manager;
            _sceneId = sceneId;
            _scene = scene;
            _scenePlayers = players;
            _visibility = new VisilityV2(_manager);
            _asServer = asServer;
            _playersManager = playersManager;

            _scenePool = NetworkPoolManager.GetScenePool(scene, sceneId);
            _prefabsPool = NetworkPoolManager.GetPool(manager);

            UnityLatestUpdate.TriggerPendingAsaps();

            SetupSceneObjects(scene);
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
            _nextId = default;
            _isDisposed = false;

            // catch up with the server's next id
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (identity.id.HasValue && identity.id.Value.id.value >= _nextId)
                    _nextId = identity.id.Value.id.value + 1;

                identity.ClearObservers();
            }
        }

        public void PostPromoteToServerModule()
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var clientId = identity.GetNetworkID(false);
                if (clientId.HasValue)
                    identity.SetID(clientId.Value);

                if (identity.IsSpawned(false))
                {
                    var owner = identity.owner;
                    if (owner.HasValue)
                    {
                        identity.TriggerOnOwnerChanged(owner.Value, null, false, false);
                    }
                    identity.TriggerDespawnEvent(false);
                    identity.SetIsSpawned(false, false);
                }
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var prevOwner = identity.internalOwnerServer;
                identity.SetIdentity(_manager, this, _sceneId, _asServer, false);
                identity.internalOwnerServer = prevOwner;
                identity.TriggerEarlySpawnEvent(true);

                if (prevOwner.HasValue)
                {
                    identity.TriggerOnOwnerChanged(null, prevOwner.Value, true, false);
                    identity.TriggerOnOwnerDisconnected(prevOwner.Value);
                }

                identity.TriggerSpawnEvent(true);
            }

            RebuildSpawnedHierarchyLinks();

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                identity.TriggerPromoteToServer();
            }
        }

        private void RebuildSpawnedHierarchyLinks()
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (!identity || !identity.isSpawned)
                    continue;

                identity.parent = identity.GetNearestParent();
                identity.RecalculateNearestPath();
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (!identity || !identity.isSpawned)
                    continue;

                if (identity.gameObject.GetComponent<NetworkIdentity>() != identity)
                    continue;

                identity.RecalculateDirectChildren();
            }
        }

        readonly List<GameObjectPrototype> _defaultPrototypes = new List<GameObjectPrototype>();

        private void SetupSceneObjects(Scene scene)
        {
            if (_manager.TryGetModule<HierarchyFactory>(!_asServer, out var factory) &&
                factory.TryGetHierarchy(_sceneId, out var other))
            {
                if (other._areSceneObjectsReady)
                {
                    _areSceneObjectsReady = true;
                    return;
                }
            }

            if (_areSceneObjectsReady)
                return;

            _defaultPrototypes.Clear();

            var allSceneIdentities = ListPool<NetworkIdentity>.Instantiate();
            SceneObjectsModule.GetSceneIdentities(scene, allSceneIdentities, _manager.networkRules.ShouldIncludeInstantiatedSceneObjects());

            var roots = HashSetPool<NetworkIdentity>.Instantiate();

            var count = allSceneIdentities.Count;
            for (int i = 0; i < count; i++)
            {
                var identity = allSceneIdentities[i];
                if (!identity)
                    continue;

                var root = identity.GetRootIdentity();

                if (!root || !roots.Add(root))
                    continue;

                if (root.skipSceneAutoSpawning)
                    continue;

                var children = ListPool<NetworkIdentity>.Instantiate();
                root.GetComponentsInChildren(true, children);

                // don't spawn scene objects that don't pass the filters
                for (int j = 0; j < children.Count; j++)
                {
                    if (children[j].skipSceneAutoSpawning)
                        children.RemoveAt(j--);
                }

                var cc = children.Count;
                if (cc == 0)
                {
                    ListPool<NetworkIdentity>.Destroy(children);
                    continue;
                }

                onPreSpawn?.Invoke(root.gameObject, true);

                var pid = -i - 2;

                for (int j = 0; j < cc; j++)
                {
                    var child = children[j];

                    if (child.isSetup)
                        continue;

                    var trs = child.transform;
                    var first = trs.GetComponent<NetworkIdentity>();

                    child.PreparePrefabInfo(pid, child == first ? j : first.componentIndex, true, true);

                    if (!_asServer)
                        child.ResetIdentity();
                }

                if (_asServer)
                {
                    SpawnSceneObject(children);
                }
                else
                {
                    for (var j = 0; j < cc; j++)
                        _scenePool.RegisterActiveScenePiece(children[j]);
                }

                _defaultPrototypes.Add(HierarchyPool.GetFullPrototype(root.transform, null, true));
                ListPool<NetworkIdentity>.Destroy(children);
            }

            ListPool<NetworkIdentity>.Destroy(allSceneIdentities);
            HashSetPool<NetworkIdentity>.Destroy(roots);
            _areSceneObjectsReady = true;
        }

        public void Enable()
        {
            _enabled = true;
            PurrNetGameObjectUtils.onGameObjectCreated += OnGameObjectCreated;
            _visibility.visibilityChanged += OnVisibilityChanged;
            _scenePlayers.onPrePlayerLoadedScene += OnPlayerLoadedScene;
            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            _playersManager.onNetworkIDReceived += OnNetworkIDReceived;

            Init();

            _playersManager.Subscribe<SpawnPacketBatch>(OnSpawnPacketBatch);
            _playersManager.Subscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Subscribe<DespawnPacket>(OnDespawnPacket);
            _playersManager.Subscribe<FinishSpawnPacket>(OnFinishSpawnPacket);
            _playersManager.Subscribe<SceneSpawnReconcilePacket>(OnSceneSpawnReconcilePacket);
            _playersManager.Subscribe<ChangeParentPacket>(OnParentChangedPacket);
        }

        private void Init()
        {
            if (_playersManager.lastNid.HasValue)
                OnNetworkIDReceived(_playersManager.lastNid.Value);
            if (_playersManager.localPlayerId.HasValue)
                OnPlayerReceivedID(_playersManager.localPlayerId.Value);
            else _playersManager.onLocalPlayerReceivedID += OnPlayerReceivedID;
        }

        public void Disable()
        {
            _enabled = false;
            PurrNetGameObjectUtils.onGameObjectCreated -= OnGameObjectCreated;
            _visibility.visibilityChanged -= OnVisibilityChanged;
            _scenePlayers.onPrePlayerLoadedScene -= OnPlayerLoadedScene;
            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            _playersManager.onLocalPlayerReceivedID -= OnPlayerReceivedID;
            _playersManager.onNetworkIDReceived -= OnNetworkIDReceived;

            _playersManager.Unsubscribe<SpawnPacketBatch>(OnSpawnPacketBatch);
            _playersManager.Unsubscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Unsubscribe<DespawnPacket>(OnDespawnPacket);
            _playersManager.Unsubscribe<FinishSpawnPacket>(OnFinishSpawnPacket);
            _playersManager.Unsubscribe<SceneSpawnReconcilePacket>(OnSceneSpawnReconcilePacket);
            _playersManager.Unsubscribe<ChangeParentPacket>(OnParentChangedPacket);

            if (!_manager.isTranferingToNewServer)
                NetworkPoolManager.RemovePool(_sceneId);
        }

        private void OnSceneSpawnReconcilePacket(PlayerID player, SceneSpawnReconcilePacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            if (_asServer)
                return;

            _scenePool.ReconcileActiveScenePieces();
        }

        public void TransferToNewServer()
        {
            isReadyToSpawn = false;
            _nextId = default;
            _isPlayerReady = false;

            var hash = HashSetPool<NetworkIdentity>.Instantiate();

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var nid = _spawnedIdentities[i];
                if (!nid)
                    continue;

                var root = nid.GetRootIdentity();

                if (!root)
                    continue;

                hash.Add(root);
            }

            foreach (var r in hash)
            {
                if (!r) continue;
                Despawn(r.gameObject, true, true);
            }

            HashSetPool<NetworkIdentity>.Destroy(hash);

            Init();

            UnityLatestUpdate.TriggerPendingAsaps();
        }

        private void OnSpawnPacketBatch(PlayerID player, SpawnPacketBatch data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            int count = data.spawnPackets.Count;
            for (var i = 0; i < count; ++i)
                HandleSpawn(player, data.spawnPackets[i], false);

            count = data.despawnPackets.Count;
            for (var i = 0; i < count; ++i)
                OnDespawnPacket(player, data.despawnPackets[i], asServer);

            FlushSpawnPackets();
            data.Dispose();
        }

        bool _isDisposed;
        bool _enabled;

        public bool Cleanup()
        {
            var rules = _manager.networkRules;
            if (rules && !rules.ShouldCleanupSpawnedObjectsOnDisconnect())
                return true;

            if (_isDisposed)
                return true;

            _isDisposed = true;

            if (ApplicationContext.isQuitting)
            {
                return true;
            }

            var hash = HashSetPool<NetworkIdentity>.Instantiate();

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var nid = _spawnedIdentities[i];
                var root = nid.GetRootIdentity();

                if (!root)
                    continue;

                hash.Add(root);
            }

            foreach (var r in hash)
            {
                if (!r) continue;
                Despawn(r.gameObject, true, true);
            }

            if (!_manager.isTranferingToNewServer)
            {
                for (var i = 0; i < _defaultPrototypes.Count; i++)
                {
                    var defaultPrototype = _defaultPrototypes[i];
                    CreatePrototype(defaultPrototype, null);
                    defaultPrototype.Dispose();
                }
                _defaultPrototypes.Clear();
            }

            HashSetPool<NetworkIdentity>.Destroy(hash);
            return true;
        }

        /// <summary>
        /// Indicates whether the system is ready to spawn networked objects.
        /// This flag is typically set when the necessary conditions for spawning
        /// objects, such as proper initialization and synchronization, have been met.
        /// </summary>
        public bool isReadyToSpawn { get; private set; }

        private void OnNetworkIDReceived(NetworkID nid)
        {
            if (nid.id >= _nextId)
                _nextId = nid.id.value + 1;

            isReadyToSpawn = true;
        }

        private void OnPlayerReceivedID(PlayerID player)
        {
            _isPlayerReady = true;

            if (_asServer || !_manager.isServer)
                return;

            if (!_manager.TryGetModule<HierarchyFactory>(true, out var factory) ||
                !factory.TryGetHierarchy(_sceneId, out var serverHierarchy))
                return;

            if (!serverHierarchy._scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                return;

            serverHierarchy.CatchupClient(player);
        }

        private void OnParentChangedPacket(PlayerID player, ChangeParentPacket data, bool asserver)
        {
            // when in host mode, let the server handle the spawning on their module
            if (!_asServer && _manager.isServer)
                return;

            if (data.sceneId != _sceneId)
                return;

            if (!TryGetIdentity(data.childId, out var identity))
                return;

            if (_asServer && !identity.HasChangeParentAuthority(player, !_asServer))
            {
                PurrLogger.LogError(
                    $"Change parent failed for '{identity.gameObject.name}' due to lack of permissions.",
                    identity.gameObject);
                return;
            }

            NetworkIdentity parent = null;

            if (data.newParentId.HasValue && !TryGetIdentity(data.newParentId.Value, out parent))
            {
                PurrLogger.LogError($"Change parent failed for '{identity.gameObject.name}'. Parent `{data.newParentId.Value}` not found.",
                    identity.gameObject);
                return;
            }

            ApplyParentChange(identity, parent, data.path, true, data.worldPositionStays);

            if (_asServer)
            {
                // forward parent change to other observers
                var observers = DisposableList<PlayerID>.Create(identity.observers);
                observers.Remove(player);
                if (_playersManager.localPlayerId.HasValue)
                    observers.Remove(_playersManager.localPlayerId.Value);
                _playersManager.Send(observers, data);
            }
        }

        static NetworkIdentity ClosestParent(Transform trs)
        {
            if (!trs)
                return null;

            var parent = trs;
            while (parent)
            {
                if (parent.TryGetComponent<NetworkIdentity>(out var nid) && nid.isSpawned)
                    return nid;

                parent = parent.parent;
            }

            return null;
        }

        void ApplyParentChange(NetworkIdentity identity, NetworkIdentity parent, int[] path, bool refreshVisibility, bool worldPositionStays = true)
        {
            var idTrs = identity.transform;
            var oldParent = identity.parent;

            var tmpList = ListPool<NetworkIdentity>.Instantiate();
            identity.GetComponents(tmpList);

            var first = tmpList[0];

            for (var i = 0; i < tmpList.Count; i++)
            {
                var child = tmpList[i];
                child.parent = parent;
                child.invertedPathToNearestParent = path;
            }

            ListPool<NetworkIdentity>.Destroy(tmpList);

            var nt = identity.GetComponent<NetworkTransform>();
            if (nt) nt.StartIgnoringParentChanges();

            var nrb = identity.GetComponent<NetworkRigidbody>();
            if (nrb) nrb.StartIgnoringParentChanges();

            if (parent)
                HierarchyPool.WalkThePath(parent.transform, idTrs, path, worldPositionStays);
            else
                idTrs.SetParent(null, worldPositionStays);

            if (nt) nt.StopIgnoringParentChanges();
            if (nrb) nrb.StopIgnoringParentChanges();

            if (parent)
                parent.AddDirectChild(first);

            if (oldParent && parent != oldParent)
                oldParent.RemoveDirectChild(first);

            if (refreshVisibility && _asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    _visibility.RefreshVisibilityForGameObject(player, idTrs, parent);
                }

                FlushSpawnPackets();
            }
        }

        public void OnParentChanged(NetworkIdentity identity, Transform parent, bool worldPositionStays = true)
        {
            if (!_asServer)
            {
                if (!_playersManager.localPlayerId.HasValue)
                    return;

                bool hasAuthority = identity.HasChangeParentAuthority(_playersManager.localPlayerId.Value, _asServer);

                if (!hasAuthority)
                    return;
            }

            if (parent && parent.gameObject.scene.handle != _scene.handle)
            {
                PurrLogger.LogError($"Change parent failed for '{identity.gameObject.name}'.\n" +
                                    $"Moving networked objects to a different scene is not supported.\n" +
                                    $"Original scene: `{parent.gameObject.scene.name}`, new parent's scene: `{_scene.name}`\n" +
                                    $"Try moving the player spawner to it's own game object in the scene or toggle off `DontDestroyOnLoad` on the `NetworkManager`.",
                    identity.gameObject);
                return;
            }

            var closestNid = ClosestParent(parent);
            var oldParent = identity.parent;

            var tmpList = ListPool<NetworkIdentity>.Instantiate();
            identity.GetComponents(tmpList);

            var first = tmpList[0];
            first.parent = closestNid;
            first.RecalculateNearestPath();

            for (var i = 1; i < tmpList.Count; i++)
            {
                var child = tmpList[i];
                child.parent = closestNid;
                child.invertedPathToNearestParent = first.invertedPathToNearestParent;
            }

            ListPool<NetworkIdentity>.Destroy(tmpList);

            if (closestNid)
                closestNid.AddDirectChild(first);

            if (oldParent && oldParent != closestNid)
                oldParent.RemoveDirectChild(first);

            if (identity.id.HasValue)
            {
                var packet = new ChangeParentPacket
                {
                    sceneId = _sceneId,
                    childId = identity.id.Value,
                    newParentId = closestNid?.id,
                    path = identity.invertedPathToNearestParent,
                    worldPositionStays = worldPositionStays
                };

                _manager.FlushBatchedRPCs();
                if (_asServer)
                    _playersManager.Send(identity.observers, packet);
                else _playersManager.SendToServer(packet);
            }

            if (_asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                var trs = identity.transform;
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    _visibility.RefreshVisibilityForGameObject(player, trs, closestNid);
                }

                _manager.FlushBatchedRPCs();
                FlushSpawnPackets();
            }
        }

        private readonly Dictionary<SpawnID, DisposableList<NetworkIdentity>> _pendingSpawns = new();
        private readonly List<(SpawnID packetIdx, PlayerID player, bool asServer)> _pendingFinishSpawns = new();
        private readonly List<(PlayerID player, DespawnPacket packet, bool asServer)> _pendingDespawns = new();

        private void OnFinishSpawnPacket(PlayerID player, FinishSpawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            if (_pendingSpawns.Remove(data.packetIdx, out var list))
            {
                using (list)
                {
                    int count = list.Count;

                    switch (count)
                    {
                        case > 0 when !list[0] || !list[0].isSpawned:
                            return;
                        case > 0 when list[0] && _asServer:
                        {
                            var spawner = data.packetIdx.scope;
                            for (var i = 0; i < count; i++)
                            {
                                var nid = list[i];
                                if (!nid || !nid.isSpawned) continue;
                                if (!nid.IsObserver(spawner)) continue;
                                onObserverAdded?.Invoke(spawner, nid);
                                nid.TriggerOnPreObserverAdded(spawner, true);
                                _triggerLateObserverAdded.Add(new PlayerNid { player = spawner, nid = nid, isSpawner = true, spawnStateIncluded = true });
                            }

                            var lastNid = list[count - 1];
                            if (lastNid && lastNid.id.HasValue)
                                _playersManager.RegisterClientLastId(spawner, lastNid.id.Value);

                            if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
                            {
                                for (var i = 0; i < players.Count; i++)
                                {
                                    var playerInScene = players[i];
                                    _visibility.RefreshVisibilityForGameObject(playerInScene, list[0].transform);
                                }
                                FlushSpawnPackets();
                            }

                            DrainObserverEventsFor(list);
                            break;
                        }
                    }

                    bool isHost = IsServerHost();

                    // trigger spawn event
                    for (var i = 0; i < count; i++)
                    {
                        var nid = list[i];
                        if (!nid || !nid.isSpawned) continue;

                        nid.TriggerSpawnEvent(_asServer);
                        if (_asServer && isHost)
                            nid.TriggerSpawnEvent(false);
                        onIdentityAdded?.Invoke(nid);
                    }
                }
            }
            else
            {
                _pendingFinishSpawns.Add((data.packetIdx, player, asServer));
            }
        }

        private void DrainObserverEventsFor(DisposableList<NetworkIdentity> list)
        {
            for (int i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                var entry = _triggerLateObserverAdded[i];
                if (!ListContainsNid(list, entry.nid)) continue;
                if (!entry.nid || !entry.nid.isSpawned) continue;
                entry.nid.TriggerOnObserverAdded(entry.player, entry.isSpawner, entry.spawnStateIncluded);
                onLateObserverAdded?.Invoke(entry.player, entry.nid);
            }
            for (int i = _triggerLateObserverAdded.Count - 1; i >= 0; i--)
            {
                if (ListContainsNid(list, _triggerLateObserverAdded[i].nid))
                    _triggerLateObserverAdded.RemoveAt(i);
            }
        }

        private static bool ListContainsNid(DisposableList<NetworkIdentity> list, NetworkIdentity target)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == target) return true;
            return false;
        }

        private void ProcessBufferedFinishSpawnsFor(SpawnID packetIdx)
        {
            for (int i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                var (idx, spawner, _) = _pendingFinishSpawns[i];
                if (!idx.Equals(packetIdx))
                    continue;

                _pendingFinishSpawns.RemoveAt(i);

                if (!_pendingSpawns.Remove(packetIdx, out var list))
                    return;

                bool disposeList = true;
                try
                {
                    int count = list.Count;
                    // root destroyed before finishing: drop & dispose, never re-add (re-adding leaks the pooled list)
                    if (count > 0 && !list[0])
                        return;
                    if (count > 0 && !list[0].isSpawned)
                    {
                        _pendingSpawns.Add(packetIdx, list);
                        disposeList = false;
                        return;
                    }

                    if (count > 0 && list[0] && _asServer)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            var nid = list[j];
                            if (!nid || !nid.isSpawned) continue;
                            if (!nid.IsObserver(spawner)) continue;
                            onObserverAdded?.Invoke(spawner, nid);
                            nid.TriggerOnPreObserverAdded(spawner, true);
                            _triggerLateObserverAdded.Add(new PlayerNid { player = spawner, nid = nid, isSpawner = true, spawnStateIncluded = true });
                        }

                        var lastNid = list[count - 1];
                        if (lastNid && lastNid.id.HasValue)
                            _playersManager.RegisterClientLastId(spawner, lastNid.id.Value);

                        if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
                        {
                            for (int j = 0; j < players.Count; j++)
                                _visibility.RefreshVisibilityForGameObject(players[j], list[0].transform);
                            FlushSpawnPackets();
                        }

                        DrainObserverEventsFor(list);
                    }

                    bool isHost = IsServerHost();
                    for (int j = 0; j < count; j++)
                    {
                        var nid = list[j];
                        if (!nid || !nid.isSpawned) continue;
                        nid.TriggerSpawnEvent(_asServer);
                        if (_asServer && isHost)
                            nid.TriggerSpawnEvent(false);
                        onIdentityAdded?.Invoke(nid);
                    }
                }
                finally
                {
                    if (disposeList && !list.isDisposed)
                        list.Dispose();
                }
                return;
            }
        }

        private void ProcessBufferedDespawnsFor(DisposableList<NetworkIdentity> createdNids)
        {
            for (int i = _pendingDespawns.Count - 1; i >= 0; i--)
            {
                var (_, packet, _) = _pendingDespawns[i];

                for (int j = 0; j < createdNids.Count; j++)
                {
                    var nid = createdNids[j];
                    if (!nid || !nid.id.HasValue || nid.id.Value != packet.parentId)
                        continue;

                    _pendingDespawns.RemoveAt(i);
                    try
                    {
                        Despawn(nid.gameObject, true, true);
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError($"ProcessBufferedDespawnsFor: exception despawning {nid.gameObject.name}: {e.Message}\n{e.StackTrace}");
                    }
                    return;
                }
            }
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (!asserver)
                return;

            if (scene != _sceneId)
                return;

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];

                if (!id) continue;

                var root = id.GetRootIdentity();

                if (!root || !roots.Add(root))
                    continue;

                _visibility.ClearVisibilityForGameObject(root.transform, player);
            }
            FlushSpawnPackets();
            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        private void OnSpawnPacket(PlayerID player, SpawnPacket data, bool asServer)
        {
            HandleSpawn(player, data, true);
        }

        private void HandleSpawn(PlayerID player, SpawnPacket data, bool flushData)
        {
            if (_asServer)
                data.packetIdx.scope = player;

            if (data.sceneId != _sceneId)
                return;

            switch (_asServer)
            {
                case true when !_manager.networkRules.HasSpawnAuthority(_manager, false):
                    PurrLogger.LogError($"Spawn failed from client due to lack of permissions.");
                    RollbackSpawnOnClient(player, data);
                    return;
                // when in host mode, let the server handle the spawning on their module
                case false when _manager.isServer:
                    return;
            }

            ReplacePartialLocalHierarchy(data.prototype);

            if (data.prototype.framework.Count > 0)
            {
                for (var i = 0; i < data.prototype.framework.Count; i++)
                {
                    var piece = data.prototype.framework[i];
                    if (TryGetIdentity(piece.id, out var existing))
                    {
                        PurrLogger.LogError(
                            $"Spawn failed for player `{player}`. Identity with id `{piece.id}` already exists: `{existing.gameObject.name}`",
                            existing);
                        return;
                    }
                }
            }

            if (_asServer && onClientSpawnValidate != null)
            {
                var list = onClientSpawnValidate.GetInvocationList();
                for (var i = 0; i < list.Length; i++)
                {
                    var @delegate = list[i];
                    var validator = (ValidateSpawnAction)@delegate;
                    if (!validator(player, data))
                    {
                        var declaring = validator.Method.DeclaringType;
                        var methodName = validator.Method.Name;
                        if (data.prototype.framework.Count > 0 &&
                            _manager.prefabProvider.TryGetPrefabData(data.prototype.framework[0].pid.prefabId,
                                out var pdata) &&
                            pdata.prefab)
                        {
                            PurrLogger.LogWarning(
                                $"Spawn validation of `{pdata.prefab.name}` failed for player `{player}` by `{declaring?.Name}.{methodName}`");
                        }
                        else
                            PurrLogger.LogWarning(
                                $"Spawn validation failed for player `{player}` by `{declaring?.Name}.{methodName}`");

                        RollbackSpawnOnClient(player, data);
                        return;
                    }
                }
            }

            if (data.prototype.framework.Count > 0 && _manager.prefabProvider is IAsyncPrefabProvider asyncProvider)
            {
                int rootPrefabId = data.prototype.framework[0].pid.prefabId;
                if (_manager.prefabProvider.TryGetPrefabData(rootPrefabId, out var prefabData) && !prefabData.prefab)
                {
                    ProcessSpawnWhenLoadedAsync(data, flushData, asyncProvider, rootPrefabId);
                    return;
                }
            }

            CompleteSpawn(data, flushData);
        }

        private void ReplacePartialLocalHierarchy(GameObjectPrototype prototype)
        {
            if (_asServer || prototype.framework.Count <= 1)
                return;

            var rootId = prototype.framework[0].id;
            if (!TryGetIdentity(rootId, out var existingRoot))
                return;

            var existingPieces = 0;
            for (var i = 0; i < prototype.framework.Count; i++)
            {
                if (TryGetIdentity(prototype.framework[i].id, out _))
                    existingPieces++;
            }

            if (existingPieces >= prototype.framework.Count)
                return;

            Despawn(existingRoot.gameObject, true, true);
        }

        private async void ProcessSpawnWhenLoadedAsync(SpawnPacket data, bool flushData,
            IAsyncPrefabProvider asyncProvider, int rootPrefabId)
        {
            try
            {
                var prototypeCopy = data.prototype.Clone();
                var customDataCopy = data.customData.Duplicate();
                var packetIdx = data.packetIdx;
                var sceneId = data.sceneId;

                try
                {
                    var loaded = await asyncProvider.LoadPrefabAsync(rootPrefabId);
                    if (loaded.prefab == null)
                    {
                        PurrLogger.LogError($"ProcessSpawnWhenLoadedAsync: failed to load prefab {rootPrefabId}.");
                        prototypeCopy.Dispose();
                        customDataCopy.Dispose();
                        return;
                    }

                    if (_isDisposed)
                    {
                        prototypeCopy.Dispose();
                        customDataCopy.Dispose();
                        return;
                    }

                    var spawnData = new SpawnPacket
                    {
                        sceneId = sceneId,
                        packetIdx = packetIdx,
                        prototype = prototypeCopy,
                        customData = customDataCopy
                    };
                    CompleteSpawn(spawnData, flushData);
                    spawnData.Dispose();
                }
                catch (Exception e)
                {
                    PurrLogger.LogError($"ProcessSpawnWhenLoadedAsync: exception for prefab {rootPrefabId}: {e.Message}\n{e.StackTrace}");
                    try { prototypeCopy.Dispose(); } catch { /* ignore */ }
                    try { customDataCopy.Dispose(); } catch { /* ignore */ }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void CompleteSpawn(SpawnPacket data, bool flushData)
        {
            var createdNids = DisposableList<NetworkIdentity>.Create(16);
            var go = CreatePrototype(data.prototype, createdNids.list);
            bool hasCustomData = data.customData.bitLength > 0;

            if (!go || createdNids.Count == 0)
            {
                PurrLogger.LogError($"CompleteSpawn: CreatePrototype failed for packet {data.packetIdx}.");
                createdNids.Dispose();
                return;
            }

            try
            {
                onPreSpawn?.Invoke(go, false);
                using var scope = data.customData.AutoScope();

                if (_asServer)
                {
                    bool isHost = IsServerHost();
                    var spawner = data.packetIdx.scope;


                    for (var i = 0; i < createdNids.Count; i++)
                    {
                        var nid = createdNids[i];
                        nid.SetIdentity(_manager, this, _sceneId, _asServer, isHost);

                        if (hasCustomData)
                            nid.TriggerOnDeserialize(data.customData.packer);

                        RegisterIdentity(nid, false);
                        nid.TryAddObserver(spawner);
                    }
                }
                else
                {
                    for (var i = 0; i < createdNids.Count; i++)
                    {
                        var nid = createdNids[i];
                        nid.SetIdentity(_manager, this, _sceneId, _asServer, false);

                        if (hasCustomData)
                            nid.TriggerOnDeserialize(data.customData.packer);

                        RegisterIdentity(nid, false);
                    }
                }

                if (!_pendingSpawns.TryAdd(data.packetIdx, createdNids))
                {
                    PurrLogger.LogError($"CompleteSpawn: failed to add spawn packet {data.packetIdx} to pending spawns.");
                    createdNids.Dispose();
                    return;
                }

                ProcessBufferedFinishSpawnsFor(data.packetIdx);
                ProcessBufferedDespawnsFor(createdNids);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"CompleteSpawn: exception for packet {data.packetIdx}: {e.Message}\n{e.StackTrace}");
                createdNids.Dispose();
                return;
            }

            if (flushData)
                FlushSpawnPackets();
        }

        private void RollbackSpawnOnClient(PlayerID player, SpawnPacket data)
        {
            if (data.prototype.framework.Count > 0)
            {
                var packet = new DespawnPacket
                {
                    sceneId = _sceneId,
                    parentId = data.prototype.framework[0].id
                };
                _playersManager.Send(player, packet);
            }
        }

        private void OnDespawnPacket(PlayerID player, DespawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            if (!asServer && _manager.isServer)
            {
                // when in host mode, let the server handle the despawn on their module
                return;
            }

            if (!TryGetIdentity(data.parentId, out var identity))
            {
                if (!_asServer)
                    _pendingDespawns.Add((player, data, asServer));
                return;
            }

            if (_asServer && !identity.HasDespawnAuthority(player, !_asServer))
            {
                PurrLogger.LogError($"Despawn failed for '{identity.gameObject.name}' due to lack of permissions.",
                    identity.gameObject);
                return;
            }

            Despawn(identity.gameObject, true, true);
        }

        /// <summary>
        /// Evaluates the visibility of all spawned network identities for all players in the current scene.
        /// This operation gathers the list of players currently present in the scene and applies a visibility evaluation
        /// for each player based on the active set of spawned network identities.
        /// Intended to be called when visibility recalculations are required, such as after significant state changes.
        /// </summary>
        public void EvaluateAllVisibilities()
        {
            if (_asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
                _visibility.EvaluateAll(players, _spawnedIdentities);
            FlushSpawnPackets();
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (!_asServer)
                return;

            if (scene != _sceneId)
                return;

            if (IsServerHost() && _manager.localPlayer == player)
                CatchupClient(player);

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];

                if (!id) continue;

                if (id.isManualSpawn)
                    continue;

                var root = id.GetRootIdentity();

                if (!root || !roots.Add(root))
                    continue;

                _visibility.RefreshVisibilityForGameObject(player, root.transform);
            }

            FlushSpawnPackets();
            SendSceneSpawnReconcile(player);
            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        private void SendSceneSpawnReconcile(PlayerID player)
        {
            if (!_asServer)
                return;

            _playersManager.Send(player, new SceneSpawnReconcilePacket
            {
                sceneId = _sceneId
            });
        }

        public void EvaluateVisibilityForPlayer(PlayerID player)
        {
            if (!_asServer || !_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                return;

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];
                if (!id || id.isManualSpawn) continue;
                var root = id.GetRootIdentity();
                if (root && roots.Add(root))
                    _visibility.RefreshVisibilityForGameObject(player, root.transform);
            }

            FlushSpawnPackets();
            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        /// <summary>
        /// Evaluates the visibility of a hierarchy of objects rooted at the specified transform
        /// for all players currently present in the associated scene. This operation is intended
        /// to be used on the server to ensure that visibility states are up-to-date for all relevant players.
        /// </summary>
        /// <param name="root">The root transform of the hierarchy of objects to evaluate visibility for.</param>
        public void EvaluateVisibility(Transform root)
        {
            if (_asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                for (var index = 0; index < players.Count; index++)
                {
                    var player = players[index];
                    _visibility.RefreshVisibilityForGameObject(player, root);
                }

                FlushSpawnPackets();
            }
        }

        /// <summary>
        /// Evaluates the visibility of the specified root transform for a given player.
        /// This method checks if the player is loaded into the current scene and refreshes the visibility
        /// of the specified GameObject hierarchy. It is generally used to update client visibility
        /// when changes occur in the scene or the player's network state.
        /// </summary>
        /// <param name="player">The unique identifier of the player for whom the visibility is being evaluated.</param>
        /// <param name="root">The root transform of the GameObject hierarchy whose visibility is being evaluated.</param>
        public void EvaluateVisibility(PlayerID player, Transform root)
        {
            if (_asServer && _scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                _visibility.RefreshVisibilityForGameObject(player, root);
            FlushSpawnPackets();
        }

        private ulong _nextPacketIdx;

        struct PlayerNid
        {
            public PlayerID player;
            public NetworkIdentity nid;
            public bool isSpawner;
            public bool spawnStateIncluded;
        }

        private readonly List<PlayerNid> _triggerLateObserverAdded = new List<PlayerNid>();
        private readonly Dictionary<PlayerID, SpawnPacketBatch> _spawnPackets = new();

        private void ClearPendingLateObserverAdded(PlayerID player, NetworkIdentity id)
        {
            for (var i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                if (_triggerLateObserverAdded[i].player == player && _triggerLateObserverAdded[i].nid == id)
                    _triggerLateObserverAdded.RemoveAt(i--);
            }
        }

        private void OnVisibilityChanged(PlayerID player, Transform scope, bool isVisible)
        {
            if (isVisible)
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                if (HierarchyPool.TryGetPrototype(scope, player, children, out var prototype))
                {
                    var sentSpawnPacket = _scenePlayers.IsPlayerLoadedInScene(player, _sceneId);
                    if (sentSpawnPacket)
                    {
                        SendSpawnPacket(player, prototype, children, true);
                    }

                    for (var i = 0; i < children.Count; i++)
                    {
                        var nid = children[i];
                        onObserverAdded?.Invoke(player, nid);
                        nid.TriggerOnPreObserverAdded(player, false);
                        _triggerLateObserverAdded.Add(new PlayerNid { player = player, nid = nid, isSpawner = false, spawnStateIncluded = sentSpawnPacket });
                    }
                }
                else PurrLogger.LogError($"Failed to get prototype for '{scope.name}'.", scope);
                return;
            }

            if (scope.TryGetComponent<NetworkIdentity>(out var identity))
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                GetComponentsInChildren(identity.gameObject, children);

                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];

                    ClearPendingLateObserverAdded(player, child);
                    child.TriggerOnObserverRemoved(player);
                    onObserverRemoved?.Invoke(player, child);
                }

                ListPool<NetworkIdentity>.Destroy(children);

                if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                {
                    _manager.FlushBatchedRPCs();
                    SendDespawnPacket(player, identity, true);
                }
            }
        }

        private void SendDespawnPacket(PlayerID player, NetworkIdentity identity, bool batched)
        {
            if (!identity.id.HasValue)
                return;

            // dont send despawn packet to the local player
            if (player == _manager.localPlayer)
                return;

            var packet = new DespawnPacket
            {
                sceneId = _sceneId,
                parentId = identity.id.Value
            };

            if (batched)
            {
                if (!_spawnPackets.TryGetValue(player, out var batch))
                {
                    batch = new SpawnPacketBatch(
                        _sceneId,
                        DisposableList<SpawnPacket>.Create(),
                        DisposableList<DespawnPacket>.Create()
                    );
                    batch.despawnPackets.Add(packet);
                    _spawnPackets.Add(player, batch);
                }
                else
                {
                    batch.despawnPackets.Add(packet);
                }
            }
            else
            {
                if (player.isServer)
                    _playersManager.SendToServer(packet);
                else _playersManager.Send(player, packet);
            }
        }

        private void SendSpawnPacket(PlayerID player, GameObjectPrototype prototype, List<NetworkIdentity> spawned, bool batched)
        {
            var spawnId = new SpawnID(_nextPacketIdx++, player, _playersManager.localPlayerId);
            var data = BitPackerPool.Get();

            try
            {
                if (player != _manager.localPlayer)
                {
                    for (var i = 0; i < spawned.Count; i++)
                    {
                        var identity = spawned[i];
                        identity.TriggerOnSerialize(data);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                data.SetBitPosition(0);
            }

            var packet = new SpawnPacket
            {
                sceneId = _sceneId,
                packetIdx = spawnId,
                prototype = prototype,
                localcache = spawned,
                customData = new BitData(data)
            };

            if (batched)
            {
                if (!_spawnPackets.TryGetValue(player, out var batch))
                {
                    batch = new SpawnPacketBatch(
                        _sceneId,
                        DisposableList<SpawnPacket>.Create(),
                        DisposableList<DespawnPacket>.Create()
                    );
                    batch.spawnPackets.Add(packet);
                    _spawnPackets.Add(player, batch);
                }
                else
                {
                    batch.spawnPackets.Add(packet);
                }
            }
            else
            {
                if (player.isServer)
                    _playersManager.SendToServer(packet);
                else _playersManager.Send(player, packet);
                packet.Dispose();
                _toCompleteNextFrame.Add(spawnId);
            }
        }

        /// <summary>
        /// Called before a gameobject is spawned.
        /// Both locally and for incoming remote spawns.
        /// </summary>
        public static event SpawnDelegate onPreSpawn;

        public void OnGameObjectCreated(GameObject obj, GameObject prefab)
        {
            if (!obj)
                return;

            if (!_asServer && _manager.isServer)
                return;

            if (obj.scene.handle != _scene.handle)
                return;

            if (!_manager.prefabProvider.TryGetPrefabData(prefab, out var data))
                return;

            NetworkManager.SetupPrefabInfo(obj, data.prefabId, data.pooled);

            InternalSpawn(obj);
        }

        internal void InternalSpawn(GameObject gameObject)
        {
            if (!isReadyToSpawn)
            {
                PurrLogger.LogError("Failed to spawn object. Hierarchy module is not ready.\n" +
                                    "Use scene events to check when ready before spawning on client.", gameObject);
                return;
            }

            if (!gameObject)
                return;

            if (!gameObject.TryGetComponent<NetworkIdentity>(out var id))
            {
                PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. No NetworkIdentity found.",
                    gameObject);
                return;
            }

            if (id.isSpawned)
                return;

            if (!id.HasSpawnAuthority(_manager, _asServer))
            {
                PurrLogger.LogError($"Spawn failed from for '{gameObject.name}' due to lack of permissions.",
                    gameObject);
                return;
            }

            PlayerID scope = default;

            if (!_asServer)
            {
                if (!_playersManager.localPlayerId.HasValue)
                {
                    PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. No local player id found.",
                        gameObject);
                    return;
                }

                scope = _playersManager.localPlayerId.Value;
            }

            onPreSpawn?.Invoke(gameObject, false);

            var baseNid = new NetworkID(_nextId++, scope);
            SetupIdsLocally(id, ref baseNid);
            ApplyParentChange(id, id.parent, id.invertedPathToNearestParent, false);

            if (!_asServer)
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                var prototype = HierarchyPool.GetFullPrototype(gameObject.transform, children);
                SendSpawnPacket(default, prototype, children, false);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    _visibility.RefreshVisibilityForGameObject(player, gameObject.transform);
                }

                FlushSpawnPackets();
            }

            AutoAssignOwnership(id);
        }

        private static int _supressAutoOwner = 0;

        public static void SupressAutoOwner()
        {
            ++_supressAutoOwner;
        }

        public static void ResumeAutoOwner()
        {
            --_supressAutoOwner;
            if (_supressAutoOwner < 0)
                _supressAutoOwner = 0;
        }

        private void AutoAssignOwnership(NetworkIdentity id)
        {
            bool shouldSupressAutoOwner = _supressAutoOwner > 0;

            if (shouldSupressAutoOwner)
                return;

            if (!id.ShouldClientGiveOwnershipOnSpawn(_manager))
                return;

            PlayersManager playersManager;

            switch (_asServer)
            {
                case true when _manager.isClient:
                    playersManager = _manager.GetModule<PlayersManager>(false);
                    break;
                case false:
                    playersManager = _playersManager;
                    break;
                default:
                    return;
            }

            if (playersManager.localPlayerId.HasValue)
                id.GiveOwnershipInternal(playersManager.localPlayerId.Value, false, true);
        }

        public static void GetComponentsInChildren(GameObject go, List<NetworkIdentity> list)
        {
            if (!go)
                return;

            // workaround for the fact that GetComponents clears the list
            var tmpList = ListPool<NetworkIdentity>.Instantiate();
            int startIdx = list.Count;
            go.GetComponents(tmpList);
            list.AddRange(tmpList);
            ListPool<NetworkIdentity>.Destroy(tmpList);

            if (list.Count <= startIdx)
                return;

            var identity = list[startIdx];
            var children = identity.directChildren;
            var dcount = children.Count;

            for (int j = 0; j < dcount; j++)
            {
                var child = children[j];
                if (!child)
                    continue;
                GetComponentsInChildren(child.gameObject, list);
            }
        }

        public void Despawn(GameObject gameObject, bool bypassPermissions = false, bool bypassBroadcast = false)
        {
            var children = ListPool<NetworkIdentity>.Instantiate();
            GetComponentsInChildren(gameObject, children);

            if (children.Count == 0)
            {
                ListPool<NetworkIdentity>.Destroy(children);
                return;
            }

            int c = children.Count;
            for (int i = 0; i < c; i++)
            {
                if (!children[i].isSpawned)
                {
                    children.RemoveAt(i--);
                    --c;
                }
            }

            if (c == 0)
            {
                ListPool<NetworkIdentity>.Destroy(children);
                return;
            }
            if (!bypassPermissions &&
                !children[0].HasDespawnAuthority(_playersManager?.localPlayerId ?? default, _asServer))
            {
                PurrLogger.LogError($"Despawn failed for '{gameObject.name}' due to lack of permissions.", gameObject);
                ListPool<NetworkIdentity>.Destroy(children);
                return;
            }

            bool isHost = IsServerHost();

            // Try to despawn the object properly if despawn was on the same tick (by first calling OnSpawned)
            for (var i = 0; i < c; i++)
                CompletePendingSpawnsFor(children[i], isHost);

            if (_asServer)
            {
                _visibility.ClearVisibilityForGameObject(gameObject.transform);
                for (var i = 0; i < c; i++)
                    TriggerDespawnEvent(children[i]);
                _manager.FlushBatchedRPCs();
                FlushSpawnPackets();
            }
            else if (!bypassBroadcast)
            {
                for (var i = 0; i < c; i++)
                    TriggerDespawnEvent(children[i]);
                _manager.FlushBatchedRPCs();
                SendDespawnPacket(default, children[0], false);
            }
            else
            {
                for (var i = 0; i < c; i++)
                    TriggerDespawnEvent(children[i]);
            }

            for (var i = 0; i < c; i++)
            {
                var child = children[i];

                UnregisterIdentity(child);

                if (child.shouldBePooled)
                    child.ResetIdentity();
            }

            var pair = new PoolPair(_scenePool, _prefabsPool);
            HierarchyPool.PutBackInPool(pair, gameObject);

            ListPool<NetworkIdentity>.Destroy(children);
        }

        private void SetupIdsLocally(NetworkIdentity root, ref NetworkID baseNid)
        {
            bool isHost = IsServerHost();
            using var siblings = DisposableList<NetworkIdentity>.Create(16);
            root.GetComponents(siblings.list);

            // handle root
            for (var i = 0; i < siblings.Count; i++)
            {
                var sibling = siblings[i];
                sibling.SetID(new NetworkID(baseNid, (uint)i));
                sibling.SetIdentity(_manager, this, _sceneId, _asServer, isHost);
                RegisterIdentity(sibling, true);
            }

            // update next id
            _nextId += (uint)siblings.list.Count;
            baseNid = new NetworkID(_nextId, baseNid.scope);

            // handle children
            if (root.directChildren == null)
                return;

            for (var i = 0; i < root.directChildren.Count; i++)
            {
                SetupIdsLocally(root.directChildren[i], ref baseNid);
            }
        }

        public NetworkID ReserveNetworkID()
        {
            if (_asServer)
                return new NetworkID(_nextId++, default);
            return new NetworkID(_nextId++, _playersManager.localPlayerId ?? default);
        }

        private void SpawnSceneObject(List<NetworkIdentity> children)
        {
            bool isHost = IsServerHost();

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.isSceneObject)
                {
                    var id = new NetworkID(default, _nextId++);
                    child.SetID(id);
                    if (_asServer)
                    {
                        child.SetIdentity(_manager, this, _sceneId, _asServer, isHost);
                        RegisterIdentity(child, true);
                    }
                }
            }
        }

        private void FlushSpawnPackets()
        {
            foreach (var (player, batch) in _spawnPackets)
            {
                using (batch)
                {
                    int count = batch.spawnPackets.Count;
                    if (player.isServer)
                    {
                        _playersManager.SendToServer(batch);
                    }
                    else
                    {
                        _playersManager.Send(player, batch);

                        for (var i = 0; i < count; i++)
                        {
                            var packet = batch.spawnPackets[i];

                            if (packet.localcache != null)
                            {
                                for (var j = 0; j < packet.localcache.Count; j++)
                                {
                                    var piece = packet.localcache[j];
                                    if (!piece) continue;
                                    var pieceid = piece.id;
                                    if (!pieceid.HasValue) continue;
                                    onSentSpawnPacket?.Invoke(player, _sceneId, pieceid.Value);
                                }
                            }
                            else if (packet.prototype.framework.Count > 0)
                            {
                                for (var j = 0; j < packet.prototype.framework.Count; j++)
                                {
                                    var piece = packet.prototype.framework[j];
                                    onSentSpawnPacket?.Invoke(player, _sceneId, piece.id);
                                }
                            }
                        }
                    }

                    for (var i = 0; i < count; i++)
                        _toCompleteNextFrame.Add(batch.spawnPackets[i].packetIdx);
                }
            }

            _spawnPackets.Clear();
        }

        public void PreNetworkMessages()
        {
            _manager.FlushBatchedRPCs();
        }

        public void PostNetworkMessages()
        {
            FlushSpawnPackets();
            SendDelayedObserverEvents();
            _manager.FlushBatchedRPCs();
            onPreFinishSpawn?.Invoke(_sceneId);
            SendDelayedCompleteSpawns();
            SpawnDelayedIdentities();
        }

        private void CompletePendingSpawnsFor(NetworkIdentity toSpawn, bool isHost)
        {
            if (_toSpawnNextFrame.Remove(toSpawn))
            {
                if (!toSpawn || !toSpawn.isSpawned)
                    return;

                toSpawn.TriggerSpawnEvent(_asServer);

                if (_asServer && isHost)
                {
                    toSpawn.SetIsSpawned(true, false);
                    toSpawn.TriggerSpawnEvent(false);
                }

                onIdentityAdded?.Invoke(toSpawn);
            }
        }

        private void SendDelayedObserverEvents()
        {
            for (var i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                var nid = _triggerLateObserverAdded[i];
                if (!nid.nid || !nid.nid.isSpawned)
                    continue;

                nid.nid.TriggerOnObserverAdded(nid.player, nid.isSpawner, nid.spawnStateIncluded);
                onLateObserverAdded?.Invoke(nid.player, nid.nid);
            }

            _triggerLateObserverAdded.Clear();
        }

        private void SendDelayedCompleteSpawns()
        {
            for (var i = 0; i < _toCompleteNextFrame.Count; i++)
            {
                var toComplete = _toCompleteNextFrame[i];
                var packet = new FinishSpawnPacket
                {
                    sceneId = _sceneId,
                    packetIdx = toComplete
                };

                if (_asServer)
                    _playersManager.Send(toComplete.target, packet);
                else _playersManager.SendToServer(packet);
            }

            _toCompleteNextFrame.Clear();
        }

        private void CatchupClient(PlayerID playerId)
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];

                if (!identity.isSpawned)
                    continue;

                if (identity.IsSpawned(false))
                    continue;

                if (!identity.id.HasValue)
                    continue;

                if (_toSpawnNextFrame.Contains(identity))
                    continue;

                identity.SetIsSpawned(true, false);
                identity.TriggerEarlySpawnEvent(false);

                onSentSpawnPacket?.Invoke(playerId, _sceneId, identity.id.Value);

                if (identity.TryAddObserver(playerId))
                {
                    onObserverAdded?.Invoke(playerId, identity);
                    identity.TriggerOnPreObserverAdded(playerId, false);
                    _triggerLateObserverAdded.Add(new PlayerNid { player = playerId, nid = identity, isSpawner = false, spawnStateIncluded = false });
                }

                identity.TriggerSpawnEvent(false);
                onIdentityAdded?.Invoke(identity);
            }
        }

        private bool IsServerHost()
        {
            if (!_asServer)
                return false;

            if (_manager.TryGetModule<HierarchyFactory>(false, out var factory) &&
                factory.TryGetHierarchy(_sceneId, out var other))
            {
                return other._isPlayerReady;
            }

            return false;
        }

        private void SpawnDelayedIdentities()
        {
            bool isHost = IsServerHost();

            // swap buffers to avoid editing while iterating
            var actual = _toSpawnNextFrame;
            _toSpawnNextFrame = _toSpawnNextFrameBuffer;
            _toSpawnNextFrameBuffer = actual;

            // trigger spawn events
            foreach (var toSpawn in actual)
            {
                if (!toSpawn || !toSpawn.isSpawned) continue;

                toSpawn.TriggerSpawnEvent(_asServer);

                if (_asServer && isHost)
                {
                    toSpawn.SetIsSpawned(true, false);
                    toSpawn.TriggerSpawnEvent(false);
                }

                onIdentityAdded?.Invoke(toSpawn);
            }

            actual.Clear();
        }

        public static void SetLocalPosAndRot(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)
        {
#if UNITY_PHYSICS_3D
            var cc = t.GetComponent<CharacterController>();
            bool wasCCEnabled = cc && cc.enabled;

            if (wasCCEnabled)
                cc.enabled = false;
#endif

            t.SetLocalPositionAndRotation(pos, rot);
            t.localScale = scale;

#if UNITY_PHYSICS_3D
            if (t.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.position = t.position;
                rb.rotation = t.rotation;
            }
#endif

#if UNITY_PHYSICS_2D
            if (t.TryGetComponent<Rigidbody2D>(out var rb2d))
            {
                rb2d.position = t.position;
                rb2d.rotation = t.rotation.eulerAngles.z;
            }
#endif

#if UNITY_PHYSICS_3D
            if (wasCCEnabled)
                cc.enabled = true;
#endif
        }

        /// <summary>
        /// Creates a new GameObject instance based on the provided prototype and optionally associates it with a list of network identities.
        /// This method handles initializing the GameObject's position, rotation, scale, and parenting. If activation conditions are met,
        /// the created GameObject is activated before being returned.
        /// </summary>
        /// <param name="prototype">The prototype containing the configuration details for the GameObject to be created.</param>
        /// <param name="createdNids">An optional list of NetworkIdentity objects that will be associated with the created GameObject. Can be null.</param>
        /// <returns>The newly created GameObject configured according to the given prototype, or null if creation fails.</returns>
        public GameObject CreatePrototype(GameObjectPrototype prototype, List<NetworkIdentity> createdNids)
        {
            var pair = new PoolPair(_scenePool, _prefabsPool);

            if (!HierarchyPool.TryBuildPrototype(pair, prototype, createdNids, out var result, out var shouldActivate))
                return null;

            var resultTrs = result.transform;
            result.transform.SetParent(null, false);

            if (prototype.parentID.HasValue)
            {
                if (TryGetIdentity(prototype.parentID.Value, out var parent))
                {
                    result.transform.SetParent(parent.transform, false);
                    if (result.TryGetComponent<NetworkIdentity>(out var nid))
                        ApplyParentChange(nid, parent, prototype.path, false);
                    SetLocalPosAndRot(resultTrs, prototype.position, prototype.rotation, prototype.scale);
                }
                else
                {
                    if (result.scene != _scene)
                    {
                        try
                        {
                            SceneManager.MoveGameObjectToScene(result, _scene);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                    PurrLogger.LogError($"Failed to find parent for '{result.name}' with id '{prototype.parentID}'.",
                        result);
                }
            }
            else if (prototype.defaultParentSiblingIndex.HasValue &&
                     result.TryGetComponent<NetworkIdentity>(out var nid) && nid.defaultParent)
            {
                result.transform.SetParent(nid.defaultParent, false);
                result.transform.SetSiblingIndex(prototype.defaultParentSiblingIndex.Value);
                SetLocalPosAndRot(resultTrs, prototype.position, prototype.rotation, prototype.scale);
            }
            else
            {
                if (result.scene != _scene)
                {
                    try
                    {
                        SceneManager.MoveGameObjectToScene(result, _scene);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                SetLocalPosAndRot(resultTrs, prototype.position, prototype.rotation, prototype.scale);
            }

            if (shouldActivate && !result.activeSelf)
                result.SetActive(true);

            return result;
        }

        HashSet<NetworkIdentity> _toSpawnNextFrame = new HashSet<NetworkIdentity>();
        HashSet<NetworkIdentity> _toSpawnNextFrameBuffer = new HashSet<NetworkIdentity>();

        readonly List<SpawnID> _toCompleteNextFrame = new List<SpawnID>();

        /// <summary>
        /// For manual spawning of identities.
        /// After this call, you should call <see cref="ManualFinalizeSpawn(NetworkIdentity)"/> to finalize the spawning.
        /// This needs to be called manually on all conserned clients.
        /// </summary>
        public void ManualEarlySpawn(NetworkIdentity identity, NetworkID id)
        {
            _spawnedIdentities.Add(identity);
            _spawnedIdentitiesMap.Add(id, identity);

            bool isHost = IsServerHost();

            identity.isManualSpawn = true;
            identity.SetID(id);
            identity.SetIdentity(_manager, this, _sceneId, _asServer, isHost);

            identity.TriggerEarlySpawnEvent(_asServer);
            if (isHost) identity.TriggerEarlySpawnEvent(false);

            onEarlyIdentityAdded?.Invoke(identity);
        }

        /// <summary>
        /// For manual despawning of identities.
        /// </summary>
        public void ManualDespawn(NetworkIdentity identity)
        {
            if (!_asServer)
                return;

            var observersCopy = ListPool<PlayerID>.Instantiate();
            observersCopy.AddRange(identity.observers);
            for (var i = 0; i < observersCopy.Count; i++)
                ManualRemoveObserver(identity, observersCopy[i]);
            ListPool<PlayerID>.Destroy(observersCopy);

            TriggerDespawnEvent(identity);
            UnregisterIdentity(identity);

            identity.SetIsSpawned(false, false);
            onIdentityRemoved?.Invoke(identity);
        }

        /// <summary>
        /// For manual finalization of spawning an identity.
        /// This needs to be called manually on all conserned clients.
        /// </summary>
        public void ManualFinalizeSpawn(NetworkIdentity identity)
        {
            bool isHost = IsServerHost();

            identity.TriggerSpawnEvent(_asServer);
            if (isHost) identity.TriggerSpawnEvent(false);

            onIdentityAdded?.Invoke(identity);
        }

        /// <summary>
        /// Once the identity is created, you should call this method to refresh visibility for all players in the scene.
        /// This will send visibility updates to all players in the scene.
        /// </summary>
        public void ManualAddObserver(NetworkIdentity identity, PlayerID player)
        {
            if (!_asServer)
                return;

            if (identity.TryAddObserver(player))
            {
                onObserverAdded?.Invoke(player, identity);
                identity.TriggerOnPreObserverAdded(player, true);


                identity.TriggerOnObserverAdded(player, true);
                onLateObserverAdded?.Invoke(player, identity);

                if (identity.id.HasValue)
                    onSentSpawnPacket?.Invoke(player, _sceneId, identity.id.Value);
            }
        }

        /// <summary>
        /// Manually remove an observer from the identity.
        /// </summary>
        public void ManualRemoveObserver(NetworkIdentity identity, PlayerID player)
        {
            if (!_asServer)
                return;

            if (identity.TryRemoveObserver(player))
            {
                ClearPendingLateObserverAdded(player, identity);
                identity.TriggerOnObserverRemoved(player);
                onObserverRemoved?.Invoke(player, identity);
            }
        }

        /// <summary>
        /// Local spawn will trigger the spawn event next frame immediately after the identity is registered.
        /// </summary>
        private void RegisterIdentity(NetworkIdentity identity, bool isLocalSpawn)
        {
            if (identity.id.HasValue)
            {
                _spawnedIdentities.Add(identity);
                _spawnedIdentitiesMap.Add(identity.id.Value, identity);

                identity.TriggerEarlySpawnEvent(_asServer);
                if (_asServer && _manager.isClient)
                    identity.TriggerEarlySpawnEvent(false);

                onEarlyIdentityAdded?.Invoke(identity);

                if (isLocalSpawn)
                    _toSpawnNextFrame.Add(identity);
            }
        }

        private void TriggerDespawnEvent(NetworkIdentity identity)
        {
            if (_asServer && IsServerHost())
                identity.TriggerDespawnEvent(false);
            identity.TriggerDespawnEvent(_asServer);
        }

        private void UnregisterIdentity(NetworkIdentity identity)
        {
            if (identity.id.HasValue)
            {
                _spawnedIdentities.Remove(identity);
                _spawnedIdentitiesMap.Remove(identity.id.Value);
                onIdentityRemoved?.Invoke(identity);
            }
        }

        internal void CleanupDestroyedIdentity(NetworkIdentity identity)
        {
            _toSpawnNextFrame.Remove(identity);
            _toSpawnNextFrameBuffer.Remove(identity);

            var nid = identity.GetNetworkID(_asServer) ?? identity.id;
            if (!nid.HasValue)
                return;

            // a proper Despawn already unregistered it; nothing left to clean up
            if (!_spawnedIdentitiesMap.TryGetValue(nid.Value, out var registered) ||
                !ReferenceEquals(registered, identity))
                return;

            if (_enabled && !_isDisposed && _asServer && _playersManager != null && identity.observers.Count > 0)
            {
                using var targets = DisposableList<PlayerID>.Create(identity.observers);
                if (_playersManager.localPlayerId.HasValue)
                    targets.Remove(_playersManager.localPlayerId.Value);
                if (targets.Count > 0)
                    _playersManager.Send(targets, new DespawnPacket { sceneId = _sceneId, parentId = nid.Value });
            }

            _spawnedIdentities.Remove(identity);
            _spawnedIdentitiesMap.Remove(nid.Value);
            onIdentityRemoved?.Invoke(identity);
        }


        public bool TryGetIdentity(NetworkID id, out NetworkIdentity identity)
        {
            if (_spawnedIdentitiesMap.TryGetValue(id, out identity))
                return identity;

            if (!_asServer && _manager.isServer)
            {
                if (_manager.TryGetModule<HierarchyFactory>(true, out var factory) &&
                    factory.TryGetHierarchy(_sceneId, out var other))
                {
                    return other.TryGetIdentity(id, out identity);
                }
            }

            return false;
        }
    }
}
