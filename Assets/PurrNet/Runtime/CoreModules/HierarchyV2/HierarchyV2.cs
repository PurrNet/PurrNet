using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
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
        private readonly struct RetainedIdentityGraphEntry
        {
            internal readonly NetworkIdentity identity;
            internal readonly NetworkID roleId;
            internal readonly NetworkIdentity parent;
            internal readonly NetworkIdentity root;
            internal readonly Transform transformParent;
            internal readonly bool isManual;

            internal RetainedIdentityGraphEntry(NetworkIdentity identity, NetworkID roleId,
                NetworkIdentity parent, NetworkIdentity root, Transform transformParent,
                bool isManual)
            {
                this.identity = identity;
                this.roleId = roleId;
                this.parent = parent;
                this.root = root;
                this.transformParent = transformParent;
                this.isManual = isManual;
            }
        }

        private sealed class RetainedSceneGraphProof : IDisposable
        {
            internal bool regularRootsOnly;
            internal readonly List<RetainedIdentityGraphEntry> identities = new();
            internal readonly Dictionary<NetworkIdentity, GameObjectPrototype> rootTopologies = new();

            public void Dispose()
            {
                foreach (var topology in rootTopologies.Values)
                    topology.Dispose();
                rootTopologies.Clear();
                identities.Clear();
            }
        }

        private bool _asServer;

        private readonly NetworkManager _manager;
        private readonly SceneID _sceneId;
        private readonly Scene _scene;
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersManager _playersManager;
        private readonly VisilityV2 _visibility;

        private readonly HierarchyPool _scenePool;
        private NetworkPoolManager.ScenePoolLease _scenePoolLease;
        private readonly HierarchyPool _prefabsPool;

        private List<NetworkIdentity> _spawnedIdentities = new();
        private Dictionary<NetworkID, NetworkIdentity> _spawnedIdentitiesMap = new();
        private ulong _nextId;

        private bool _areSceneObjectsReady;

        /// <summary>
        /// Invoked to validate the spawning of a client-side object before it is instantiated.
        /// This event allows implementing custom rules to determine whether the object spawn
        /// should proceed or be rejected.
        /// </summary>
        private readonly List<ValidateSpawnAction> _clientSpawnValidators = new();

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                if (value != null)
                    _clientSpawnValidators.Add(value);
            }
            remove
            {
                if (value == null)
                    return;

                for (int i = _clientSpawnValidators.Count - 1; i >= 0; i--)
                {
                    if (_clientSpawnValidators[i] != value)
                        continue;

                    _clientSpawnValidators.RemoveAt(i);
                    return;
                }
            }
        }

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

        internal void AppendSpawnedIdentitySnapshot(List<NetworkIdentity> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.AddRange(_spawnedIdentities);
        }

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

        private HostMigrationTransitionOptions _transferReconciliationOptions;
        private bool _transferReconciliationRequested;
        private bool _transferSessionValidated;
        private bool _transferPreambleReceived;
        private bool _transferReconciliationArmed;
        private bool _transferEndReceived;
        private bool _transferReconciliationComplete = true;
        private readonly HashSet<NetworkIdentity> _retainedTransferRoots = new();
        private readonly HashSet<NetworkIdentity> _ownedManualTransferRoots = new();
        private readonly HashSet<NetworkIdentity> _confirmedTransferRoots = new();
        private readonly Dictionary<NetworkID, NetworkIdentity> _retainedTransferRootsById = new();
        private readonly Dictionary<SpawnID, DisposableList<NetworkIdentity>>
            _pendingReconciledSpawns = new();
        private readonly List<Task> _pendingReconciliationReadiness = new();
        private readonly HashSet<NetworkIdentity> _reconciliationNotifiedIdentities = new();
        private readonly Dictionary<SpawnID, HostMigrationTransitionOptions>
            _exactBarrierBypassFinishes = new();
        private PlayerID? _buildingExactSnapshotForPlayer;
        private HostMigrationTransitionOptions _buildingExactSnapshotTransition;
        private ExactSceneSnapshotStagingJournal _activeExactSnapshotStagingJournal;
        private SceneSpawnReconcileManifest _expectedTransferSpawnManifest;
        private Exception _transferReconciliationFailure;
        private Task _promotionReadiness = Task.CompletedTask;

        private readonly struct PendingSceneReconcileEnd
        {
            internal readonly PlayerID player;
            internal readonly SceneSpawnReconcilePacket packet;
            internal readonly HierarchyV2 promotedListenClient;

            internal PendingSceneReconcileEnd(PlayerID player,
                SceneSpawnReconcilePacket packet, HierarchyV2 promotedListenClient)
            {
                this.player = player;
                this.packet = packet;
                this.promotedListenClient = promotedListenClient;
            }
        }

        private readonly List<PendingSceneReconcileEnd> _sceneReconcileEndsNextFrame = new();

        internal sealed class ExactSceneSnapshotPlan : IDisposable
        {
            internal readonly HierarchyV2 hierarchy;
            internal readonly PlayerID player;
            internal readonly HostMigrationTransitionOptions transition;
            internal readonly HierarchyV2 promotedListenClient;
            internal SceneSpawnReconcileBeginPacket preamble;
            internal bool ownsPreamble;
            internal SpawnPacketBatch batch;
            internal bool ownsBatch;
            internal SceneSpawnReconcileManifest promotedManifest;
            internal List<SpawnID> syntheticFinishes;
            internal List<NetworkIdentity> promotedNewlyRegistered;
            private ExactSceneSnapshotStagingJournal _stagingJournal;
            internal IDisposable graphProof;
            internal IDisposable promotedClientGraphProof;

            internal ExactSceneSnapshotPlan(HierarchyV2 hierarchy, PlayerID player,
                HostMigrationTransitionOptions transition,
                HierarchyV2 promotedListenClient,
                SceneSpawnReconcileBeginPacket preamble)
            {
                this.hierarchy = hierarchy;
                this.player = player;
                this.transition = transition;
                this.promotedListenClient = promotedListenClient;
                this.preamble = preamble;
                ownsPreamble = true;
            }

            internal void AttachStagingJournal(ExactSceneSnapshotStagingJournal journal)
            {
                if (_stagingJournal != null)
                    throw new InvalidOperationException(
                        $"Scene {hierarchy._sceneId} exact snapshot already owns a staging journal.");
                _stagingJournal = journal ?? throw new ArgumentNullException(nameof(journal));
            }

            internal void AcceptStaging()
            {
                _stagingJournal?.Accept();
            }

            public void Dispose()
            {
                if (ownsPreamble)
                {
                    ownsPreamble = false;
                    preamble.Dispose();
                }

                if (ownsBatch)
                {
                    ownsBatch = false;
                    batch.Dispose();
                }

                _stagingJournal?.Dispose();
                _stagingJournal = null;

                graphProof?.Dispose();
                graphProof = null;
                promotedClientGraphProof?.Dispose();
                promotedClientGraphProof = null;

                promotedManifest?.Dispose();
                promotedManifest = null;
                if (syntheticFinishes != null)
                {
                    ListPool<SpawnID>.Destroy(syntheticFinishes);
                    syntheticFinishes = null;
                }
                promotedNewlyRegistered = null;
            }
        }

        private enum ExactObserverState : byte
        {
            None,
            Observer,
            Pending
        }

        internal enum ExactObserverLifecycle : byte
        {
            Added,
            Removed
        }

        private readonly struct ExactObserverSnapshot
        {
            internal readonly NetworkIdentity identity;
            internal readonly ExactObserverState state;

            internal ExactObserverSnapshot(NetworkIdentity identity, ExactObserverState state)
            {
                this.identity = identity;
                this.state = state;
            }
        }

        private readonly struct ExactObserverLifecycleEntry
        {
            internal readonly NetworkIdentity identity;
            internal readonly ExactObserverLifecycle lifecycle;
            internal readonly bool isSpawner;

            internal ExactObserverLifecycleEntry(NetworkIdentity identity,
                ExactObserverLifecycle lifecycle, bool isSpawner)
            {
                this.identity = identity;
                this.lifecycle = lifecycle;
                this.isSpawner = isSpawner;
            }
        }

        internal sealed class ExactSceneSnapshotStagingJournal : IDisposable
        {
            private readonly HierarchyV2 _hierarchy;
            private readonly PlayerID _player;
            private readonly List<ExactObserverSnapshot> _observers = new();
            private readonly HashSet<NetworkIdentity> _capturedIdentities = new();
            private readonly List<ExactObserverLifecycleEntry> _lifecycle = new();
            private readonly List<PlayerNid> _lateObservers;
            private readonly Dictionary<SpawnID, PendingAsyncObserverSpawn> _pendingAsyncObservers;
            private readonly Dictionary<SpawnID, PendingAsyncObserverSpawn> _readyAsyncObservers;
            private readonly Dictionary<PendingAsyncObserverSpawn, bool> _asyncSentState = new();
            private readonly HashSet<(PlayerID player, NetworkID root)> _failedAsyncObserverRoots;
            private readonly List<SpawnID> _toCompleteNextFrame;
            private readonly Dictionary<SpawnID, HostMigrationTransitionOptions> _exactFinishes;
            private readonly List<PendingSceneReconcileEnd> _sceneReconcileEnds;
            private bool _accepted;
            private bool _disposed;

            internal ExactSceneSnapshotStagingJournal(HierarchyV2 hierarchy, PlayerID player)
            {
                _hierarchy = hierarchy;
                _player = player;

                for (var i = 0; i < hierarchy._spawnedIdentities.Count; i++)
                {
                    var identity = hierarchy._spawnedIdentities[i];
                    if (!identity)
                        continue;

                    var state = identity.IsObserver(player)
                        ? ExactObserverState.Observer
                        : identity.IsObserverOrPending(player)
                            ? ExactObserverState.Pending
                            : ExactObserverState.None;
                    _observers.Add(new ExactObserverSnapshot(identity, state));
                    _capturedIdentities.Add(identity);
                }

                _lateObservers = new List<PlayerNid>(hierarchy._triggerLateObserverAdded);
                _pendingAsyncObservers = new Dictionary<SpawnID, PendingAsyncObserverSpawn>(
                    hierarchy._pendingAsyncObservers);
                _readyAsyncObservers = new Dictionary<SpawnID, PendingAsyncObserverSpawn>(
                    hierarchy._readyAsyncObservers);
                foreach (var pending in _pendingAsyncObservers.Values)
                    _asyncSentState[pending] = pending.sent;
                foreach (var ready in _readyAsyncObservers.Values)
                    _asyncSentState[ready] = ready.sent;
                _failedAsyncObserverRoots = new HashSet<(PlayerID player, NetworkID root)>(
                    hierarchy._failedAsyncObserverRoots);
                _toCompleteNextFrame = new List<SpawnID>(hierarchy._toCompleteNextFrame);
                _exactFinishes = new Dictionary<SpawnID, HostMigrationTransitionOptions>(
                    hierarchy._exactBarrierBypassFinishes);
                _sceneReconcileEnds = new List<PendingSceneReconcileEnd>(
                    hierarchy._sceneReconcileEndsNextFrame);
            }

            internal void Record(NetworkIdentity identity, ExactObserverLifecycle lifecycle,
                bool isSpawner = false)
            {
                if (!_accepted && !_disposed && identity)
                    _lifecycle.Add(new ExactObserverLifecycleEntry(identity, lifecycle, isSpawner));
            }

            internal bool IsFor(PlayerID player) => _player == player;
            internal PlayerID player => _player;

            internal void Accept()
            {
                _accepted = true;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (_accepted)
                    return;

                _hierarchy.RollbackExactSnapshotStaging(this);
            }

            internal void RestoreState()
            {
                RestoreObserverMembership();

                RestorePlayerScopedDictionary(
                    _hierarchy._pendingAsyncObservers, _pendingAsyncObservers);
                RestorePlayerScopedDictionary(
                    _hierarchy._readyAsyncObservers, _readyAsyncObservers);
                foreach (var pair in _pendingAsyncObservers)
                {
                    if (pair.Key.target == _player && _asyncSentState.TryGetValue(pair.Value, out var sent))
                        pair.Value.sent = sent;
                }
                foreach (var pair in _readyAsyncObservers)
                {
                    if (pair.Key.target == _player && _asyncSentState.TryGetValue(pair.Value, out var sent))
                        pair.Value.sent = sent;
                }

                _hierarchy._failedAsyncObserverRoots.RemoveWhere(entry => entry.player == _player);
                foreach (var entry in _failedAsyncObserverRoots)
                {
                    if (entry.player == _player)
                        _hierarchy._failedAsyncObserverRoots.Add(entry);
                }

                _hierarchy._toCompleteNextFrame.RemoveAll(id => id.target == _player);
                for (var i = 0; i < _toCompleteNextFrame.Count; i++)
                {
                    if (_toCompleteNextFrame[i].target == _player)
                        _hierarchy._toCompleteNextFrame.Add(_toCompleteNextFrame[i]);
                }

                RestorePlayerScopedDictionary(
                    _hierarchy._exactBarrierBypassFinishes, _exactFinishes);

                _hierarchy._sceneReconcileEndsNextFrame.RemoveAll(end => end.player == _player);
                for (var i = 0; i < _sceneReconcileEnds.Count; i++)
                {
                    if (_sceneReconcileEnds[i].player == _player)
                        _hierarchy._sceneReconcileEndsNextFrame.Add(_sceneReconcileEnds[i]);
                }

                _hierarchy._triggerLateObserverAdded.RemoveAll(entry => entry.player == _player);
                for (var i = 0; i < _lateObservers.Count; i++)
                {
                    if (_lateObservers[i].player == _player)
                        _hierarchy._triggerLateObserverAdded.Add(_lateObservers[i]);
                }
            }

            private void RestorePlayerScopedDictionary<TValue>(
                Dictionary<SpawnID, TValue> live, Dictionary<SpawnID, TValue> snapshot)
            {
                using var toRemove = DisposableList<SpawnID>.Create(16);
                foreach (var pair in live)
                {
                    if (pair.Key.target == _player)
                        toRemove.Add(pair.Key);
                }
                for (var i = 0; i < toRemove.Count; i++)
                    live.Remove(toRemove[i]);

                foreach (var pair in snapshot)
                {
                    if (pair.Key.target == _player)
                        live[pair.Key] = pair.Value;
                }
            }

            private void RestoreObserverMembership()
            {
                for (var i = 0; i < _hierarchy._spawnedIdentities.Count; i++)
                {
                    var current = _hierarchy._spawnedIdentities[i];
                    if (current && !_capturedIdentities.Contains(current))
                        current.TryRemoveObserver(_player);
                }

                for (var i = 0; i < _observers.Count; i++)
                {
                    var snapshot = _observers[i];
                    var identity = snapshot.identity;
                    if (!identity)
                        continue;

                    identity.TryRemoveObserver(_player);
                    switch (snapshot.state)
                    {
                        case ExactObserverState.Observer:
                            identity.TryAddObserver(_player);
                            break;
                        case ExactObserverState.Pending:
                            if (identity.TryAddObserver(_player))
                                identity.TryMoveObserverToPending(_player);
                            break;
                    }
                }
            }

            internal void CompensateLifecycle()
            {
                for (var i = _lifecycle.Count - 1; i >= 0; i--)
                {
                    var entry = _lifecycle[i];
                    if (!entry.identity)
                        continue;

                    if (entry.lifecycle == ExactObserverLifecycle.Added)
                    {
                        _hierarchy.InvokeExactRollbackObserverRemoved(_player, entry.identity);
                    }
                    else
                    {
                        _hierarchy.InvokeExactRollbackObserverAdded(
                            _player, entry.identity, entry.isSpawner);
                    }
                }
            }
        }

        internal Task promotionReadiness => _promotionReadiness;

        internal SceneID sceneId => _sceneId;

        internal bool IsAwaitingExactTransferPreamble(
            HostMigrationTransitionOptions transition) =>
            !_asServer && _transferReconciliationRequested &&
            !_transferReconciliationComplete &&
            _transferReconciliationOptions == transition;

        internal bool isTransferReconciliationComplete
        {
            get
            {
                PollReconciliationReadiness();
                TryFinalizeTransferReconciliation();
                return !_transferReconciliationRequested || _transferReconciliationComplete;
            }
        }

        internal bool TryGetTransferReconciliationFailure(out Exception failure)
        {
            PollReconciliationReadiness();
            failure = _transferReconciliationFailure;
            return failure != null;
        }

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

            _scenePoolLease = NetworkPoolManager.AcquireScenePool(manager, scene, sceneId);
            _scenePool = _scenePoolLease.pool;

            try
            {
                _prefabsPool = NetworkPoolManager.GetPool(manager);
                UnityLatestUpdate.TriggerPendingAsaps();
                SetupSceneObjects(scene);
            }
            catch
            {
                ReleaseScenePoolLease();
                throw;
            }
        }

        public void PromoteToServerModule()
        {
            if (_manager.hostMigrationSession.canReconcile &&
                (TryGetAuthoritySwitchQueueFailure(out var queueFailure) ||
                 !TryValidateExactAuthoritySwitchGraph(
                     _manager, _sceneId, _scene, true, out queueFailure)))
            {
                throw new InvalidOperationException(
                    $"Scene {_sceneId} cannot begin exact server promotion while ordinary " +
                    $"hierarchy work is pending: {queueFailure}.");
            }

            _clientSpawnGeneration++;
            _deferredPrefabSpawnCount = 0;
            ClearAsyncSpawnState();
            ClearPendingReceivedSpawnTransactions();
            _pendingLocalDespawnEchoes.Dispose();

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
                identity.ReconcileClientRoleAsServer(this);
            }
        }

        internal bool TryGetAuthoritySwitchQueueFailure(out string failure)
        {
            List<string> pending = null;

            void Add(string name, int count)
            {
                if (count <= 0)
                    return;
                pending ??= new List<string>();
                pending.Add($"{name}={count}");
            }

            Add("spawn-lifecycle", _toSpawnNextFrame.Count);
            Add("spawn-lifecycle-buffer", _toSpawnNextFrameBuffer.Count);
            Add("outgoing-finish", _toCompleteNextFrame.Count);
            Add("outgoing-batches", _spawnPackets.Count);
            Add("late-observer-callbacks", _triggerLateObserverAdded.Count);
            Add("reconcile-end", _sceneReconcileEndsNextFrame.Count);
            Add("incoming-spawns", _pendingSpawns.Count);
            Add("early-finish", _pendingFinishSpawns.Count);
            Add("early-despawn", _pendingDespawns.Count);
            Add("deferred-prefab", _deferredPrefabSpawnCount);
            Add("async-client-spawns", _asyncPendingSpawns.Count);
            Add("async-observer-spawns", _pendingAsyncObservers.Count);
            Add("ready-async-observers", _readyAsyncObservers.Count);
            if (!_pendingLocalDespawnEchoes.isDisposed)
                Add("local-despawn-echoes", _pendingLocalDespawnEchoes.Count);
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            Add("async-instantiations", _pendingAsyncInstantiations.Count);
#endif

            if (pending == null)
            {
                failure = null;
                return false;
            }

            failure = $"scene {_sceneId}: {string.Join(", ", pending)}";
            return true;
        }

        internal bool TryPreflightExactStaleSceneRetirement(
            Scene claimedScene, bool asServer, out string failure)
        {
            failure = null;
            if (_asServer != asServer || !_enabled || _isDisposed)
            {
                failure = $"scene {_sceneId} is not an enabled retained " +
                          $"{(asServer ? "server" : "client")} hierarchy";
                return false;
            }

            if (_scene != claimedScene || !_scene.IsValid() || !_scene.isLoaded)
            {
                failure = $"scene {_sceneId} is not bound to the claimed loaded Unity scene";
                return false;
            }

            if (_transferReconciliationFailure != null)
            {
                failure = $"scene {_sceneId} already rejected exact reconciliation: " +
                          _transferReconciliationFailure.Message;
                return false;
            }

            if (TryGetAuthoritySwitchQueueFailure(out failure))
            {
                failure = $"scene {_sceneId} cannot retire its stale hierarchy while work is pending: " +
                          failure;
                return false;
            }

            if (!TryValidateRetainedSceneMembership(out failure))
                return false;

            return TryValidateStaleRetirementPhysicalRoster(out failure);
        }

        internal bool TryRetireExactStaleSceneHierarchy(
            Scene claimedScene, bool asServer, out string failure)
        {
            if (!TryPreflightExactStaleSceneRetirement(
                    claimedScene, asServer, out failure))
                return false;

            if (_spawnedIdentities.Count == 0)
                return true;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            var preservedRoots = HashSetPool<NetworkIdentity>.Instantiate();
            var regularRoots = HashSetPool<NetworkIdentity>.Instantiate();
            identities.AddRange(_spawnedIdentities);

            try
            {
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    var root = identity ? identity.GetRootIdentity() : null;
                    if (root && (identity.isManualSpawn || identity.IsSpawned(!_asServer)))
                        preservedRoots.Add(root);
                }

                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    var root = identity ? identity.GetRootIdentity() : null;
                    if (root && !preservedRoots.Contains(root))
                        regularRoots.Add(root);
                }

                foreach (var root in regularRoots)
                {
                    try
                    {
                        Despawn(root.gameObject, true, true);
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError(
                            $"Scene {_sceneId} could not physically retire stale root " +
                            $"'{root.name}'; preserving it after network-role retirement: {e.Message}");
                        PurrLogger.LogException(e);
                    }
                }

                for (var i = identities.Count - 1; i >= 0; i--)
                    RetireStaleIdentityRoleIfRegistered(identities[i]);

                if (_spawnedIdentities.Count != 0 || _spawnedIdentitiesMap.Count != 0)
                {
                    failure = $"scene {_sceneId} still owns {_spawnedIdentities.Count} list and " +
                              $"{_spawnedIdentitiesMap.Count} mapped identities after stale retirement";
                    return false;
                }

                failure = null;
                return true;
            }
            finally
            {
                HashSetPool<NetworkIdentity>.Destroy(regularRoots);
                HashSetPool<NetworkIdentity>.Destroy(preservedRoots);
                ListPool<NetworkIdentity>.Destroy(identities);
            }
        }

        private void RetireStaleIdentityRoleIfRegistered(NetworkIdentity identity)
        {
            if (!identity)
                return;

            var roleId = identity.GetNetworkID(_asServer);
            if (!roleId.HasValue ||
                !_spawnedIdentitiesMap.TryGetValue(roleId.Value, out var registered) ||
                !ReferenceEquals(registered, identity))
                return;

            if (identity.IsSpawned(_asServer))
                ManualDespawn(identity);
            else
                UnregisterIdentity(identity);
        }

        internal bool TryValidateExactAuthoritySwitchGraph(NetworkManager claimedManager,
            SceneID claimedSceneId, Scene claimedScene, bool promotion, out string failure)
        {
            failure = null;
            if (_asServer || !ReferenceEquals(_manager, claimedManager))
            {
                failure = $"scene {_sceneId} is not a retained client hierarchy for the claimed manager";
                return false;
            }

            if (!_enabled || _isDisposed)
            {
                failure = $"scene {_sceneId} retained client hierarchy is disabled or disposed";
                return false;
            }

            if (_sceneId != claimedSceneId || _scene != claimedScene ||
                !_scene.IsValid() || !_scene.isLoaded)
            {
                failure = $"scene {_sceneId} is not bound to its claimed loaded Unity scene";
                return false;
            }

            if (_spawnedIdentities.Count != _spawnedIdentitiesMap.Count)
            {
                failure = $"scene {_sceneId} identity list/map counts differ " +
                          $"({_spawnedIdentities.Count}/{_spawnedIdentitiesMap.Count})";
                return false;
            }

            var registered = new HashSet<NetworkIdentity>();
            var clientIds = new HashSet<NetworkID>();
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (!identity)
                {
                    failure = $"scene {_sceneId} identity list contains a destroyed entry";
                    return false;
                }

                if (!registered.Add(identity))
                {
                    failure = $"scene {_sceneId} identity list contains {identity.name} more than once";
                    return false;
                }

                if (!ReferenceEquals(identity.networkManager, _manager) ||
                    identity.sceneId != _sceneId || identity.gameObject.scene != _scene)
                {
                    failure = $"scene {_sceneId} identity {identity.name} has drifted from its " +
                              "manager, SceneID, or physical Unity scene";
                    return false;
                }

                if (!identity.CanRetainClientRoleForExactAuthoritySwitch(
                        this, promotion, out var clientId, out var roleFailure))
                {
                    failure = $"scene {_sceneId} identity {identity.name} is not stable: {roleFailure}";
                    return false;
                }

                if (!clientIds.Add(clientId))
                {
                    failure = $"scene {_sceneId} client-role NetworkID {clientId} is duplicated";
                    return false;
                }

                if (!_spawnedIdentitiesMap.TryGetValue(clientId, out var mapped) ||
                    !ReferenceEquals(mapped, identity))
                {
                    failure = $"scene {_sceneId} identity list/map disagree at client-role " +
                              $"NetworkID {clientId}";
                    return false;
                }
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity.gameObject.GetComponent<NetworkIdentity>();
                if (!root || !registered.Contains(root))
                {
                    failure = $"scene {_sceneId} identity {identity.name} has no registered stable root";
                    return false;
                }

                var ancestry = new HashSet<NetworkIdentity> { identity };
                var current = identity.parent;
                while (!ReferenceEquals(current, null))
                {
                    if (!current)
                    {
                        failure = $"scene {_sceneId} identity {identity.name} has a destroyed parent";
                        return false;
                    }

                    if (!registered.Contains(current))
                    {
                        failure = $"scene {_sceneId} identity {identity.name} has an unregistered parent";
                        return false;
                    }

                    if (!ancestry.Add(current))
                    {
                        failure = $"scene {_sceneId} identity {identity.name} has a cyclic root chain";
                        return false;
                    }

                    root = current;
                    current = current.parent;
                }

                var rootId = root.GetNetworkID(false);
                if (!rootId.HasValue ||
                    !_spawnedIdentitiesMap.TryGetValue(rootId.Value, out var mappedRoot) ||
                    !ReferenceEquals(mappedRoot, root))
                {
                    failure = $"scene {_sceneId} identity {identity.name} resolves to an unstable root";
                    return false;
                }
            }

            return true;
        }

        private void ClearLegacyAuthoritySwitchQueues()
        {
            _toSpawnNextFrame.Clear();
            _toSpawnNextFrameBuffer.Clear();
            _toCompleteNextFrame.Clear();
            _exactBarrierBypassFinishes.Clear();
            _triggerLateObserverAdded.Clear();
            _sceneReconcileEndsNextFrame.Clear();

            foreach (var batch in _spawnPackets.Values)
                batch.Dispose();
            _spawnPackets.Clear();
        }

        public void PostPromoteToServerModule()
        {
            RebuildSpawnedHierarchyLinks();

            List<Task> readiness = null;

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var task = identity.TriggerPromoteToServer(_manager.hostMigrationSession);
                if (task.IsCompletedSuccessfully)
                    continue;

                readiness ??= new List<Task>();
                readiness.Add(task);
            }

            _promotionReadiness = readiness == null
                ? Task.CompletedTask
                : Task.WhenAll(readiness);
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
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            UnityProxy.onAsyncInstantiateCompleted += OnAsyncInstantiateCompleted;
#endif
            _visibility.visibilityChanged += OnVisibilityChanged;
            _scenePlayers.onPrePlayerLoadedScene += OnPlayerLoadedScene;
            _scenePlayers.onPrePlayerSceneReboundInternal += OnPlayerLoadedScene;
            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            _playersManager.onNetworkIDReceived += OnNetworkIDReceived;

            Init();

            _playersManager.Subscribe<SpawnPacketBatch>(OnSpawnPacketBatch);
            _playersManager.Subscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Subscribe<DespawnPacket>(OnDespawnPacket);
            _playersManager.Subscribe<FinishSpawnPacket>(OnFinishSpawnPacket);
            _playersManager.Subscribe<AsyncSpawnReadyPacket>(OnAsyncSpawnReadyPacket);
            _playersManager.Subscribe<SceneSpawnReconcileBeginPacket>(OnSceneSpawnReconcileBeginPacket);
            _playersManager.Subscribe<SceneSpawnReconcilePacket>(OnSceneSpawnReconcilePacket);
            _playersManager.Subscribe<SceneSpawnReconcileAbortPacket>(OnSceneSpawnReconcileAbortPacket);
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
            try
            {
                DisableCore();
            }
            finally
            {
                ReleaseScenePoolLease();
            }
        }

        private void DisableCore()
        {
            _enabled = false;
            _clientSpawnGeneration++;
            _deferredPrefabSpawnCount = 0;
            ClearAsyncSpawnState();
            ClearTransferReconciliationState();
            _sceneReconcileEndsNextFrame.Clear();
            _exactBarrierBypassFinishes.Clear();
            _cachedPrefabAsyncShapes.Clear();
            _pendingLocalDespawnEchoes.Dispose();
            PurrNetGameObjectUtils.onGameObjectCreated -= OnGameObjectCreated;
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            UnityProxy.onAsyncInstantiateCompleted -= OnAsyncInstantiateCompleted;
#endif
            _visibility.visibilityChanged -= OnVisibilityChanged;
            _scenePlayers.onPrePlayerLoadedScene -= OnPlayerLoadedScene;
            _scenePlayers.onPrePlayerSceneReboundInternal -= OnPlayerLoadedScene;
            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            _playersManager.onLocalPlayerReceivedID -= OnPlayerReceivedID;
            _playersManager.onNetworkIDReceived -= OnNetworkIDReceived;

            _playersManager.Unsubscribe<SpawnPacketBatch>(OnSpawnPacketBatch);
            _playersManager.Unsubscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Unsubscribe<DespawnPacket>(OnDespawnPacket);
            _playersManager.Unsubscribe<FinishSpawnPacket>(OnFinishSpawnPacket);
            _playersManager.Unsubscribe<AsyncSpawnReadyPacket>(OnAsyncSpawnReadyPacket);
            _playersManager.Unsubscribe<SceneSpawnReconcileBeginPacket>(OnSceneSpawnReconcileBeginPacket);
            _playersManager.Unsubscribe<SceneSpawnReconcilePacket>(OnSceneSpawnReconcilePacket);
            _playersManager.Unsubscribe<SceneSpawnReconcileAbortPacket>(OnSceneSpawnReconcileAbortPacket);
            _playersManager.Unsubscribe<ChangeParentPacket>(OnParentChangedPacket);

        }

        private void ReleaseScenePoolLease()
        {
            _scenePoolLease?.Dispose();
            _scenePoolLease = null;
        }

        private void OnSceneSpawnReconcileBeginPacket(PlayerID player,
            SceneSpawnReconcileBeginPacket data, bool asServer)
        {
            try
            {
                if (_asServer || data.sceneId != _sceneId || !_transferReconciliationRequested)
                    return;

                var transition = new HostMigrationTransitionOptions(data.sessionId, data.epoch);
                var accepted = TryAcceptTransferPreamble(ref data);
                if (_manager.TryGetModule<HierarchyFactory>(false, out var factory))
                {
                    factory.RegisterExactTransferPreamble(
                        this, transition, accepted,
                        accepted ? null : _transferReconciliationFailure?.Message);
                }
                else if (accepted)
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} accepted an exact topology preamble without a client hierarchy coordinator.");
                }
            }
            finally
            {
                data.Dispose();
            }
        }

        private bool TryAcceptTransferPreamble(ref SceneSpawnReconcileBeginPacket data)
        {
            if (_asServer || data.sceneId != _sceneId || !_transferReconciliationRequested)
                return false;

            var descriptor = new HostMigrationTransitionOptions(data.sessionId, data.epoch);
            if (!_transferSessionValidated || !descriptor.canReconcile ||
                descriptor != _transferReconciliationOptions)
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} received a host-migration preamble for an unexpected session ({descriptor}).");
                return false;
            }

            if (_transferPreambleReceived || _expectedTransferSpawnManifest != null)
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} received more than one host-migration manifest preamble.");
                return false;
            }

            if (!TryValidateRetainedSceneMembership(out var membershipFailure))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} retained graph changed before its topology preflight: " +
                    membershipFailure);
                return false;
            }

            if (!TryCreateTransferSpawnManifest(data.spawns, out var manifest, out var failure))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} rejected its host-migration topology preflight: {failure}.");
                return false;
            }

            data.spawns = default;
            _expectedTransferSpawnManifest = manifest;
            _transferPreambleReceived = true;
            return true;
        }

        private bool TryCreateTransferSpawnManifest(
            DisposableList<SceneSpawnReconcileSpawnTopology> topologies,
            out SceneSpawnReconcileManifest manifest, out string failure)
        {
            manifest = null;
            failure = null;
            var existingRootByIdentity = new Dictionary<NetworkID, NetworkID>();
            var retainedTopologyByRoot = new Dictionary<NetworkID, GameObjectPrototype>();

            try
            {
                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    var root = identity ? identity.GetRootIdentity() : null;
                    if (!identity || !identity.id.HasValue || !root || !root.id.HasValue)
                    {
                        failure = "the retained registry contains an identity without a stable root or NetworkID";
                        return false;
                    }

                    if (!existingRootByIdentity.TryAdd(identity.id.Value, root.id.Value))
                    {
                        failure = $"the retained registry contains duplicate NetworkID {identity.id.Value}";
                        return false;
                    }
                }

                foreach (var retainedRoot in _retainedTransferRoots)
                {
                    if (!retainedRoot || !retainedRoot.id.HasValue)
                    {
                        failure = "a retained root lost its NetworkID before the topology preflight";
                        return false;
                    }

                    var topology = HierarchyPool.GetFullPrototype(retainedRoot.transform, null, true);
                    if (!retainedTopologyByRoot.TryAdd(retainedRoot.id.Value, topology))
                    {
                        topology.Dispose();
                        failure = $"retained root {retainedRoot.id.Value} is registered more than once";
                        return false;
                    }
                }

                return SceneSpawnReconcileManifest.TryCreate(topologies,
                    existingRootByIdentity, retainedTopologyByRoot, out manifest, out failure);
            }
            catch (Exception exception)
            {
                failure = $"the retained topology could not be captured: {exception.Message}";
                return false;
            }
            finally
            {
                foreach (var topology in retainedTopologyByRoot.Values)
                    topology.Dispose();
            }
        }

        private void OnSceneSpawnReconcilePacket(PlayerID player, SceneSpawnReconcilePacket data, bool asServer)
        {
            if (data.sceneId != _sceneId || _asServer)
                return;

            if (!_transferReconciliationRequested)
            {
                _scenePool.ReconcileActiveScenePieces();
                return;
            }

            var descriptor = new HostMigrationTransitionOptions(data.sessionId, data.epoch);
            if (!_transferSessionValidated || !_transferPreambleReceived ||
                !descriptor.canReconcile || descriptor != _transferReconciliationOptions)
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} received a host-migration end marker without a matching preamble.");
                return;
            }

            if (!TryAuthorizeTransactionWideExactSnapshot(out var transactionFailure))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} received its host-migration end marker before the complete " +
                    $"scene topology set was proven: {transactionFailure}");
                return;
            }

            if (_expectedTransferSpawnManifest == null ||
                _expectedTransferSpawnManifest.unconsumedCount != 0)
            {
                SpawnID unconsumed = default;
                _expectedTransferSpawnManifest?.TryGetFirstUnconsumed(out unconsumed);
                AbortTransferReconciliation(
                    $"Scene {_sceneId} received its host-migration end marker with " +
                    $"{_expectedTransferSpawnManifest?.unconsumedCount ?? 0} unconsumed spawn entries" +
                    (_expectedTransferSpawnManifest?.unconsumedCount > 0
                        ? $" (first: {unconsumed})."
                        : "."));
                return;
            }

            _transferEndReceived = true;
            TryFinalizeTransferReconciliation();
        }

        private void OnSceneSpawnReconcileAbortPacket(PlayerID player,
            SceneSpawnReconcileAbortPacket data, bool asServer)
        {
            if (_asServer || data.sceneId != _sceneId || !_transferReconciliationRequested)
                return;

            var descriptor = new HostMigrationTransitionOptions(data.sessionId, data.epoch);
            if (!descriptor.canReconcile || descriptor != _transferReconciliationOptions)
                return;

            var reason = string.IsNullOrWhiteSpace(data.reason)
                ? $"Scene {_sceneId} exact spawn snapshot was rejected by the new authority."
                : data.reason;
            AbortTransferReconciliation(reason);
        }

        public void TransferToNewServer()
        {
            var exactTransfer = _manager.expectedHostMigrationSession.canReconcile;
            if (exactTransfer &&
                (TryGetAuthoritySwitchQueueFailure(out var queueFailure) ||
                 !TryValidateExactAuthoritySwitchGraph(
                     _manager, _sceneId, _scene, false, out queueFailure)))
            {
                throw new InvalidOperationException(
                    $"Scene {_sceneId} cannot begin exact client transfer while ordinary " +
                    $"hierarchy work is pending: {queueFailure}.");
            }

            _clientSpawnGeneration++;
            _deferredPrefabSpawnCount = 0;
            ClearAsyncSpawnState();
            ClearPendingReceivedSpawnTransactions();
            _sceneReconcileEndsNextFrame.Clear();
            _exactBarrierBypassFinishes.Clear();
            _pendingLocalDespawnEchoes.Dispose();
            isReadyToSpawn = false;
            _nextId = default;
            _isPlayerReady = false;

            if (exactTransfer)
                BeginTransferReconciliation();
            else
            {
                ClearLegacyAuthoritySwitchQueues();
                ClearTransferReconciliationState();
                _transferReconciliationComplete = true;
                DestroyAllSpawnedRoots();
            }

            Init();
            UnityLatestUpdate.TriggerPendingAsaps();
        }

        private void BeginTransferReconciliation()
        {
            ClearTransferReconciliationState();
            _transferReconciliationOptions = _manager.expectedHostMigrationSession;
            _transferReconciliationRequested = _transferReconciliationOptions.canReconcile;
            _transferReconciliationComplete = !_transferReconciliationRequested;
            if (!_transferReconciliationRequested)
                return;

            if (!TryValidateRetainedSceneMembership(out var sceneFailure))
            {
                RecordTransferReconciliationFailure(new InvalidOperationException(sceneFailure));
                return;
            }

            var manualRoots = HashSetPool<NetworkIdentity>.Instantiate();
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity ? identity.GetRootIdentity() : null;
                if (identity && identity.isManualSpawn && root)
                    manualRoots.Add(root);
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity ? identity.GetRootIdentity() : null;
                if (root && !manualRoots.Contains(root) && _retainedTransferRoots.Add(root))
                {
                    if (!root.id.HasValue ||
                        !_retainedTransferRootsById.TryAdd(root.id.Value, root))
                    {
                        RecordTransferReconciliationFailure(new InvalidOperationException(
                            $"Scene {_sceneId} contains a retained regular root without a unique NetworkID."));
                        break;
                    }
                }
            }

            if (_transferReconciliationFailure != null)
            {
                HashSetPool<NetworkIdentity>.Destroy(manualRoots);
                return;
            }

            var ownershipFailures = new List<Exception>();
            foreach (var manualRoot in manualRoots)
            {
                var hasOwner = false;
                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    if (!identity || !identity.OwnsHostMigrationManualRoot(
                            manualRoot, ownershipFailures))
                        continue;

                    hasOwner = true;
                    break;
                }

                if (hasOwner)
                {
                    _ownedManualTransferRoots.Add(manualRoot);
                    continue;
                }

                RecordTransferReconciliationFailure(new InvalidOperationException(
                    $"Scene {_sceneId} contains unclaimed package-managed/manual root " +
                    $"{manualRoot.id?.ToString() ?? manualRoot.name}. A scoped transfer requires " +
                    $"an exact {nameof(IHostMigrationManualHierarchyParticipant)} owner for every " +
                    "manual root; PurrNet cannot infer or implicitly replace its state."));
                break;
            }
            if (ownershipFailures.Count > 0)
            {
                RecordTransferReconciliationFailure(new AggregateException(
                    $"One or more packages could not classify manual roots in scene {_sceneId}.",
                    ownershipFailures));
            }
            HashSetPool<NetworkIdentity>.Destroy(manualRoots);

            if (_transferReconciliationFailure != null)
                return;

            if (_manager.hasReceivedHostMigrationSession)
            {
                ReceiveHostMigrationSession(_manager.hostMigrationSession,
                    _manager.isHostMigrationSessionValidated);
            }
        }

        internal bool TryArmTransferReconciliation(out string failure)
        {
            failure = null;
            if (_asServer || !_transferReconciliationRequested ||
                !_transferSessionValidated || !_transferPreambleReceived ||
                _expectedTransferSpawnManifest == null)
            {
                failure = $"Scene {_sceneId} cannot arm package reconciliation before its " +
                          "accepted exact preamble.";
                return false;
            }

            if (_transferReconciliationArmed)
                return true;

            var beginIdentities = new List<NetworkIdentity>(_spawnedIdentities);
            if (!TryCaptureRetainedSceneGraphProof(
                    out var graphProof, out failure, regularRootsOnly: true))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} could not freeze its retained graph before package Begin: {failure}");
                return false;
            }

            using (graphProof)
            {
                var beginFailures = new List<Exception>();
                for (var i = 0; i < beginIdentities.Count; i++)
                {
                    var identity = beginIdentities[i];
                    if (identity)
                    {
                        identity.TriggerBeginHostMigrationReconciliation(
                            _transferReconciliationOptions, beginFailures);
                    }
                }

                if (beginFailures.Count > 0)
                {
                    RecordTransferReconciliationFailure(new AggregateException(
                        $"One or more packages could not begin host-migration reconciliation for scene {_sceneId}.",
                        beginFailures));
                    failure = _transferReconciliationFailure.Message;
                    return false;
                }

                if (!TryValidateRetainedSceneMembership(out var membershipFailure) ||
                    !TryValidateRetainedSceneGraphProof(graphProof, out membershipFailure))
                {
                    failure = $"Scene {_sceneId} retained graph changed while package Begin hooks " +
                              $"armed exact reconciliation: {membershipFailure}";
                    AbortTransferReconciliation(failure);
                    return false;
                }
            }

            _transferReconciliationArmed = true;
            return true;
        }

        private bool TryCaptureRetainedSceneGraphProof(
            out RetainedSceneGraphProof proof, out string failure,
            bool regularRootsOnly = false)
        {
            proof = null;
            if (!TryValidateRetainedSceneMembership(out failure))
                return false;

            var captured = new RetainedSceneGraphProof();
            try
            {
                captured.regularRootsOnly = regularRootsOnly;

                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    var roleId = identity.GetNetworkID(_asServer);
                    var root = identity.GetRootIdentity();
                    if (!roleId.HasValue || !root)
                    {
                        failure = $"retained identity {identity.name} has no stable role ID or root";
                        return false;
                    }

                    if (regularRootsOnly && !_retainedTransferRoots.Contains(root))
                        continue;

                    captured.identities.Add(new RetainedIdentityGraphEntry(
                        identity, roleId.Value, identity.parent, root,
                        identity.transform.parent, identity.isManualSpawn));

                    if (!captured.rootTopologies.ContainsKey(root))
                    {
                        captured.rootTopologies.Add(root,
                            HierarchyPool.GetFullPrototype(root.transform, null, true));
                    }
                }

                proof = captured;
                captured = null;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
            finally
            {
                captured?.Dispose();
            }
        }

        private bool TryValidateRetainedSceneGraphProof(
            RetainedSceneGraphProof proof, out string failure)
        {
            if (proof == null)
            {
                failure = "the retained graph proof is missing";
                return false;
            }

            List<NetworkIdentity> currentIdentities;
            if (proof.regularRootsOnly)
            {
                if (!TryRefreshOwnedManualTransferRoots(out var manualRoots, out failure))
                    return false;

                currentIdentities = new List<NetworkIdentity>(_spawnedIdentities.Count);
                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    var root = identity ? identity.GetRootIdentity() : null;
                    if (root && manualRoots.Contains(root))
                        continue;
                    currentIdentities.Add(identity);
                }
            }
            else
            {
                currentIdentities = _spawnedIdentities;
            }

            if (proof.identities.Count != currentIdentities.Count)
            {
                failure = proof.regularRootsOnly
                    ? "the retained regular identity roster count changed"
                    : "the retained identity roster count changed";
                return false;
            }

            var currentRoots = new HashSet<NetworkIdentity>();
            for (var i = 0; i < proof.identities.Count; i++)
            {
                var expected = proof.identities[i];
                var identity = currentIdentities[i];
                var roleId = identity ? identity.GetNetworkID(_asServer) : null;
                var root = identity ? identity.GetRootIdentity() : null;
                if (!identity || !ReferenceEquals(identity, expected.identity) ||
                    !roleId.HasValue || roleId.Value != expected.roleId ||
                    !ReferenceEquals(identity.parent, expected.parent) ||
                    !ReferenceEquals(root, expected.root) ||
                    !ReferenceEquals(identity.transform.parent, expected.transformParent) ||
                    identity.isManualSpawn != expected.isManual)
                {
                    failure = $"retained identity roster entry {i} changed its identity, role, " +
                              "parent, root, or spawn classification";
                    return false;
                }

                currentRoots.Add(root);
            }

            if (currentRoots.Count != proof.rootTopologies.Count)
            {
                failure = "the retained root roster changed";
                return false;
            }

            foreach (var pair in proof.rootTopologies)
            {
                if (!pair.Key || !currentRoots.Contains(pair.Key))
                {
                    failure = "a retained root was removed or replaced";
                    return false;
                }

                using var current = HierarchyPool.GetFullPrototype(pair.Key.transform, null, true);
                if (!ArePrototypesCompatible(pair.Value, current))
                {
                    failure = $"retained root {pair.Key.name} changed its network topology";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryCollectOwnedManualTransferRoots(
            out HashSet<NetworkIdentity> manualRoots, out string failure)
        {
            manualRoots = new HashSet<NetworkIdentity>();
            failure = null;
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity ? identity.GetRootIdentity() : null;
                if (identity && identity.isManualSpawn && root)
                    manualRoots.Add(root);
            }

            var ownershipFailures = new List<Exception>();
            foreach (var manualRoot in manualRoots)
            {
                var hasOwner = false;
                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    if (!identity || !identity.OwnsHostMigrationManualRoot(
                            manualRoot, ownershipFailures))
                        continue;

                    hasOwner = true;
                    break;
                }

                if (hasOwner)
                    continue;

                failure = $"manual root {manualRoot.name} is no longer claimed by a " +
                          nameof(IHostMigrationManualHierarchyParticipant);
                return false;
            }

            if (ownershipFailures.Count > 0)
            {
                failure = new AggregateException(
                    $"One or more packages could not classify manual roots in scene {_sceneId}.",
                    ownershipFailures).Message;
                return false;
            }

            return true;
        }

        private bool TryRefreshOwnedManualTransferRoots(
            out HashSet<NetworkIdentity> manualRoots, out string failure)
        {
            if (!TryCollectOwnedManualTransferRoots(out manualRoots, out failure))
                return false;

            _ownedManualTransferRoots.Clear();
            foreach (var manualRoot in manualRoots)
                _ownedManualTransferRoots.Add(manualRoot);
            return true;
        }

        private bool TryValidateRetainedSceneMembership(out string failure)
        {
            if (!_scene.IsValid() || !_scene.isLoaded)
            {
                failure = $"Scene {_sceneId} has no valid loaded Unity scene for exact retained reconciliation.";
                return false;
            }

            if (_spawnedIdentities.Count != _spawnedIdentitiesMap.Count)
            {
                failure = $"Scene {_sceneId} retained identity list/map counts differ " +
                          $"({_spawnedIdentities.Count}/{_spawnedIdentitiesMap.Count}).";
                return false;
            }

            var registered = new HashSet<NetworkIdentity>();
            var roleIds = new HashSet<NetworkID>();
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (!identity)
                {
                    failure = $"Scene {_sceneId} retained registry contains a destroyed identity.";
                    return false;
                }

                if (identity.sceneId != _sceneId || identity.gameObject.scene != _scene)
                {
                    failure = $"Retained identity {identity.name} is registered in scene {_sceneId}, " +
                              $"but physically belongs to Unity scene '{identity.gameObject.scene.name}'. " +
                              "Exact reconciliation does not support cross-scene or DontDestroyOnLoad drift.";
                    return false;
                }

                if (!ReferenceEquals(identity.networkManager, _manager) ||
                    !identity.IsSpawned(_asServer) || !registered.Add(identity))
                {
                    failure = $"Retained identity {identity.name} in scene {_sceneId} has a dead, " +
                              "duplicate, or foreign hierarchy role.";
                    return false;
                }

                var roleId = identity.GetNetworkID(_asServer);
                if (!roleId.HasValue || !roleIds.Add(roleId.Value) ||
                    !_spawnedIdentitiesMap.TryGetValue(roleId.Value, out var mapped) ||
                    !ReferenceEquals(mapped, identity))
                {
                    failure = $"Retained identity {identity.name} in scene {_sceneId} has no unique " +
                              "bijective role NetworkID.";
                    return false;
                }
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity.gameObject.GetComponent<NetworkIdentity>();
                if (!root || !registered.Contains(root))
                {
                    failure = $"Retained identity {identity.name} in scene {_sceneId} has no registered root.";
                    return false;
                }

                var ancestry = new HashSet<NetworkIdentity> { identity };
                var current = identity.parent;
                while (!ReferenceEquals(current, null))
                {
                    if (!current || !registered.Contains(current))
                    {
                        failure = $"Retained identity {identity.name} in scene {_sceneId} has a dead " +
                                  "or unregistered parent.";
                        return false;
                    }

                    if (!ancestry.Add(current))
                    {
                        failure = $"Retained identity {identity.name} in scene {_sceneId} has a cyclic root chain.";
                        return false;
                    }

                    root = current;
                    current = current.parent;
                }

                var rootId = root.GetNetworkID(_asServer);
                if (!rootId.HasValue ||
                    !_spawnedIdentitiesMap.TryGetValue(rootId.Value, out var mappedRoot) ||
                    !ReferenceEquals(mappedRoot, root))
                {
                    failure = $"Retained identity {identity.name} in scene {_sceneId} resolves to an unstable root.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        private bool TryValidateStaleRetirementPhysicalRoster(out string failure)
        {
            var roots = _scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var identities = roots[i].GetComponentsInChildren<NetworkIdentity>(true);
                for (var j = 0; j < identities.Length; j++)
                {
                    var identity = identities[j];
                    if (!identity || !ReferenceEquals(identity.networkManager, _manager) ||
                        !identity.IsSpawned(_asServer))
                        continue;

                    var roleId = identity.GetNetworkID(_asServer);
                    if (identity.sceneId != _sceneId || !roleId.HasValue ||
                        !_spawnedIdentitiesMap.TryGetValue(roleId.Value, out var registered) ||
                        !ReferenceEquals(registered, identity))
                    {
                        failure = $"loaded scene '{_scene.name}' contains a live " +
                                  $"{(_asServer ? "server" : "client")} identity " +
                                  $"'{identity.name}' outside SceneID {_sceneId}'s hierarchy registry";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        internal void ReceiveHostMigrationSession(HostMigrationTransitionOptions session, bool matched)
        {
            if (!_transferReconciliationRequested)
                return;

            _transferSessionValidated = matched && session.canReconcile &&
                                        session == _transferReconciliationOptions;
            if (!_transferSessionValidated)
            {
                AbortTransferReconciliation(
                    $"The connected server advertised host-migration session {session}, expected " +
                    $"{_transferReconciliationOptions}.");
            }
        }

        private void AbortTransferReconciliation(string reason)
        {
            if (!_transferReconciliationRequested)
                return;

            if (_transferReconciliationFailure == null)
                PurrLogger.LogError(reason);
            RecordTransferReconciliationFailure(new InvalidOperationException(reason));
        }

        internal void AbortExactTransferFromFactory(string reason)
        {
            AbortTransferReconciliation(reason);
        }

        private bool TryAuthorizeTransactionWideExactSnapshot(out string failure)
        {
            failure = null;
            if (_asServer || !_transferReconciliationRequested)
                return true;

            if (!_manager.TryGetModule<HierarchyFactory>(false, out var factory))
            {
                failure = "the client hierarchy coordinator is unavailable";
                return false;
            }

            return factory.TryAuthorizeExactTransferSnapshot(
                this, _transferReconciliationOptions, out failure);
        }

        private void RecordTransferReconciliationFailure(Exception failure)
        {
            if (failure == null)
                return;

            ClearExpectedTransferSpawnManifest();
            _transferReconciliationFailure = _transferReconciliationFailure == null
                ? failure
                : new AggregateException(_transferReconciliationFailure, failure);
        }

        private void ClearExpectedTransferSpawnManifest()
        {
            _expectedTransferSpawnManifest?.Dispose();
            _expectedTransferSpawnManifest = null;
        }

        private void ClearTransferReconciliationState()
        {
            ClearExpectedTransferSpawnManifest();
            foreach (var pending in _pendingReconciledSpawns.Values)
            {
                if (!pending.isDisposed)
                    pending.Dispose();
            }

            _pendingReconciledSpawns.Clear();
            _pendingReconciliationReadiness.Clear();
            _reconciliationNotifiedIdentities.Clear();
            _transferReconciliationFailure = null;
            _retainedTransferRoots.Clear();
            _ownedManualTransferRoots.Clear();
            _retainedTransferRootsById.Clear();
            _confirmedTransferRoots.Clear();
            _transferReconciliationOptions = default;
            _transferReconciliationRequested = false;
            _transferSessionValidated = false;
            _transferPreambleReceived = false;
            _transferReconciliationArmed = false;
            _transferEndReceived = false;
        }

        private void ClearPendingReceivedSpawnTransactions()
        {
            foreach (var pending in _pendingSpawns.Values)
            {
                if (!pending.isDisposed)
                    pending.Dispose();
            }

            _pendingSpawns.Clear();
            _pendingFinishSpawns.Clear();
            _pendingDespawns.Clear();
        }

        private void DestroyAllSpawnedRoots()
        {
            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity ? identity.GetRootIdentity() : null;
                if (root)
                    roots.Add(root);
            }

            foreach (var root in roots)
            {
                if (root)
                    Despawn(root.gameObject, true, true);
            }

            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        private void DestroyRetainedTransferRoots(bool includeConfirmed)
        {
            var roots = ListPool<NetworkIdentity>.Instantiate();
            foreach (var root in _retainedTransferRoots)
            {
                if (root && (includeConfirmed || !_confirmedTransferRoots.Contains(root)))
                    roots.Add(root);
            }

            for (var i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root)
                    Despawn(root.gameObject, true, true);
            }

            ListPool<NetworkIdentity>.Destroy(roots);
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
            _pendingLocalDespawnEchoes.Dispose();

            var rules = _manager.networkRules;
            if (rules && !rules.ShouldCleanupSpawnedObjectsOnDisconnect())
                return true;

            if (_isDisposed)
                return true;

            _isDisposed = true;
            ClearAsyncSpawnState();

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

            if (_manager.expectedHostMigrationSession.canReconcile)
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

        void ApplyParentChange(NetworkIdentity identity, NetworkIdentity parent, int[] path, bool refreshVisibility, bool worldPositionStays = true, bool applyToTransform = true)
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

            if (applyToTransform)
            {
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
            }

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
        private readonly HashSet<SpawnID> _asyncPendingSpawns = new();
        private ulong _clientSpawnGeneration;
        private int _deferredPrefabSpawnCount;
        private readonly List<(SpawnID packetIdx, PlayerID player, bool asServer)> _pendingFinishSpawns = new();
        private readonly List<(PlayerID player, DespawnPacket packet, bool asServer)> _pendingDespawns = new();
        private DisposableList<NetworkID> _pendingLocalDespawnEchoes;
        private readonly HashSet<SpawnID> _cancelledPendingSpawns = new();
        private readonly Dictionary<SpawnID, PendingAsyncObserverSpawn> _pendingAsyncObservers = new();
        private readonly Dictionary<SpawnID, PendingAsyncObserverSpawn> _readyAsyncObservers = new();
        private readonly HashSet<(PlayerID player, NetworkID root)> _failedAsyncObserverRoots = new();
        private readonly HashSet<SpawnID> _relayAsyncSpawns = new();
        private readonly HashSet<NetworkID> _failedAsyncSpawnRoots = new();
        private int _asyncVisibilityDepth;
        private int _asyncObserverPromotionDepth;
        // One-way: later catch-up packets must retain bypass checks after async/unpooled provenance appears.
        private bool _hasConfiguredPoolBypass;

        private bool HasActiveAsyncObserverState =>
            _asyncObserverPromotionDepth > 0 ||
            _pendingAsyncObservers.Count > 0 ||
            _readyAsyncObservers.Count > 0 ||
            _failedAsyncObserverRoots.Count > 0;

        private sealed class PendingAsyncObserverSpawn
        {
            public readonly PlayerID player;
            public readonly List<NetworkIdentity> identities;
            public readonly float createdAt;
            public bool sent;

            public PendingAsyncObserverSpawn(PlayerID player, List<NetworkIdentity> identities)
            {
                this.player = player;
                this.identities = identities;
                createdAt = Time.realtimeSinceStartup;
            }
        }

        private const float AsyncSpawnReadyTimeoutSeconds = 60f;

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        private sealed class PendingAsyncInstantiation
        {
            public SpawnPacket packet;
            public AsyncInstantiateOperation<GameObject> operation;
            public GameObject result;
            public bool flushData;
            public bool cancelled;
            public bool packetDisposed;

            public void DisposePacket()
            {
                if (packetDisposed)
                    return;
                packetDisposed = true;
                packet.Dispose();
            }
        }

        private readonly Dictionary<NetworkID, PendingAsyncInstantiation> _pendingAsyncInstantiations = new();
        private readonly HashSet<NetworkID> _reservedAsyncNetworkIds = new();
#endif

        private void ClearAsyncSpawnState()
        {
            foreach (var pending in _pendingAsyncObservers.Values)
            {
                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (identity)
                        identity.TryRemovePendingObserver(pending.player);
                }
            }
            _pendingAsyncObservers.Clear();

            foreach (var pair in _readyAsyncObservers)
            {
                var ready = pair.Value;
                _toCompleteNextFrame.Remove(pair.Key);
                for (var i = 0; i < ready.identities.Count; i++)
                {
                    var identity = ready.identities[i];
                    if (identity)
                        identity.TryRemoveObserver(ready.player);
                }
            }
            _readyAsyncObservers.Clear();

            foreach (var failed in _failedAsyncObserverRoots)
            {
                if (!TryGetIdentity(failed.root, out var root) || !root)
                    continue;

                var identities = ListPool<NetworkIdentity>.Instantiate();
                GetComponentsInChildren(root.gameObject, identities);
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    if (identity)
                        identity.TryRemovePendingObserver(failed.player);
                }
                ListPool<NetworkIdentity>.Destroy(identities);
            }
            _failedAsyncObserverRoots.Clear();
            _relayAsyncSpawns.Clear();
            _failedAsyncSpawnRoots.Clear();
            _cancelledPendingSpawns.Clear();
            _asyncPendingSpawns.Clear();
            _asyncVisibilityDepth = 0;

#if PURRNET_UNITY_INSTANTIATE_ASYNC
            if (_pendingAsyncInstantiations.Count > 0)
            {
                var states = new List<PendingAsyncInstantiation>(_pendingAsyncInstantiations.Values);
                _pendingAsyncInstantiations.Clear();
                _reservedAsyncNetworkIds.Clear();

                for (var i = 0; i < states.Count; i++)
                {
                    var state = states[i];
                    state.cancelled = true;
                    try { state.operation?.Cancel(); }
                    catch { }
                    if (state.result)
                        UnityProxy.DestroyDirectly(state.result);
                    state.result = null;
                    state.DisposePacket();
                }
            }
#endif
        }

        private void OnFinishSpawnPacket(PlayerID player, FinishSpawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            if (_pendingReconciledSpawns.Remove(data.packetIdx, out var reconciled))
            {
                CompleteReconciledSpawn(reconciled);
                TryFinalizeTransferReconciliation();
                return;
            }

            if (_cancelledPendingSpawns.Count > 0 && _cancelledPendingSpawns.Remove(data.packetIdx))
                return;

            if (_pendingSpawns.Remove(data.packetIdx, out var list))
            {
                if (_asyncPendingSpawns.Count > 0)
                    _asyncPendingSpawns.Remove(data.packetIdx);
                using (list)
                {
                    int count = list.Count;

                    switch (count)
                    {
                        case > 0 when !list[0] || !list[0].isSpawned:
                            if (_relayAsyncSpawns.Count > 0)
                                _relayAsyncSpawns.Remove(data.packetIdx);
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
                                _triggerLateObserverAdded.Add(
                                    CreateLateObserverEntry(spawner, nid, true));
                            }

                            var lastNid = list[count - 1];
                            if (lastNid && lastNid.id.HasValue)
                                _playersManager.RegisterClientLastId(spawner, lastNid.id.Value);

                            bool relayAsync = _relayAsyncSpawns.Count > 0 &&
                                              _relayAsyncSpawns.Remove(data.packetIdx);
                            RefreshVisibilityAfterRemoteSpawn(list[0], relayAsync);

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
                TryFinalizeTransferReconciliation();
            }
            else
            {
                _pendingFinishSpawns.Add((data.packetIdx, player, asServer));
            }
        }

        private void CompleteReconciledSpawn(DisposableList<NetworkIdentity> identities)
        {
            using (identities)
            {
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    if (identity && identity.isSpawned)
                    {
                        BeginIdentityReconciliationReadiness(identity);
                    }
                }
            }
        }

        private void BeginManualHierarchyParticipantReadiness(
            HashSet<NetworkIdentity> manualRoots)
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var root = identity ? identity.GetRootIdentity() : null;
                if (!identity || !root ||
                    (!manualRoots.Contains(root) && !_confirmedTransferRoots.Contains(root)) ||
                    !identity.HasHostMigrationManualHierarchyParticipant())
                    continue;

                BeginIdentityReconciliationReadiness(identity);
            }
        }

        private void BeginIdentityReconciliationReadiness(NetworkIdentity identity)
        {
            if (!identity || !_reconciliationNotifiedIdentities.Add(identity))
                return;

            var readiness = identity.TriggerOnHostMigrationRebound(
                _transferReconciliationOptions);
            if (!readiness.IsCompleted)
                _pendingReconciliationReadiness.Add(readiness);
            else
                RecordReconciliationReadinessResult(readiness);
        }

        private void PollReconciliationReadiness()
        {
            for (var i = _pendingReconciliationReadiness.Count - 1; i >= 0; i--)
            {
                var task = _pendingReconciliationReadiness[i];
                if (!task.IsCompleted)
                    continue;

                _pendingReconciliationReadiness.RemoveAt(i);
                RecordReconciliationReadinessResult(task);
            }
        }

        private void RecordReconciliationReadinessResult(Task task)
        {
            if (_transferReconciliationFailure != null || task == null)
                return;

            if (task.IsCanceled)
            {
                _transferReconciliationFailure = new TaskCanceledException(
                    $"A package cancelled host-migration reconciliation for scene {_sceneId}.");
            }
            else if (task.IsFaulted)
            {
                _transferReconciliationFailure = task.Exception?.GetBaseException() ??
                                                 new InvalidOperationException(
                                                     $"A package failed host-migration reconciliation for scene {_sceneId}.");
            }
        }

        private void ProcessBufferedFinishSpawnForReconciled(SpawnID packetIdx)
        {
            for (var i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                if (!_pendingFinishSpawns[i].packetIdx.Equals(packetIdx))
                    continue;

                _pendingFinishSpawns.RemoveAt(i);
                if (_pendingReconciledSpawns.Remove(packetIdx, out var reconciled))
                    CompleteReconciledSpawn(reconciled);
                TryFinalizeTransferReconciliation();
                return;
            }
        }

        private void TryFinalizeTransferReconciliation()
        {
            if (!_transferReconciliationRequested || _transferReconciliationComplete ||
                !_transferEndReceived || _pendingReconciledSpawns.Count > 0 ||
                _pendingSpawns.Count > 0 || _pendingFinishSpawns.Count > 0 ||
                _deferredPrefabSpawnCount > 0 || _pendingReconciliationReadiness.Count > 0 ||
                _transferReconciliationFailure != null)
                return;

#if PURRNET_UNITY_INSTANTIATE_ASYNC
            if (_pendingAsyncInstantiations.Count > 0)
                return;
#endif

            while (true)
            {
                if (!TryRefreshOwnedManualTransferRoots(
                        out var manualRoots, out var ownershipFailure))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} has invalid manual-root ownership before " +
                        $"reconciliation readiness: {ownershipFailure}");
                    return;
                }

                var notifiedBefore = _reconciliationNotifiedIdentities.Count;
                BeginManualHierarchyParticipantReadiness(manualRoots);
                if (_pendingReconciliationReadiness.Count > 0 ||
                    _transferReconciliationFailure != null)
                    return;

                if (!TryRefreshOwnedManualTransferRoots(
                        out _, out ownershipFailure))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} has invalid manual-root ownership after " +
                        $"reconciliation readiness: {ownershipFailure}");
                    return;
                }

                if (_reconciliationNotifiedIdentities.Count == notifiedBefore)
                    break;
            }

            if (!TryValidateRetainedSceneMembership(out var membershipFailure))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} retained graph changed before reconciliation completion: " +
                    membershipFailure);
                return;
            }

            DestroyRetainedTransferRoots(includeConfirmed: false);
            _scenePool.ReconcileActiveScenePieces();
            if (!TryValidateRetainedSceneMembership(out membershipFailure))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} retained graph changed at reconciliation commit: " +
                    membershipFailure);
                return;
            }

            if (!TryRefreshOwnedManualTransferRoots(
                    out _, out var commitOwnershipFailure))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} has invalid manual-root ownership after stale regular-root " +
                    $"retirement: {commitOwnershipFailure}");
                return;
            }

            ClearTransferReconciliationState();
            _transferReconciliationComplete = true;
        }

        private void DrainObserverEventsFor(DisposableList<NetworkIdentity> list)
        {
            for (int i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                var entry = _triggerLateObserverAdded[i];
                if (!ListContainsNid(list, entry.nid)) continue;
                if (!entry.nid || !entry.nid.isSpawned) continue;
                entry.nid.TriggerOnObserverAdded(entry.player, entry.isSpawner);
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

        private void RefreshVisibilityAfterRemoteSpawn(NetworkIdentity root, bool relayAsync)
        {
            if (!root)
                return;

            if (relayAsync)
                ++_asyncVisibilityDepth;

            try
            {
                if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
                {
                    for (var i = 0; i < players.Count; i++)
                        _visibility.RefreshVisibilityForGameObject(players[i], root.transform);
                }
            }
            finally
            {
                if (relayAsync)
                    --_asyncVisibilityDepth;
            }

            FlushSpawnPackets();
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

                if (_asyncPendingSpawns.Count > 0)
                    _asyncPendingSpawns.Remove(packetIdx);

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
                            _triggerLateObserverAdded.Add(
                                CreateLateObserverEntry(spawner, nid, true));
                        }

                        var lastNid = list[count - 1];
                        if (lastNid && lastNid.id.HasValue)
                            _playersManager.RegisterClientLastId(spawner, lastNid.id.Value);

                        bool relayAsync = _relayAsyncSpawns.Count > 0 &&
                                          _relayAsyncSpawns.Remove(packetIdx);
                        RefreshVisibilityAfterRemoteSpawn(list[0], relayAsync);

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

        private void ProcessBufferedDespawnsFor(GameObjectPrototype prototype)
        {
            for (int i = _pendingDespawns.Count - 1; i >= 0; i--)
            {
                var (_, packet, _) = _pendingDespawns[i];

                for (int j = 0; j < prototype.framework.Count; j++)
                {
                    var piece = prototype.framework[j];
                    if (piece.id != packet.parentId || !TryGetIdentity(piece.id, out var nid) || !nid)
                        continue;

                    _pendingDespawns.RemoveAt(i);
                    try
                    {
                        CancelPendingAsyncSpawnRoot(nid);
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

        private bool RemoveBufferedDespawnsFor(NetworkID rootId)
        {
            bool removed = false;
            for (var i = _pendingDespawns.Count - 1; i >= 0; i--)
            {
                if (_pendingDespawns[i].packet.parentId != rootId)
                    continue;

                _pendingDespawns.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (!asserver)
                return;

            if (scene != _sceneId)
                return;

            for (var i = _sceneReconcileEndsNextFrame.Count - 1; i >= 0; i--)
            {
                if (_sceneReconcileEndsNextFrame[i].player == player)
                    _sceneReconcileEndsNextFrame.RemoveAt(i);
            }

            if (_exactBarrierBypassFinishes.Count != 0)
            {
                var staleFinishes = ListPool<SpawnID>.Instantiate();
                foreach (var pair in _exactBarrierBypassFinishes)
                {
                    if (pair.Key.target == player)
                        staleFinishes.Add(pair.Key);
                }
                for (var i = 0; i < staleFinishes.Count; i++)
                    _exactBarrierBypassFinishes.Remove(staleFinishes[i]);
                ListPool<SpawnID>.Destroy(staleFinishes);
            }

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];

                if (!id) continue;

                var root = id.GetRootIdentity();

                if (!root || root.isManualSpawn || !roots.Add(root))
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
            NetworkID[] replacementRootIds = null;
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

            if (!_asServer && _transferReconciliationRequested)
            {
                SceneSpawnReconcileClassification classification = default;
                string manifestFailure = null;
                if (!TryAuthorizeTransactionWideExactSnapshot(out var transactionFailure))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} received spawn {data.packetIdx} before the complete " +
                        $"scene topology set was proven: {transactionFailure}");
                    return;
                }
                else if (!_transferSessionValidated || !_transferPreambleReceived)
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} received a spawn before its host-migration manifest preamble.");
                    return;
                }
                else if (_expectedTransferSpawnManifest == null ||
                         !_expectedTransferSpawnManifest.TryConsume(_sceneId, data,
                             out classification, out manifestFailure))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} rejected spawn {data.packetIdx}: " +
                        (manifestFailure ?? "no accepted topology preflight exists"));
                    return;
                }
                else if (classification.isRetained)
                {
                    var retainedResult = TryReconcileRetainedSpawn(data, classification.retainedRootId);
                    if (retainedResult == RetainedSpawnResult.Reconciled)
                    {
                        if (flushData)
                            FlushSpawnPackets();
                        return;
                    }

                    if (_transferReconciliationFailure != null)
                        return;
                }
                else if (classification.replacementRootIds is { Length: > 0 })
                {
                    replacementRootIds = classification.replacementRootIds;
                }
            }

            if (replacementRootIds != null && data.prototype.framework.Count > 0)
            {
                var rootPrefabId = (int)data.prototype.framework[0].pid.prefabId;
                PrefabData prefabData = default;
                if (rootPrefabId >= 0 &&
                    (_manager.prefabProvider == null ||
                     !_manager.prefabProvider.TryGetPrefabData(rootPrefabId, out prefabData)))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} cannot replace incompatible roots for spawn " +
                        $"{data.packetIdx}: prefab {rootPrefabId} is unavailable.");
                    return;
                }

                if (rootPrefabId >= 0 && !prefabData.prefab)
                {
                    if (_manager.prefabProvider is IAsyncPrefabProvider deferredAsyncProvider &&
                        !data.isAsync)
                    {
                        _deferredPrefabSpawnCount++;
                        ProcessSpawnWhenLoadedAsync(data, flushData, deferredAsyncProvider, rootPrefabId,
                            _clientSpawnGeneration, replacementRootIds);
                    }
                    else
                    {
                        AbortTransferReconciliation(
                            $"Scene {_sceneId} cannot replace incompatible roots for spawn " +
                            $"{data.packetIdx}: prefab {rootPrefabId} is not loaded.");
                    }
                    return;
                }

                if (!TryRetireReplacedTransferRoots(data.packetIdx, replacementRootIds,
                        out var replacementFailure))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} could not replace the incompatible local roots for " +
                        $"spawn {data.packetIdx}: {replacementFailure}");
                    return;
                }
            }

            ReplacePartialLocalHierarchy(data.prototype);

            if (data.prototype.framework.Count > 0)
            {
                for (var i = 0; i < data.prototype.framework.Count; i++)
                {
                    var piece = data.prototype.framework[i];
                    if (TryGetIdentity(piece.id, out var existing))
                    {
                        if (!_asServer && _transferReconciliationRequested)
                        {
                            AbortTransferReconciliation(
                                $"Scene {_sceneId} received declared fresh spawn {data.packetIdx}, " +
                                $"but NetworkID {piece.id} appeared after its topology preflight.");
                            return;
                        }

                        PurrLogger.LogError(
                            $"Spawn failed for player `{player}`. Identity with id `{piece.id}` already exists: `{existing.gameObject.name}`",
                            existing);
                        RejectAsyncSpawn(data);
                        return;
                    }
                }
            }

            if (_asServer && _clientSpawnValidators.Count > 0)
            {
                for (var i = 0; i < _clientSpawnValidators.Count; i++)
                {
                    var validator = _clientSpawnValidators[i];
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
                    if (data.isAsync)
                    {
                        PurrLogger.LogError(
                            $"InstantiateAsync spawn {data.packetIdx} was rejected because prefab {rootPrefabId} is not loaded on this peer. Preload it before instantiating so reliable ordered traffic can be applied immediately.");
                        RejectAsyncSpawn(data);
                        return;
                    }

                    _deferredPrefabSpawnCount++;
                    ProcessSpawnWhenLoadedAsync(data, flushData, asyncProvider, rootPrefabId,
                        _clientSpawnGeneration);
                    return;
                }
            }

            CompleteReceivedSpawn(data, flushData);
        }

        private bool TryRetireReplacedTransferRoots(SpawnID spawnId,
            IReadOnlyList<NetworkID> replacementRootIds, out string failure)
        {
            failure = null;
            var roots = ListPool<NetworkIdentity>.Instantiate();
            try
            {
                for (var i = 0; i < replacementRootIds.Count; i++)
                {
                    var rootId = replacementRootIds[i];
                    if (!_retainedTransferRootsById.TryGetValue(rootId, out var root))
                    {
                        continue;
                    }

                    if (!root || !_retainedTransferRoots.Contains(root))
                    {
                        failure = $"retained root {rootId} disappeared after topology preflight";
                        return false;
                    }

                    if (!roots.Contains(root))
                        roots.Add(root);
                }

                for (var i = 0; i < replacementRootIds.Count; i++)
                    _retainedTransferRootsById.Remove(replacementRootIds[i]);
                for (var i = 0; i < roots.Count; i++)
                {
                    _retainedTransferRoots.Remove(roots[i]);
                    _confirmedTransferRoots.Remove(roots[i]);
                }

                for (var i = 0; i < roots.Count; i++)
                {
                    var root = roots[i];
                    if (root)
                        Despawn(root.gameObject, true, true);
                }

                if (!TryValidateRetainedSceneMembership(out failure))
                    return false;

                if (roots.Count > 0)
                {
                    PurrLogger.LogWarning(
                        $"Scene {_sceneId} retained the Unity scene while replacing {roots.Count} " +
                        $"incompatible network root(s) for authoritative spawn {spawnId}.");
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
            finally
            {
                ListPool<NetworkIdentity>.Destroy(roots);
            }
        }

        private enum RetainedSpawnResult
        {
            NotRetained,
            Reconciled,
            Failed
        }

        private RetainedSpawnResult TryReconcileRetainedSpawn(SpawnPacket data,
            NetworkID retainedRootId)
        {
            if (data.prototype.framework.Count == 0)
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} declared retained spawn {data.packetIdx} with an empty topology.");
                return RetainedSpawnResult.Failed;
            }

            if (!_retainedTransferRootsById.TryGetValue(retainedRootId, out var retainedRoot) ||
                !retainedRoot || !_retainedTransferRoots.Contains(retainedRoot))
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} lost retained root {retainedRootId} before spawn {data.packetIdx} could apply.");
                return RetainedSpawnResult.Failed;
            }

            var identities = DisposableList<NetworkIdentity>.Create(data.prototype.framework.Count);
            bool compatible;
            try
            {
                compatible = retainedRoot &&
                             IsRetainedPrototypeCompatible(retainedRoot, data.prototype, identities.list);
            }
            catch (Exception exception)
            {
                identities.Dispose();
                AbortTransferReconciliation(
                    $"Scene {_sceneId} could not inspect retained spawn {data.packetIdx}: " +
                    exception.Message);
                return RetainedSpawnResult.Failed;
            }

            if (!compatible)
            {
                identities.Dispose();
                AbortTransferReconciliation(
                    $"Scene {_sceneId} could not exactly reconcile retained spawn {data.packetIdx}: " +
                    $"retained root {retainedRootId} changed after the topology preflight.");
                return RetainedSpawnResult.Failed;
            }

            if (_pendingReconciledSpawns.ContainsKey(data.packetIdx))
            {
                identities.Dispose();
                AbortTransferReconciliation(
                    $"Scene {_sceneId} received duplicate retained spawn transaction {data.packetIdx}.");
                return RetainedSpawnResult.Failed;
            }

            if (!TryValidateRetainedSceneMembership(out var membershipFailure))
            {
                identities.Dispose();
                AbortTransferReconciliation(
                    $"Scene {_sceneId} retained graph changed before spawn {data.packetIdx} " +
                    $"could apply custom state: {membershipFailure}");
                return RetainedSpawnResult.Failed;
            }

            try
            {
                if (data.customData.bitLength > 0)
                {
                    using var scope = data.customData.AutoScope();
                    for (var i = 0; i < identities.Count; i++)
                    {
                        var identity = identities[i];
                        if (identity)
                            identity.TriggerOnDeserialize(data.customData.packer);
                    }
                }

                if (!TryValidateRetainedSceneMembership(out membershipFailure))
                {
                    identities.Dispose();
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} retained graph changed while spawn {data.packetIdx} " +
                        $"applied custom state: {membershipFailure}");
                    return RetainedSpawnResult.Failed;
                }

                _pendingReconciledSpawns.Add(data.packetIdx, identities);
                _confirmedTransferRoots.Add(retainedRoot);

                if (data.isAsync)
                    SendAsyncSpawnReady(data.packetIdx, true);

                ProcessBufferedFinishSpawnForReconciled(data.packetIdx);
                ProcessBufferedDespawnsFor(data.prototype);
                return RetainedSpawnResult.Reconciled;
            }
            catch (Exception e)
            {
                if (_pendingReconciledSpawns.Remove(data.packetIdx, out var pending) &&
                    !pending.isDisposed)
                    pending.Dispose();
                else if (!identities.isDisposed)
                    identities.Dispose();

                PurrLogger.LogError(
                    $"Failed to reconcile retained spawn {data.packetIdx}: {e.Message}\n{e.StackTrace}");
                AbortTransferReconciliation(
                    $"Scene {_sceneId} could not apply authoritative state to retained spawn " +
                    $"{data.packetIdx}: {e.Message}");
                return RetainedSpawnResult.Failed;
            }
        }

        internal static bool ArePrototypesCompatible(GameObjectPrototype retained,
            GameObjectPrototype authoritative)
        {
            if (retained.framework.Count != authoritative.framework.Count ||
                retained.parentID != authoritative.parentID ||
                retained.defaultParentSiblingIndex != authoritative.defaultParentSiblingIndex ||
                !ArePathsEqual(retained.path, authoritative.path))
                return false;

            for (var i = 0; i < retained.framework.Count; i++)
            {
                var retainedPiece = retained.framework[i];
                var authoritativePiece = authoritative.framework[i];
                if (retainedPiece.id != authoritativePiece.id ||
                    !retainedPiece.AreEqual(authoritativePiece))
                    return false;
            }

            return true;
        }

        private static bool ArePathsEqual(int[] left, int[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static bool IsRetainedPrototypeCompatible(NetworkIdentity retainedRoot,
            GameObjectPrototype authoritative, List<NetworkIdentity> identities)
        {
            using var retained = HierarchyPool.GetFullPrototype(retainedRoot.transform, identities);
            return ArePrototypesCompatible(retained, authoritative);
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
            IAsyncPrefabProvider asyncProvider, int rootPrefabId, ulong spawnGeneration,
            NetworkID[] replacementRootIds = null)
        {
            try
            {
                var prototypeCopy = data.prototype.Clone();
                var customDataCopy = data.customData.Duplicate();
                var packetIdx = data.packetIdx;
                var sceneId = data.sceneId;
                var bypassPool = data.bypassPool;
                var isAsync = data.isAsync;

                try
                {
                    var loaded = await asyncProvider.LoadPrefabAsync(rootPrefabId);

                    if (_isDisposed || !_enabled || spawnGeneration != _clientSpawnGeneration)
                    {
                        prototypeCopy.Dispose();
                        customDataCopy.Dispose();
                        return;
                    }

                    if (loaded.prefab == null)
                    {
                        PurrLogger.LogError($"ProcessSpawnWhenLoadedAsync: failed to load prefab {rootPrefabId}.");
                        if (!_asServer && _transferReconciliationRequested)
                        {
                            AbortTransferReconciliation(
                                $"Scene {_sceneId} could not load prefab {rootPrefabId} for " +
                                $"declared spawn {packetIdx}.");
                        }
                        else
                        {
                            RejectDeferredAsyncSpawn(packetIdx, sceneId, isAsync, prototypeCopy);
                        }
                        prototypeCopy.Dispose();
                        customDataCopy.Dispose();
                        return;
                    }

                    var spawnData = new SpawnPacket
                    {
                        sceneId = sceneId,
                        packetIdx = packetIdx,
                        bypassPool = bypassPool,
                        isAsync = isAsync,
                        prototype = prototypeCopy,
                        customData = customDataCopy
                    };

                    if (replacementRootIds is { Length: > 0 } &&
                        !TryRetireReplacedTransferRoots(packetIdx, replacementRootIds,
                            out var replacementFailure))
                    {
                        AbortTransferReconciliation(
                            $"Scene {_sceneId} loaded prefab {rootPrefabId}, but could not replace " +
                            $"the incompatible local roots for spawn {packetIdx}: " +
                            replacementFailure);
                        spawnData.Dispose();
                        return;
                    }

                    CompleteReceivedSpawn(spawnData, flushData);
                    spawnData.Dispose();
                }
                catch (Exception e)
                {
                    PurrLogger.LogError($"ProcessSpawnWhenLoadedAsync: exception for prefab {rootPrefabId}: {e.Message}\n{e.StackTrace}");
                    if (!_isDisposed && _enabled && spawnGeneration == _clientSpawnGeneration)
                    {
                        if (!_asServer && _transferReconciliationRequested)
                        {
                            AbortTransferReconciliation(
                                $"Scene {_sceneId} could not load prefab {rootPrefabId} for " +
                                $"declared spawn {packetIdx}: {e.Message}");
                        }
                        else
                        {
                            RejectDeferredAsyncSpawn(packetIdx, sceneId, isAsync, prototypeCopy);
                        }
                    }
                    try { prototypeCopy.Dispose(); } catch { }
                    try { customDataCopy.Dispose(); } catch { }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                if (spawnGeneration == _clientSpawnGeneration && _deferredPrefabSpawnCount > 0)
                    _deferredPrefabSpawnCount--;
                TryFinalizeTransferReconciliation();
            }
        }

        private void RejectDeferredAsyncSpawn(SpawnID packetIdx, SceneID sceneId, bool isAsync,
            GameObjectPrototype prototype)
        {
            if (!isAsync || _isDisposed)
                return;

            var packet = new SpawnPacket
            {
                packetIdx = packetIdx,
                sceneId = sceneId,
                isAsync = true,
                prototype = prototype
            };
            RejectAsyncSpawn(packet);
        }

        private void RejectAsyncSpawn(SpawnPacket packet)
        {
            if (!packet.isAsync)
                return;

            if (_asServer)
                RollbackSpawnOnClient(packet.packetIdx.scope, packet);
            else
                SendAsyncSpawnFailure(packet);
        }

        private void CompleteReceivedSpawn(SpawnPacket data, bool flushData)
        {
            if (!data.isAsync)
            {
                if (!CompleteSpawn(data, flushData, data.bypassPool) &&
                    !_asServer && _transferReconciliationRequested)
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} could not materialize declared spawn {data.packetIdx}.");
                }
                return;
            }

            if (_asServer)
            {
                // Client-authoritative async spawns are integrated synchronously on the server so
                // reliable packets sent immediately after the source operation cannot overtake the
                // server identity. They still bypass pooling and relay asynchronously to observers.
                _relayAsyncSpawns.Add(data.packetIdx);
                if (!CompleteSpawn(data, flushData, true))
                {
                    _relayAsyncSpawns.Remove(data.packetIdx);
                    RollbackSpawnOnClient(data.packetIdx.scope, data);
                }
                return;
            }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
            BeginAsyncRemoteSpawn(data, flushData);
#else
            PurrLogger.LogError("Received an asynchronous spawn on a Unity version that does not support Object.InstantiateAsync.");
            SendAsyncSpawnFailure(data);
#endif
        }

        private bool CompleteSpawn(SpawnPacket data, bool flushData, bool forceUnpooled = false)
        {
            if (forceUnpooled)
                _hasConfiguredPoolBypass = true;

            var createdNids = DisposableList<NetworkIdentity>.Create(16);
            var go = forceUnpooled
                ? CreateUnpooledPrototype(data.prototype, createdNids.list)
                : CreatePrototype(data.prototype, createdNids.list);

            if (!go || createdNids.Count == 0)
            {
                PurrLogger.LogError($"CompleteSpawn: CreatePrototype failed for packet {data.packetIdx}.");
                createdNids.Dispose();
                if (go)
                    UnityProxy.DestroyDirectly(go);
                return false;
            }

            return CompleteSpawnWithInstance(data, flushData, go, createdNids);
        }

        private bool CompleteSpawnWithInstance(SpawnPacket data, bool flushData, GameObject go,
            DisposableList<NetworkIdentity> createdNids)
        {
            bool hasCustomData = data.customData.bitLength > 0;
            bool ownsPendingEntry = false;

            try
            {
                onPreSpawn?.Invoke(go, false);
                using var scope = data.customData.AutoScope();

                bool isHost = _asServer && IsServerHost();
                var spawner = data.packetIdx.scope;

                for (var i = 0; i < createdNids.Count; i++)
                {
                    var nid = createdNids[i];
                    if (_failedAsyncSpawnRoots.Count > 0 && nid.id.HasValue)
                        _failedAsyncSpawnRoots.Remove(nid.id.Value);
                    nid.SetIdentity(_manager, this, _sceneId, _asServer, isHost);
                    RegisterIdentity(nid, false, false);
                }

                if (!_asServer && _transferReconciliationRequested &&
                    !TryValidateRetainedSceneMembership(out var membershipFailure))
                {
                    AbortTransferReconciliation(
                        $"Scene {_sceneId} graph changed before fresh spawn {data.packetIdx} " +
                        $"could apply custom state: {membershipFailure}");
                    throw new InvalidOperationException(membershipFailure);
                }

                if (hasCustomData)
                {
                    for (var i = 0; i < createdNids.Count; i++)
                    {
                        var nid = createdNids[i];
                        if (nid)
                            nid.TriggerOnDeserialize(data.customData.packer);
                    }

                    if (!_asServer && _transferReconciliationRequested &&
                        !TryValidateRetainedSceneMembership(out membershipFailure))
                    {
                        AbortTransferReconciliation(
                            $"Scene {_sceneId} graph changed while fresh spawn {data.packetIdx} " +
                            $"applied custom state: {membershipFailure}");
                        throw new InvalidOperationException(membershipFailure);
                    }
                }

                for (var i = 0; i < createdNids.Count; i++)
                {
                    var nid = createdNids[i];
                    if (!nid)
                        continue;

                    TriggerEarlySpawnForRegisteredIdentity(nid);

                    if (_asServer)
                        nid.TryAddObserver(spawner);
                }

                for (var i = 0; i < createdNids.Count; i++)
                {
                    var nid = createdNids[i];
                    if (nid)
                        nid.TriggerOnSpawnReceived();
                }

                if (!_pendingSpawns.TryAdd(data.packetIdx, createdNids))
                {
                    PurrLogger.LogError($"CompleteSpawn: failed to add spawn packet {data.packetIdx} to pending spawns.");
                    RollbackFailedSpawn(data.packetIdx, go, createdNids, false);
                    return false;
                }
                ownsPendingEntry = true;

                if (data.isAsync && !_asServer)
                    _asyncPendingSpawns.Add(data.packetIdx);

                if (data.isAsync && !_asServer)
                    SendAsyncSpawnReady(data.packetIdx, true);

#if PURRNET_UNITY_INSTANTIATE_ASYNC
                if (_pendingAsyncInstantiations.Count > 0)
                    ProcessAsyncInstantiationsWaitingForParents();
#endif
                ProcessBufferedFinishSpawnsFor(data.packetIdx);
                ProcessBufferedDespawnsFor(data.prototype);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"CompleteSpawn: exception for packet {data.packetIdx}: {e.Message}\n{e.StackTrace}");

                // A buffered Finish may already have removed the entry and completed the spawn.
                // In that case an exception came from user spawn callbacks; the network transaction
                // is complete and must not be retroactively destroyed.
                if (ownsPendingEntry && !_pendingSpawns.ContainsKey(data.packetIdx))
                    return true;

                RollbackFailedSpawn(data.packetIdx, go, createdNids, ownsPendingEntry);
                return false;
            }

            if (flushData)
                FlushSpawnPackets();
            return true;
        }

        private void RollbackFailedSpawn(SpawnID packetIdx, GameObject go,
            DisposableList<NetworkIdentity> createdNids, bool ownsPendingEntry)
        {
            DisposableList<NetworkIdentity> pendingNids = default;
            if (ownsPendingEntry)
                _pendingSpawns.Remove(packetIdx, out pendingNids);

            _asyncPendingSpawns.Remove(packetIdx);
            _relayAsyncSpawns.Remove(packetIdx);

            if (!createdNids.isDisposed)
            {
                for (var i = 0; i < createdNids.Count; i++)
                    RollbackFailedIdentity(createdNids[i]);
            }
            else if (!pendingNids.isDisposed)
            {
                for (var i = 0; i < pendingNids.Count; i++)
                    RollbackFailedIdentity(pendingNids[i]);
            }
            else if (go)
            {
                var identities = ListPool<NetworkIdentity>.Instantiate();
                go.GetComponentsInChildren(true, identities);
                for (var i = 0; i < identities.Count; i++)
                    RollbackFailedIdentity(identities[i]);
                ListPool<NetworkIdentity>.Destroy(identities);
            }

            if (!pendingNids.isDisposed)
                pendingNids.Dispose();
            if (!createdNids.isDisposed)
                createdNids.Dispose();

            if (go)
                UnityProxy.DestroyDirectly(go);
        }

        private void RollbackFailedIdentity(NetworkIdentity identity)
        {
            if (!identity)
                return;

            _toSpawnNextFrame.Remove(identity);
            _toSpawnNextFrameBuffer.Remove(identity);
            for (var i = _triggerLateObserverAdded.Count - 1; i >= 0; i--)
            {
                if (_triggerLateObserverAdded[i].nid == identity)
                    _triggerLateObserverAdded.RemoveAt(i);
            }

            _spawnedIdentities.Remove(identity);
            if (!identity.id.HasValue ||
                !_spawnedIdentitiesMap.TryGetValue(identity.id.Value, out var registered) ||
                !ReferenceEquals(registered, identity))
            {
                return;
            }

            _spawnedIdentitiesMap.Remove(identity.id.Value);
            try
            {
                onIdentityRemoved?.Invoke(identity);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"CompleteSpawn: exception while rolling back '{identity.name}': {e.Message}\n{e.StackTrace}", identity);
            }
        }

        private void SendAsyncSpawnReady(SpawnID packetIdx, bool success)
        {
            if (_asServer || !_enabled || _isDisposed)
                return;

            _playersManager.SendToServer(new AsyncSpawnReadyPacket
            {
                sceneId = _sceneId,
                packetIdx = packetIdx,
                success = success
            });
        }

        private void SendAsyncSpawnFailure(SpawnPacket packet)
        {
            if (packet.prototype.framework.Count > 0)
            {
                var rootId = packet.prototype.framework[0].id;
                _failedAsyncSpawnRoots.Add(rootId);
#if PURRNET_UNITY_INSTANTIATE_ASYNC
                FailAsyncInstantiationsWaitingForParent(rootId);
#endif
                if (RemoveBufferedDespawnsFor(rootId))
                    _failedAsyncSpawnRoots.Remove(rootId);
            }
            SendAsyncSpawnReady(packet.packetIdx, false);
            if (!_asServer && _transferReconciliationRequested)
            {
                AbortTransferReconciliation(
                    $"Scene {_sceneId} could not materialize declared asynchronous spawn " +
                    $"{packet.packetIdx}.");
            }
        }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        private void BeginAsyncRemoteSpawn(SpawnPacket data, bool flushData)
        {
            if (data.prototype.framework.Count == 0)
            {
                SendAsyncSpawnFailure(data);
                return;
            }

            var reserved = ListPool<NetworkID>.Instantiate();
            for (var i = 0; i < data.prototype.framework.Count; i++)
            {
                var id = data.prototype.framework[i].id;
                if (_spawnedIdentitiesMap.ContainsKey(id) || !_reservedAsyncNetworkIds.Add(id))
                {
                    for (var j = 0; j < reserved.Count; j++)
                        _reservedAsyncNetworkIds.Remove(reserved[j]);
                    ListPool<NetworkID>.Destroy(reserved);
                    PurrLogger.LogError($"Async spawn packet {data.packetIdx} contains an identity id that is already active or pending: {id}.");
                    SendAsyncSpawnFailure(data);
                    return;
                }
                reserved.Add(id);
            }
            ListPool<NetworkID>.Destroy(reserved);

            int prefabId = data.prototype.framework[0].pid.prefabId;
            if (!_manager.prefabProvider.TryGetPrefabData(prefabId, out var prefabData) || !prefabData.prefab)
            {
                ReleaseAsyncReservations(data);
                SendAsyncSpawnFailure(data);
                return;
            }

            var packetCopy = new SpawnPacket
            {
                sceneId = data.sceneId,
                packetIdx = data.packetIdx,
                bypassPool = true,
                isAsync = true,
                prototype = data.prototype.Clone(),
                customData = data.customData.Duplicate()
            };

            var rootId = packetCopy.prototype.framework[0].id;
            var state = new PendingAsyncInstantiation
            {
                packet = packetCopy,
                flushData = flushData
            };

            try
            {
                state.operation = UnityProxy.InstantiateAsyncDirectly(prefabData.prefab);
                _pendingAsyncInstantiations.Add(rootId, state);
                state.operation.completed += _ => OnAsyncRemoteInstantiateCompleted(rootId, state, prefabData.prefab);
            }
            catch (Exception e)
            {
                _pendingAsyncInstantiations.Remove(rootId);
                ReleaseAsyncReservations(packetCopy);
                state.DisposePacket();
                PurrLogger.LogError($"Failed to start remote InstantiateAsync for `{prefabData.prefab.name}`: {e.Message}");
                SendAsyncSpawnFailure(data);
            }
        }

        private void OnAsyncRemoteInstantiateCompleted(NetworkID rootId, PendingAsyncInstantiation state,
            GameObject prefab)
        {
            GameObject result = null;
            try
            {
                var results = state.operation.Result;
                if (results != null && results.Length > 0)
                    result = results[0];

                if (results != null)
                {
                    for (var i = 1; i < results.Length; i++)
                    {
                        if (results[i])
                            UnityProxy.DestroyDirectly(results[i]);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is an expected outcome when visibility is revoked mid-operation.
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Remote InstantiateAsync for `{prefab.name}` failed: {e.Message}");
            }

            if (state.cancelled ||
                !_pendingAsyncInstantiations.TryGetValue(rootId, out var current) || current != state)
            {
                if (result)
                    UnityProxy.DestroyDirectly(result);
                return;
            }

            if (!_enabled || _isDisposed)
            {
                _pendingAsyncInstantiations.Remove(rootId);
                ReleaseAsyncReservations(state.packet);
                if (result)
                    UnityProxy.DestroyDirectly(result);
                state.DisposePacket();
                return;
            }

            if (!result)
            {
                FailPendingAsyncInstantiation(rootId, state, null);
                return;
            }

            int prefabId = state.packet.prototype.framework[0].pid.prefabId;
            var identities = ListPool<NetworkIdentity>.Instantiate();
            try
            {
                result.GetComponentsInChildren(true, identities);
                NetworkManager.SetupPrefabInfo(result, prefabId, false, identities);
                if (!HasMatchingAsyncNetworkShape(prefab, result, identities, out var mismatch))
                {
                    ReportAsyncShapeMismatch(prefab, result, mismatch);
                    FailPendingAsyncInstantiation(rootId, state, result);
                    return;
                }
            }
            finally
            {
                ListPool<NetworkIdentity>.Destroy(identities);
            }

            state.result = result;
            TryCompletePendingAsyncInstantiation(rootId, state);
        }

        private void TryCompletePendingAsyncInstantiation(NetworkID rootId, PendingAsyncInstantiation state)
        {
            if (state.cancelled || !state.result || !_enabled || _isDisposed ||
                !_pendingAsyncInstantiations.TryGetValue(rootId, out var current) || current != state)
                return;

            if (state.packet.prototype.parentID.HasValue &&
                !TryGetIdentity(state.packet.prototype.parentID.Value, out _))
            {
                if (_failedAsyncSpawnRoots.Contains(state.packet.prototype.parentID.Value))
                    FailPendingAsyncInstantiation(rootId, state, state.result);
                return;
            }

            ReleaseAsyncReservations(state.packet);

            for (var i = 0; i < state.packet.prototype.framework.Count; i++)
            {
                if (!TryGetIdentity(state.packet.prototype.framework[i].id, out _))
                    continue;
                FailPendingAsyncInstantiation(rootId, state, state.result);
                return;
            }

            var createdNids = DisposableList<NetworkIdentity>.Create(16);
            if (!TryApplyPrototypeToExisting(state.result, state.packet.prototype, createdNids.list,
                    out var shouldActivate))
            {
                createdNids.Dispose();
                PurrLogger.LogError(
                    $"`InstantiateAsync` could not apply spawn packet {state.packet.packetIdx} because the receiver got a partial or mismatched NetworkIdentity hierarchy.",
                    state.result);
                FailPendingAsyncInstantiation(rootId, state, state.result);
                return;
            }

            var result = FinalizePrototypeInstance(state.result, state.packet.prototype, shouldActivate);
            _pendingAsyncInstantiations.Remove(rootId);
            state.result = null;

            _hasConfiguredPoolBypass = true;
            bool completed = CompleteSpawnWithInstance(state.packet, state.flushData, result, createdNids);
            if (!completed)
            {
                SendAsyncSpawnFailure(state.packet);
            }
            state.DisposePacket();
        }

        private void ProcessAsyncInstantiationsWaitingForParents()
        {
            if (_pendingAsyncInstantiations.Count == 0)
                return;

            var ready = ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Instantiate();
            foreach (var pair in _pendingAsyncInstantiations)
            {
                var state = pair.Value;
                if (!state.result)
                    continue;
                if (!state.packet.prototype.parentID.HasValue ||
                    TryGetIdentity(state.packet.prototype.parentID.Value, out _))
                    ready.Add((pair.Key, state));
            }

            for (var i = 0; i < ready.Count; i++)
                TryCompletePendingAsyncInstantiation(ready[i].id, ready[i].state);
            ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Destroy(ready);
        }

        private void FailPendingAsyncInstantiation(NetworkID rootId, PendingAsyncInstantiation state,
            GameObject result)
        {
            _pendingAsyncInstantiations.Remove(rootId);
            ReleaseAsyncReservations(state.packet);
            if (result)
                UnityProxy.DestroyDirectly(result);
            state.result = null;
            SendAsyncSpawnFailure(state.packet);
            state.DisposePacket();
        }

        private void FailAsyncInstantiationsWaitingForParent(NetworkID failedParent)
        {
            if (_pendingAsyncInstantiations.Count == 0)
                return;

            var dependants = ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Instantiate();
            foreach (var pair in _pendingAsyncInstantiations)
            {
                if (pair.Value.packet.prototype.parentID == failedParent)
                    dependants.Add((pair.Key, pair.Value));
            }

            for (var i = 0; i < dependants.Count; i++)
            {
                var dependant = dependants[i];
                if (_pendingAsyncInstantiations.TryGetValue(dependant.id, out var current) &&
                    current == dependant.state)
                    FailPendingAsyncInstantiation(dependant.id, dependant.state, dependant.state.result);
            }
            ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Destroy(dependants);
        }

        private void ReleaseAsyncReservations(SpawnPacket packet)
        {
            for (var i = 0; i < packet.prototype.framework.Count; i++)
                _reservedAsyncNetworkIds.Remove(packet.prototype.framework[i].id);
        }
#endif

        private bool TryCancelPendingAsyncInstantiation(NetworkID rootId)
        {
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            if (_pendingAsyncInstantiations.Count == 0)
                return false;

            if (!_pendingAsyncInstantiations.Remove(rootId, out var state))
                return false;

            state.cancelled = true;
            var packetIdx = state.packet.packetIdx;
            _failedAsyncSpawnRoots.Add(rootId);
            ReleaseAsyncReservations(state.packet);
            try
            {
                state.operation?.Cancel();
            }
            catch (Exception e)
            {
                PurrLogger.LogWarning($"Cancelling remote InstantiateAsync failed: {e.Message}");
            }

            if (state.result)
                UnityProxy.DestroyDirectly(state.result);
            state.result = null;
            FailAsyncInstantiationsWaitingForParent(rootId);
            _failedAsyncSpawnRoots.Remove(rootId);
            state.DisposePacket();

            for (var i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                if (_pendingFinishSpawns[i].packetIdx.Equals(packetIdx))
                    _pendingFinishSpawns.RemoveAt(i);
            }
            return true;
#else
            return false;
#endif
        }

        private void RollbackSpawnOnClient(PlayerID player, SpawnPacket data)
        {
            if (_asServer)
                _cancelledPendingSpawns.Add(data.packetIdx);

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

            if (!_asServer && _failedAsyncSpawnRoots.Count > 0 &&
                _failedAsyncSpawnRoots.Remove(data.parentId))
                return;

            if (!_asServer && TryCancelPendingAsyncInstantiation(data.parentId))
                return;

            if (!TryGetIdentity(data.parentId, out var identity))
            {
                if (!_asServer && !ConsumePendingLocalDespawnEcho(data.parentId))
                    _pendingDespawns.Add((player, data, asServer));
                return;
            }

            if (_asServer && !identity.HasDespawnAuthority(player, !_asServer))
            {
                PurrLogger.LogError($"Despawn failed for '{identity.gameObject.name}' due to lack of permissions.",
                    identity.gameObject);
                return;
            }

            CancelPendingAsyncSpawnRoot(identity);
            Despawn(identity.gameObject, true, true);
        }

        private bool ConsumePendingLocalDespawnEcho(NetworkID identityId)
        {
            if (_asServer || _pendingLocalDespawnEchoes.isDisposed ||
                !_pendingLocalDespawnEchoes.Remove(identityId))
                return false;

            if (_pendingLocalDespawnEchoes.Count == 0)
                _pendingLocalDespawnEchoes.Dispose();
            return true;
        }

        private void CancelPendingAsyncSpawnRoot(NetworkIdentity identity)
        {
            if (_asyncPendingSpawns.Count == 0 || !identity)
                return;

            // A nested despawn is part of the staged transaction: FinishSpawn must still
            // complete its surviving identities. Only cancelling the async root removes
            // the whole transaction because the server may intentionally omit its Finish.
            SpawnID found = default;
            DisposableList<NetworkIdentity> list = default;
            bool hasFound = false;

            foreach (var packetIdx in _asyncPendingSpawns)
            {
                if (!_pendingSpawns.TryGetValue(packetIdx, out var pending) ||
                    pending.Count == 0 || pending[0] != identity)
                    continue;

                found = packetIdx;
                list = pending;
                hasFound = true;
                break;
            }

            if (!hasFound)
                return;

            _pendingSpawns.Remove(found);
            if (!list.isDisposed)
                list.Dispose();
            _asyncPendingSpawns.Remove(found);

            for (var i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                if (_pendingFinishSpawns[i].packetIdx.Equals(found))
                    _pendingFinishSpawns.RemoveAt(i);
            }
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

        internal bool TryPreflightExactSceneSnapshot(PlayerID player,
            HostMigrationTransitionOptions session, out HierarchyV2 promotedListenClient,
            out string failure)
        {
            promotedListenClient = null;
            failure = null;
            if (!_asServer || !session.canReconcile ||
                !_playersManager.IsPendingRetainedHostMigrationPlayer(player, session))
            {
                failure = $"Scene {_sceneId} is not serving pending exact player {player} for {session}.";
                return false;
            }

            if (HasQueuedSpawnWork(player))
            {
                failure = $"Scene {_sceneId} cannot stage an exact snapshot while that player's queue is non-empty.";
                return false;
            }

            if (!TryValidateRetainedSceneMembership(out failure))
                return false;

            if (!_playersManager.TryBeginExactOutboundBarrier(player, session, out failure))
                return false;

            if (!IsServerHost() || _manager.localPlayer != player)
                return true;

            if (!_manager.TryGetModule<HierarchyFactory>(false, out var factory) ||
                !factory.TryGetHierarchy(_sceneId, out promotedListenClient))
            {
                failure = $"Scene {_sceneId} has no fresh listen-client hierarchy to bind.";
                return false;
            }

            return promotedListenClient.CanAttachPromotedListenGraph(this, out failure);
        }

        internal bool TryStagePromotedListenSnapshotPlan(PlayerID player,
            HostMigrationTransitionOptions session, HierarchyV2 promotedListenClient,
            out ExactSceneSnapshotPlan plan, out string failure)
        {
            plan = null;
            failure = null;
            RetainedSceneGraphProof graphProof = null;
            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var preamble = default(SceneSpawnReconcileBeginPacket);
            var ownsPreamble = false;
            try
            {
                if (!TryCaptureRetainedSceneGraphProof(out graphProof, out failure))
                    return false;

                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    if (!identity || identity.isManualSpawn)
                        continue;
                    var root = identity.GetRootIdentity();
                    if (root)
                        roots.Add(root);
                }

                preamble = BuildPromotedListenPreamble(player, roots, session);
                ownsPreamble = true;
                if (!promotedListenClient.TryStagePromotedListenManifest(
                        this, ref preamble, out var manifest, out failure))
                    return false;

                plan = new ExactSceneSnapshotPlan(
                    this, player, session, promotedListenClient, preamble)
                {
                    promotedManifest = manifest
                };
                plan.graphProof = graphProof;
                graphProof = null;
                ownsPreamble = false;
                return true;
            }
            finally
            {
                graphProof?.Dispose();
                if (ownsPreamble)
                    preamble.Dispose();
                HashSetPool<NetworkIdentity>.Destroy(roots);
            }
        }

        internal bool TryPrepareExactSceneSnapshot(PlayerID player,
            HostMigrationTransitionOptions session, HierarchyV2 promotedListenClient,
            ExactSceneSnapshotPlan stagedPlan, out ExactSceneSnapshotPlan plan,
            out string failure)
        {
            plan = stagedPlan;
            failure = null;
            if (promotedListenClient != null && stagedPlan == null)
            {
                failure = $"Scene {_sceneId} has no pure promoted-listen topology plan.";
                return false;
            }

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var preamble = default(SceneSpawnReconcileBeginPacket);
            var ownsPreamble = false;
            RetainedSceneGraphProof graphProof = null;
            ExactSceneSnapshotStagingJournal stagingJournal = null;
            var journalAttached = false;
            try
            {
                if (_activeExactSnapshotStagingJournal != null)
                {
                    failure = $"Scene {_sceneId} is already staging another exact snapshot.";
                    return false;
                }

                if (stagedPlan == null &&
                    !TryCaptureRetainedSceneGraphProof(out graphProof, out failure))
                    return false;

                stagingJournal = new ExactSceneSnapshotStagingJournal(this, player);
                _activeExactSnapshotStagingJournal = stagingJournal;
                _buildingExactSnapshotForPlayer = player;
                _buildingExactSnapshotTransition = session;

                if (IsServerHost() && _manager.localPlayer == player)
                    CatchupClient(player);

                for (var i = 0; i < _spawnedIdentities.Count; i++)
                {
                    var identity = _spawnedIdentities[i];
                    if (!identity || identity.isManualSpawn)
                        continue;
                    var root = identity.GetRootIdentity();
                    if (root && roots.Add(root))
                        _visibility.RefreshVisibilityForGameObject(player, root.transform);
                }

                if (_spawnPackets.TryGetValue(player, out var queued) &&
                    !queued.despawnPackets.isDisposed && queued.despawnPackets.Count > 0)
                {
                    failure = $"Scene {_sceneId} generated despawns while staging its exact snapshot.";
                    return false;
                }

                if (promotedListenClient != null)
                {
                    if (_spawnPackets.TryGetValue(player, out queued) &&
                        !queued.spawnPackets.isDisposed && queued.spawnPackets.Count > 0)
                    {
                        failure = $"Scene {_sceneId} generated loopback spawns for a directly-bound listen graph.";
                        return false;
                    }

                    plan.syntheticFinishes ??= ListPool<SpawnID>.Instantiate();
                    plan.AttachStagingJournal(stagingJournal);
                    journalAttached = true;
                    return true;
                }

                preamble = BuildQueuedSpawnPreamble(player, session);
                ownsPreamble = true;
                plan = new ExactSceneSnapshotPlan(this, player, session, null, preamble);
                plan.graphProof = graphProof;
                graphProof = null;
                ownsPreamble = false;
                if (_spawnPackets.Remove(player, out var batch))
                {
                    plan.batch = batch;
                    plan.ownsBatch = true;
                }
                plan.AttachStagingJournal(stagingJournal);
                journalAttached = true;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"Scene {_sceneId} could not stage its exact snapshot: {exception.Message}";
                return false;
            }
            finally
            {
                if (ReferenceEquals(_activeExactSnapshotStagingJournal, stagingJournal))
                    _activeExactSnapshotStagingJournal = null;
                if (_buildingExactSnapshotForPlayer == player &&
                    _buildingExactSnapshotTransition == session)
                {
                    _buildingExactSnapshotForPlayer = null;
                    _buildingExactSnapshotTransition = default;
                }

                if (ownsPreamble)
                    preamble.Dispose();
                graphProof?.Dispose();
                if (!journalAttached)
                    stagingJournal?.Dispose();
                HashSetPool<NetworkIdentity>.Destroy(roots);
            }
        }

        internal bool TryValidateExactSceneSnapshotPlan(
            ExactSceneSnapshotPlan plan, out string failure)
        {
            if (plan == null || !ReferenceEquals(plan.hierarchy, this))
            {
                failure = $"Scene {_sceneId} received an exact snapshot plan for another hierarchy.";
                return false;
            }

            if (!TryValidateRetainedSceneMembership(out failure) ||
                !TryValidateRetainedSceneGraphProof(
                    plan.graphProof as RetainedSceneGraphProof, out failure))
                return false;

            if (plan.promotedListenClient == null)
                return true;

            if (!plan.promotedListenClient.TryValidateRetainedSceneMembership(out failure) ||
                !plan.promotedListenClient.TryValidateRetainedSceneGraphProof(
                    plan.promotedClientGraphProof as RetainedSceneGraphProof, out failure))
            {
                failure = $"the promoted listen-client graph changed: {failure}";
                return false;
            }

            return true;
        }

        internal bool TryCapturePromotedClientSnapshotPlanProof(
            ExactSceneSnapshotPlan plan, out string failure)
        {
            failure = null;
            if (plan == null || !ReferenceEquals(plan.promotedListenClient, this) ||
                plan.promotedClientGraphProof != null)
            {
                failure = $"Scene {_sceneId} cannot bind a promoted client graph proof to this plan.";
                return false;
            }

            if (!TryCaptureRetainedSceneGraphProof(out var proof, out failure))
                return false;

            plan.promotedClientGraphProof = proof;
            return true;
        }

        internal bool TryPublishExactScenePreamble(ExactSceneSnapshotPlan plan, out string failure)
        {
            failure = null;
            if (plan.promotedListenClient != null)
                return true;
            if (_playersManager.SendExactBarrierBypass(
                    plan.player, plan.transition, plan.preamble, Channel.ReliableOrdered))
                return true;
            failure = $"Scene {_sceneId} lost its exact outbound barrier before its topology preamble.";
            return false;
        }

        internal bool TryPublishExactSpawnTopology(ExactSceneSnapshotPlan plan, out string failure)
        {
            failure = null;
            if (!plan.ownsBatch)
                return true;
            if (_playersManager.SendExactBarrierBypass(
                    plan.player, plan.transition, plan.batch, Channel.ReliableOrdered))
                return true;
            failure = $"Scene {_sceneId} lost its exact outbound barrier before its spawn topology batch.";
            return false;
        }

        internal bool TryCommitExactSceneSnapshot(ExactSceneSnapshotPlan plan, out string failure)
        {
            failure = null;
            if (plan.promotedListenClient != null &&
                !plan.promotedListenClient.TryApplyAcceptedPromotedListenManifest(
                    plan.syntheticFinishes, out failure))
                return false;

            try
            {
                if (!_playersManager.RunExactOutboundBarrierBypass(
                        plan.player, plan.transition, () =>
                        {
                            CommitPreparedExactSpawnBatch(plan);
                            _manager.FlushBatchedRPCs();
                        }))
                {
                    failure = $"Scene {_sceneId} lost its exact outbound barrier while committing snapshot baselines.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure = $"Scene {_sceneId} snapshot baseline failed: {exception.Message}";
                return false;
            }

            if (plan.syntheticFinishes != null)
            {
                for (var i = 0; i < plan.syntheticFinishes.Count; i++)
                {
                    var finish = plan.syntheticFinishes[i];
                    _exactBarrierBypassFinishes[finish] = plan.transition;
                    _toCompleteNextFrame.Add(finish);
                }
            }

            _sceneReconcileEndsNextFrame.Add(new PendingSceneReconcileEnd(
                plan.player,
                new SceneSpawnReconcilePacket
                {
                    sceneId = _sceneId,
                    sessionId = plan.transition.sessionId,
                    epoch = plan.transition.epoch
                },
                plan.promotedListenClient));
            return true;
        }

        private void CommitPreparedExactSpawnBatch(ExactSceneSnapshotPlan plan)
        {
            if (!plan.ownsBatch)
                return;

            var batch = plan.batch;
            var count = batch.spawnPackets.isDisposed ? 0 : batch.spawnPackets.Count;
            for (var i = 0; i < count; i++)
            {
                var packet = batch.spawnPackets[i];
                if (packet.isAsync &&
                    _pendingAsyncObservers.TryGetValue(packet.packetIdx, out var pendingAsync))
                    pendingAsync.sent = true;

                if (packet.localcache != null)
                {
                    for (var j = 0; j < packet.localcache.Count; j++)
                    {
                        var piece = packet.localcache[j];
                        if (piece && piece.id.HasValue)
                            onSentSpawnPacket?.Invoke(plan.player, _sceneId, piece.id.Value);
                    }
                }
                else if (!packet.prototype.framework.isDisposed)
                {
                    for (var j = 0; j < packet.prototype.framework.Count; j++)
                        onSentSpawnPacket?.Invoke(
                            plan.player, _sceneId, packet.prototype.framework[j].id);
                }

                _exactBarrierBypassFinishes[packet.packetIdx] = plan.transition;
                if (!(_asServer && packet.isAsync))
                    _toCompleteNextFrame.Add(packet.packetIdx);
            }

            plan.ownsBatch = false;
            batch.Dispose();
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (!_asServer)
                return;

            if (scene != _sceneId)
                return;

            var session = _manager.hostMigrationSession;
            var isExactReconciliation = session.canReconcile &&
                                        _playersManager.IsPendingRetainedHostMigrationPlayer(
                                            player, session);
            if (isExactReconciliation)
            {
                RejectExactSpawnSnapshot(player, session,
                    $"Scene {_sceneId} exact acknowledgement bypassed the transaction-wide hierarchy gate.");
                return;
            }

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            try
            {
                if (IsServerHost() && _manager.localPlayer == player)
                    CatchupClient(player);

                var count = _spawnedIdentities.Count;
                for (var i = 0; i < count; i++)
                {
                    var id = _spawnedIdentities[i];
                    if (!id || id.isManualSpawn)
                        continue;

                    var root = id.GetRootIdentity();
                    if (root && roots.Add(root))
                        _visibility.RefreshVisibilityForGameObject(player, root.transform);
                }

                FlushSpawnPackets();

                _playersManager.Send(player, new SceneSpawnReconcilePacket
                {
                    sceneId = _sceneId,
                    sessionId = session.sessionId,
                    epoch = session.epoch
                }, Channel.ReliableOrdered);
            }
            finally
            {
                HashSetPool<NetworkIdentity>.Destroy(roots);
            }
        }

        private bool HasQueuedSpawnWork(PlayerID player)
        {
            if (!_spawnPackets.TryGetValue(player, out var batch))
                return false;

            return (!batch.spawnPackets.isDisposed && batch.spawnPackets.Count > 0) ||
                   (!batch.despawnPackets.isDisposed && batch.despawnPackets.Count > 0);
        }

        private void RejectExactSpawnSnapshot(PlayerID player,
            HostMigrationTransitionOptions session, string reason)
        {
            PurrLogger.LogError(reason);
            if (_spawnPackets.Remove(player, out var batch))
                batch.Dispose();

            if (IsServerHost() && _manager.localPlayer == player &&
                _manager.TryGetModule<HierarchyFactory>(false, out var clientFactory) &&
                clientFactory.TryGetHierarchy(_sceneId, out var localClient))
            {
                localClient.AbortTransferReconciliation(reason);
                return;
            }

            var abort = new SceneSpawnReconcileAbortPacket
            {
                sceneId = _sceneId,
                sessionId = session.sessionId,
                epoch = session.epoch,
                reason = reason
            };
            if (!_playersManager.SendExactBarrierBypass(
                    player, session, abort, Channel.ReliableOrdered))
                _playersManager.Send(player, abort, Channel.ReliableOrdered);
        }

        internal void RejectExactSpawnSnapshotFromFactory(PlayerID player,
            HostMigrationTransitionOptions session, string reason)
        {
            RejectExactSpawnSnapshot(player, session, reason);
        }

        private SceneSpawnReconcileBeginPacket BuildQueuedSpawnPreamble(PlayerID player,
            HostMigrationTransitionOptions session)
        {
            var topologies = DisposableList<SceneSpawnReconcileSpawnTopology>.Create();
            if (_spawnPackets.TryGetValue(player, out var batch) && !batch.spawnPackets.isDisposed)
            {
                for (var i = 0; i < batch.spawnPackets.Count; i++)
                {
                    var spawn = batch.spawnPackets[i];
                    topologies.Add(new SceneSpawnReconcileSpawnTopology
                    {
                        spawnId = spawn.packetIdx,
                        bypassPool = spawn.bypassPool,
                        isAsync = spawn.isAsync,
                        prototype = spawn.prototype.Clone()
                    });
                }
            }

            return new SceneSpawnReconcileBeginPacket
            {
                sceneId = _sceneId,
                sessionId = session.sessionId,
                epoch = session.epoch,
                spawns = topologies
            };
        }

        private SceneSpawnReconcileBeginPacket BuildPromotedListenPreamble(PlayerID player,
            HashSet<NetworkIdentity> regularRoots, HostMigrationTransitionOptions session)
        {
            var topologies = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(regularRoots.Count);
            foreach (var root in regularRoots)
            {
                if (!root || !root.id.HasValue)
                    continue;

                topologies.Add(new SceneSpawnReconcileSpawnTopology
                {
                    spawnId = new SpawnID(_nextPacketIdx++, player, _playersManager.localPlayerId),
                    prototype = HierarchyPool.GetFullPrototype(root.transform, null, true)
                });
            }

            return new SceneSpawnReconcileBeginPacket
            {
                sceneId = _sceneId,
                sessionId = session.sessionId,
                epoch = session.epoch,
                spawns = topologies
            };
        }

        internal bool CanAttachPromotedListenGraph(HierarchyV2 serverHierarchy, out string failure)
        {
            failure = null;
            if (_asServer || serverHierarchy == null || !serverHierarchy._asServer ||
                !ReferenceEquals(_manager, serverHierarchy._manager) ||
                _sceneId != serverHierarchy._sceneId || _scene != serverHierarchy._scene)
            {
                failure = "The promoted listen graph does not belong to the paired server/client scene.";
                return false;
            }

            if (!serverHierarchy.TryValidateRetainedSceneMembership(out failure))
                return false;

            var serverIdentities = serverHierarchy._spawnedIdentities;
            if (serverHierarchy._spawnedIdentitiesMap.Count != serverIdentities.Count)
            {
                failure = $"Scene {_sceneId} server list/map registry is incomplete.";
                return false;
            }

            if (_spawnedIdentities.Count != _spawnedIdentitiesMap.Count ||
                (_spawnedIdentities.Count != 0 &&
                 _spawnedIdentities.Count != serverIdentities.Count))
            {
                failure = $"Scene {_sceneId} has a partial fresh-client registry; refusing a mixed graph bind.";
                return false;
            }

            var clientRegistryEmpty = _spawnedIdentities.Count == 0;
            var seen = new HashSet<NetworkID>();
            HashSet<NetworkIdentity> existingClientIdentities = null;
            if (!clientRegistryEmpty)
            {
                existingClientIdentities = new HashSet<NetworkIdentity>(_spawnedIdentities);
                if (existingClientIdentities.Count != _spawnedIdentities.Count)
                {
                    failure = $"Scene {_sceneId} fresh-client list contains duplicate identities.";
                    return false;
                }
            }

            for (var i = 0; i < serverIdentities.Count; i++)
            {
                var identity = serverIdentities[i];
                var id = identity ? identity.GetNetworkID(true) : null;
                if (!identity || !identity.IsSpawned(true) || !id.HasValue || !seen.Add(id.Value))
                {
                    failure = $"Scene {_sceneId} server graph contains a dead or duplicate identity.";
                    return false;
                }

                if (!serverHierarchy._spawnedIdentitiesMap.TryGetValue(
                        id.Value, out var registeredServer) ||
                    !ReferenceEquals(registeredServer, identity))
                {
                    failure = $"Scene {_sceneId} server registry conflicts at NetworkID {id.Value}.";
                    return false;
                }

                if (!identity.CanAttachPromotedListenClientRole(this, out failure))
                    return false;

                if (!clientRegistryEmpty &&
                    (!_spawnedIdentitiesMap.TryGetValue(id.Value, out var registeredClient) ||
                     !ReferenceEquals(registeredClient, identity) ||
                     !existingClientIdentities.Contains(identity)))
                {
                    failure = $"Scene {_sceneId} fresh-client registry conflicts at NetworkID {id.Value}.";
                    return false;
                }
            }

            if (!clientRegistryEmpty && _spawnedIdentitiesMap.Count != seen.Count)
            {
                failure = $"Scene {_sceneId} fresh-client map contains identities outside the promoted graph.";
                return false;
            }

            return true;
        }

        internal bool TryStagePromotedListenManifest(HierarchyV2 serverHierarchy,
            ref SceneSpawnReconcileBeginPacket preamble,
            out SceneSpawnReconcileManifest manifest, out string failure)
        {
            manifest = null;
            failure = null;
            if (!CanAttachPromotedListenGraph(serverHierarchy, out failure))
                return false;

            var existingRootByIdentity = new Dictionary<NetworkID, NetworkID>();
            var retainedTopologyByRoot = new Dictionary<NetworkID, GameObjectPrototype>();
            try
            {
                for (var i = 0; i < serverHierarchy._spawnedIdentities.Count; i++)
                {
                    var identity = serverHierarchy._spawnedIdentities[i];
                    var root = identity ? identity.GetRootIdentity() : null;
                    var identityId = identity ? identity.GetNetworkID(true) : null;
                    var rootId = root ? root.GetNetworkID(true) : null;
                    if (!identity || !root || !identityId.HasValue || !rootId.HasValue ||
                        !existingRootByIdentity.TryAdd(identityId.Value, rootId.Value))
                    {
                        failure = $"Scene {_sceneId} promoted graph has no stable unique root mapping.";
                        return false;
                    }
                }

                for (var i = 0; i < serverHierarchy._spawnedIdentities.Count; i++)
                {
                    var identity = serverHierarchy._spawnedIdentities[i];
                    var root = identity ? identity.GetRootIdentity() : null;
                    if (!identity || identity.isManualSpawn || !ReferenceEquals(identity, root))
                        continue;
                    var rootId = root.GetNetworkID(true);
                    if (!rootId.HasValue)
                    {
                        failure = $"Scene {_sceneId} promoted regular root has no server NetworkID.";
                        return false;
                    }

                    var topology = HierarchyPool.GetFullPrototype(root.transform, null, true);
                    if (!retainedTopologyByRoot.TryAdd(rootId.Value, topology))
                    {
                        topology.Dispose();
                        failure = $"Scene {_sceneId} promoted regular root {rootId.Value} is duplicated.";
                        return false;
                    }
                }

                if (preamble.spawns.isDisposed ||
                    preamble.spawns.Count != retainedTopologyByRoot.Count)
                {
                    failure = $"Scene {_sceneId} promoted topology declared " +
                              $"{(preamble.spawns.isDisposed ? 0 : preamble.spawns.Count)} roots, " +
                              $"expected {retainedTopologyByRoot.Count}.";
                    return false;
                }

                if (!SceneSpawnReconcileManifest.TryCreate(preamble.spawns,
                        existingRootByIdentity, retainedTopologyByRoot, false,
                        out manifest, out failure))
                    return false;

                preamble.spawns = default;
                return true;
            }
            finally
            {
                foreach (var topology in retainedTopologyByRoot.Values)
                    topology.Dispose();
            }
        }

        internal bool TryInstallPromotedListenManifest(
            SceneSpawnReconcileManifest manifest,
            HostMigrationTransitionOptions transition, out string failure)
        {
            failure = null;
            if (manifest == null || !_transferReconciliationRequested ||
                !_transferSessionValidated || transition != _transferReconciliationOptions ||
                _transferPreambleReceived || _expectedTransferSpawnManifest != null)
            {
                failure = $"Scene {_sceneId} cannot install its staged promoted-listen topology manifest.";
                return false;
            }

            if (manifest.count != _retainedTransferRoots.Count ||
                !TryValidateRetainedSceneMembership(out failure))
            {
                failure ??= $"Scene {_sceneId} promoted-listen retained graph changed after pure topology proof.";
                return false;
            }

            _expectedTransferSpawnManifest = manifest;
            _transferPreambleReceived = true;
            return true;
        }

        internal bool TryAttachPromotedListenGraphCore(HierarchyV2 serverHierarchy,
            out List<NetworkIdentity> newlyRegistered, out string failure)
        {
            newlyRegistered = null;
            if (!CanAttachPromotedListenGraph(serverHierarchy, out failure))
                return false;

            var serverIdentities = serverHierarchy._spawnedIdentities;
            var clientRegistryEmpty = _spawnedIdentities.Count == 0;
            var registered = clientRegistryEmpty
                ? new List<NetworkIdentity>(serverIdentities.Count)
                : new List<NetworkIdentity>();
            try
            {
                for (var i = 0; i < serverIdentities.Count; i++)
                {
                    var identity = serverIdentities[i];
                    if (!identity.AttachPromotedListenClientRole(this, out _, out failure))
                        return false;
                }

                if (clientRegistryEmpty)
                {
                    for (var i = 0; i < serverIdentities.Count; i++)
                    {
                        var identity = serverIdentities[i];
                        var id = identity.GetNetworkID(false).Value;
                        _spawnedIdentities.Add(identity);
                        _spawnedIdentitiesMap.Add(id, identity);
                        registered.Add(identity);
                    }
                }
            }
            catch (Exception exception)
            {
                failure = $"Scene {_sceneId} could not attach its promoted listen graph: {exception.Message}";
                return false;
            }

            newlyRegistered = registered;
            return true;
        }

        internal bool TryPublishPromotedListenRegistrySignals(
            List<NetworkIdentity> newlyRegistered, out string failure)
        {
            failure = null;
            if (newlyRegistered == null || newlyRegistered.Count == 0)
                return true;

            var signalFailures = new List<Exception>();
            for (var i = 0; i < newlyRegistered.Count; i++)
            {
                InvokePromotedListenRegistrySignal(onEarlyIdentityAdded,
                    newlyRegistered[i], "early identity-added", signalFailures);
            }

            for (var i = 0; i < newlyRegistered.Count; i++)
            {
                InvokePromotedListenRegistrySignal(onIdentityAdded,
                    newlyRegistered[i], "identity-added", signalFailures);
            }

            if (signalFailures.Count == 0)
                return true;

            failure = new AggregateException(
                $"Scene {_sceneId} promoted-listen registry signals failed after the full graph was published.",
                signalFailures).ToString();
            return false;
        }

        internal bool TryCapturePromotedListenTransfer(
            HostMigrationTransitionOptions transition,
            ref SceneSpawnReconcileManifest stagedManifest, out string failure)
        {
            BeginTransferReconciliation();
            ReceiveHostMigrationSession(transition, true);
            if (_transferReconciliationFailure != null)
            {
                failure = _transferReconciliationFailure.Message;
                return false;
            }

            if (!TryInstallPromotedListenManifest(stagedManifest, transition, out failure))
                return false;

            stagedManifest = null;
            return true;
        }

        private static void InvokePromotedListenRegistrySignal(IdentityAction signal,
            NetworkIdentity identity, string phase, List<Exception> failures)
        {
            if (signal == null)
                return;

            var subscribers = signal.GetInvocationList();
            for (var i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((IdentityAction)subscribers[i])(identity);
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        $"{phase} subscriber failed for {identity.name}.", exception));
                }
            }
        }

        internal bool TryApplyAcceptedPromotedListenManifest(
            List<SpawnID> syntheticFinishes, out string failure)
        {
            failure = null;
            if (!_transferReconciliationArmed || _expectedTransferSpawnManifest == null)
            {
                failure = $"Scene {_sceneId} cannot apply promoted-listen state before the " +
                          "transaction-wide topology gate is armed.";
                return false;
            }

            var count = _expectedTransferSpawnManifest.count;
            for (var i = 0; i < count; i++)
            {
                var topology = _expectedTransferSpawnManifest.GetTopology(i);
                var synthetic = new SpawnPacket
                {
                    sceneId = _sceneId,
                    packetIdx = topology.spawnId,
                    bypassPool = topology.bypassPool,
                    isAsync = topology.isAsync,
                    prototype = topology.prototype
                };

                if (!_expectedTransferSpawnManifest.TryConsume(_sceneId, synthetic,
                        out var classification, out failure) || !classification.isRetained)
                {
                    failure ??= $"Synthetic spawn {topology.spawnId} did not classify as one retained root.";
                    return false;
                }

                if (TryReconcileRetainedSpawn(synthetic, classification.retainedRootId) !=
                    RetainedSpawnResult.Reconciled)
                {
                    failure = _transferReconciliationFailure?.Message ??
                              $"Synthetic spawn {topology.spawnId} could not reconcile its retained root.";
                    return false;
                }

                syntheticFinishes.Add(topology.spawnId);
            }

            return true;
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
            public HostMigrationTransitionOptions exactTransition;
        }

        private readonly List<PlayerNid> _triggerLateObserverAdded = new List<PlayerNid>();
        private readonly Dictionary<PlayerID, SpawnPacketBatch> _spawnPackets = new();

        private PlayerNid CreateLateObserverEntry(PlayerID player,
            NetworkIdentity identity, bool isSpawner)
        {
            var exactTransition = _buildingExactSnapshotForPlayer.HasValue &&
                                  _buildingExactSnapshotForPlayer.Value == player
                ? _buildingExactSnapshotTransition
                : default;
            return new PlayerNid
            {
                player = player,
                nid = identity,
                isSpawner = isSpawner,
                exactTransition = exactTransition
            };
        }

        private void ClearPendingLateObserverAdded(PlayerID player, NetworkIdentity id)
        {
            for (var i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                if (_triggerLateObserverAdded[i].player == player && _triggerLateObserverAdded[i].nid == id)
                    _triggerLateObserverAdded.RemoveAt(i--);
            }
        }

        private void RecordExactSnapshotObserverLifecycle(PlayerID player,
            NetworkIdentity identity, ExactObserverLifecycle lifecycle, bool isSpawner = false)
        {
            if (_activeExactSnapshotStagingJournal != null &&
                _activeExactSnapshotStagingJournal.IsFor(player))
            {
                _activeExactSnapshotStagingJournal.Record(identity, lifecycle, isSpawner);
            }
        }

        private void RollbackExactSnapshotStaging(ExactSceneSnapshotStagingJournal journal)
        {
            if (journal == null)
                return;

            if (ReferenceEquals(_activeExactSnapshotStagingJournal, journal))
                _activeExactSnapshotStagingJournal = null;

            DropStagedExactSpawnBatch(journal);
            journal.RestoreState();
            journal.CompensateLifecycle();

            journal.RestoreState();
            DropStagedExactSpawnBatch(journal);
        }

        private void DropStagedExactSpawnBatch(ExactSceneSnapshotStagingJournal journal)
        {
            if (_spawnPackets.Remove(journal.player, out var batch))
                batch.Dispose();
        }

        private void InvokeExactRollbackObserverAdded(PlayerID player,
            NetworkIdentity identity, bool isSpawner)
        {
            InvokeExactRollbackObserverSignal(onObserverAdded, player, identity);
            TryInvokeExactRollback(() => identity.TriggerOnPreObserverAdded(player, isSpawner));
            TryInvokeExactRollback(() => identity.TriggerOnObserverAdded(player, isSpawner));
            InvokeExactRollbackObserverSignal(onLateObserverAdded, player, identity);
        }

        private void InvokeExactRollbackObserverRemoved(PlayerID player, NetworkIdentity identity)
        {
            TryInvokeExactRollback(() => identity.TriggerOnObserverRemoved(player));
            InvokeExactRollbackObserverSignal(onObserverRemoved, player, identity);
        }

        private static void InvokeExactRollbackObserverSignal(ObserverAction signal,
            PlayerID player, NetworkIdentity identity)
        {
            if (signal == null)
                return;

            var callbacks = signal.GetInvocationList();
            for (var i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((ObserverAction)callbacks[i])(player, identity);
                }
                catch (Exception exception)
                {
                    PurrLogger.LogException(exception);
                }
            }
        }

        private static void TryInvokeExactRollback(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                PurrLogger.LogException(exception);
            }
        }

        private bool MoveObserversToAsyncPending(SpawnID spawnId, PlayerID player,
            List<NetworkIdentity> identities)
        {
            var pendingIdentities = new List<NetworkIdentity>(identities.Count);
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (identity && identity.TryMoveObserverToPending(player))
                    pendingIdentities.Add(identity);
            }

            if (pendingIdentities.Count == 0)
                return false;

            _pendingAsyncObservers[spawnId] = new PendingAsyncObserverSpawn(player, pendingIdentities);
            return true;
        }

        private void RemovePendingAsyncObservers(PlayerID player, List<NetworkIdentity> identities,
            HashSet<NetworkIdentity> unconfirmed = null, List<NetworkIdentity> cancelledRoots = null,
            List<NetworkIdentity> confirmedRemoved = null)
        {
            if (_pendingAsyncObservers.Count == 0 && _readyAsyncObservers.Count == 0)
                return;

            var pendingIds = ListPool<SpawnID>.Instantiate();
            var readyIds = ListPool<SpawnID>.Instantiate();

            foreach (var pair in _pendingAsyncObservers)
            {
                var pending = pair.Value;
                if (pending.player == player && AsyncTransactionIntersects(pending.identities, identities))
                    pendingIds.Add(pair.Key);
            }

            foreach (var pair in _readyAsyncObservers)
            {
                var ready = pair.Value;
                if (ready.player == player && AsyncTransactionIntersects(ready.identities, identities))
                    readyIds.Add(pair.Key);
            }

            for (var keyIndex = 0; keyIndex < pendingIds.Count; keyIndex++)
            {
                if (!_pendingAsyncObservers.Remove(pendingIds[keyIndex], out var pending))
                    continue;

                AddAsyncTransactionRoot(pending, cancelledRoots);

                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (identity)
                    {
                        unconfirmed?.Add(identity);
                        identity.TryRemovePendingObserver(player);
                    }
                }
            }

            for (var keyIndex = 0; keyIndex < readyIds.Count; keyIndex++)
            {
                var key = readyIds[keyIndex];
                if (!_readyAsyncObservers.Remove(key, out var ready))
                    continue;
                _toCompleteNextFrame.Remove(key);
                AddAsyncTransactionRoot(ready, cancelledRoots);

                for (var i = 0; i < ready.identities.Count; i++)
                {
                    var identity = ready.identities[i];
                    if (identity && identity.TryRemoveObserver(player))
                        confirmedRemoved?.Add(identity);
                }
            }

            ListPool<SpawnID>.Destroy(pendingIds);
            ListPool<SpawnID>.Destroy(readyIds);
        }

        private static void AddAsyncTransactionRoot(PendingAsyncObserverSpawn pending,
            List<NetworkIdentity> roots)
        {
            if (roots == null || pending.identities.Count == 0)
                return;

            var root = pending.identities[0];
            if (root && !roots.Contains(root))
                roots.Add(root);
        }

        private void ConsumeFailedAsyncObserverRoots(PlayerID player, List<NetworkIdentity> candidates,
            List<NetworkIdentity> failedRoots, HashSet<NetworkIdentity> unconfirmed = null)
        {
            if (_failedAsyncObserverRoots.Count == 0)
                return;

            for (var i = 0; i < candidates.Count; i++)
            {
                var current = candidates[i];
                while (current)
                {
                    if (current.id.HasValue &&
                        _failedAsyncObserverRoots.Remove((player, current.id.Value)) &&
                        !failedRoots.Contains(current))
                        failedRoots.Add(current);
                    current = current.parent;
                }
            }

            var transaction = ListPool<NetworkIdentity>.Instantiate();
            for (var i = 0; i < failedRoots.Count; i++)
            {
                var root = failedRoots[i];
                if (!root)
                    continue;
                transaction.Clear();
                GetComponentsInChildren(root.gameObject, transaction);
                for (var j = 0; j < transaction.Count; j++)
                {
                    var member = transaction[j];
                    if (!member)
                        continue;
                    member.TryRemovePendingObserver(player);
                    unconfirmed?.Add(member);
                }
            }
            ListPool<NetworkIdentity>.Destroy(transaction);
        }

        private static bool AsyncTransactionIntersects(List<NetworkIdentity> transaction,
            List<NetworkIdentity> identities)
        {
            for (var i = 0; i < identities.Count; i++)
            {
                if (transaction.Contains(identities[i]))
                    return true;
            }
            return false;
        }

        private void OnAsyncSpawnReadyPacket(PlayerID player, AsyncSpawnReadyPacket data, bool asServer)
        {
            if (!_asServer || data.sceneId != _sceneId || player != data.packetIdx.target)
                return;

            if (!_pendingAsyncObservers.TryGetValue(data.packetIdx, out var pending) ||
                pending.player != player || !pending.sent)
                return;

            _pendingAsyncObservers.Remove(data.packetIdx);

            if (!data.success)
            {
                var root = MarkAsyncObserverSpawnFailed(pending);
                // Clear the receiver's failure tombstone and any dependent staged spawns. The
                // identities remain pending until visibility turns false, suppressing retries.
                if (root)
                    SendDespawnPacket(player, root, false);
                return;
            }

            // Make the ready transaction visible before invoking user callbacks. A callback may
            // remove visibility or despawn the root; that cancellation must suppress FinishSpawn.
            _readyAsyncObservers[data.packetIdx] = pending;

            _asyncObserverPromotionDepth++;
            try
            {
                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (!identity || !identity.isSpawned || !identity.TryPromotePendingObserver(player))
                        continue;

                    onObserverAdded?.Invoke(player, identity);
                    identity.TriggerOnPreObserverAdded(player, false);
                    _triggerLateObserverAdded.Add(
                        CreateLateObserverEntry(player, identity, false));
                }

                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (!identity || !identity.id.HasValue ||
                        identity.gameObject.GetComponent<NetworkIdentity>() != identity)
                        continue;

                    _playersManager.Send(player, new ChangeParentPacket
                    {
                        sceneId = _sceneId,
                        childId = identity.id.Value,
                        newParentId = identity.parent ? identity.parent.id : null,
                        path = identity.invertedPathToNearestParent,
                        worldPositionStays = false
                    });
                }

                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (identity && identity.id.HasValue && identity.IsObserver(player))
                        onSentSpawnPacket?.Invoke(player, _sceneId, identity.id.Value);
                }

                // Finish must be sent only after observer-state packets produced above have flushed,
                // and only if no callback cancelled the transaction while it was being promoted.
                if (_readyAsyncObservers.ContainsKey(data.packetIdx))
                    _toCompleteNextFrame.Add(data.packetIdx);
            }
            finally
            {
                _asyncObserverPromotionDepth--;
            }
        }

        private void ExpireTimedOutAsyncObservers()
        {
            if (!_asServer || _pendingAsyncObservers.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            var expired = ListPool<SpawnID>.Instantiate();
            foreach (var pair in _pendingAsyncObservers)
            {
                var pending = pair.Value;
                if (!pending.sent || now - pending.createdAt < AsyncSpawnReadyTimeoutSeconds)
                    continue;

                expired.Add(pair.Key);
                var root = MarkAsyncObserverSpawnFailed(pending);

                PurrLogger.LogError(
                    $"InstantiateAsync spawn {pair.Key} did not become ready on player {pending.player} within " +
                    $"{AsyncSpawnReadyTimeoutSeconds:0} seconds. The remote operation was cancelled.", root);

                if (root)
                    SendDespawnPacket(pending.player, root, false);
            }

            for (var i = 0; i < expired.Count; i++)
                _pendingAsyncObservers.Remove(expired[i]);
            ListPool<SpawnID>.Destroy(expired);
        }

        private NetworkIdentity MarkAsyncObserverSpawnFailed(PendingAsyncObserverSpawn pending)
        {
            var root = pending.identities.Count > 0 ? pending.identities[0] : null;

            if (root && root.id.HasValue)
                _failedAsyncObserverRoots.Add((pending.player, root.id.Value));
            return root;
        }

        private void OnVisibilityChanged(PlayerID player, Transform scope, bool isVisible)
        {
            if (isVisible)
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                if (HierarchyPool.TryGetPrototype(scope, player, children, out var prototype))
                {
                    if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                    {
                        bool sendAsync = _asyncVisibilityDepth > 0 &&
                                         player != _manager.localPlayer &&
                                         !player.isBot && !player.isServer;
                        var spawnId = SendSpawnPacket(player, prototype, children, true, sendAsync);

                        if (sendAsync && _pendingAsyncObservers.ContainsKey(spawnId))
                            return;
                    }

                    for (var i = 0; i < children.Count; i++)
                    {
                        var nid = children[i];
                        RecordExactSnapshotObserverLifecycle(
                            player, nid, ExactObserverLifecycle.Added);
                        onObserverAdded?.Invoke(player, nid);
                        nid.TriggerOnPreObserverAdded(player, false);
                        _triggerLateObserverAdded.Add(
                            CreateLateObserverEntry(player, nid, false));
                    }
                }
                else PurrLogger.LogError($"Failed to get prototype for '{scope.name}'.", scope);
                return;
            }

            if (scope.TryGetComponent<NetworkIdentity>(out var identity))
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                GetComponentsInChildren(identity.gameObject, children);

                if (!HasActiveAsyncObserverState)
                {
                    for (var i = 0; i < children.Count; i++)
                    {
                        var child = children[i];
                        ClearPendingLateObserverAdded(player, child);
                        RecordExactSnapshotObserverLifecycle(
                            player, child, ExactObserverLifecycle.Removed);
                        child.TriggerOnObserverRemoved(player);
                        onObserverRemoved?.Invoke(player, child);
                    }

                    ListPool<NetworkIdentity>.Destroy(children);

                    if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                    {
                        _manager.FlushBatchedRPCs();
                        SendDespawnPacket(player, identity, true);
                    }
                    return;
                }

                var unconfirmed = HashSetPool<NetworkIdentity>.Instantiate();
                var cancelledRoots = ListPool<NetworkIdentity>.Instantiate();
                var confirmedRemoved = ListPool<NetworkIdentity>.Instantiate();
                var failedRoots = ListPool<NetworkIdentity>.Instantiate();
                RemovePendingAsyncObservers(player, children, unconfirmed, cancelledRoots, confirmedRemoved);
                ConsumeFailedAsyncObserverRoots(player, children, failedRoots, unconfirmed);

                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];

                    if (unconfirmed.Contains(child))
                        continue;

                    ClearPendingLateObserverAdded(player, child);
                    RecordExactSnapshotObserverLifecycle(
                        player, child, ExactObserverLifecycle.Removed);
                    child.TriggerOnObserverRemoved(player);
                    onObserverRemoved?.Invoke(player, child);
                }

                for (var i = 0; i < confirmedRemoved.Count; i++)
                {
                    var removed = confirmedRemoved[i];
                    if (!removed || children.Contains(removed))
                        continue;
                    ClearPendingLateObserverAdded(player, removed);
                    RecordExactSnapshotObserverLifecycle(
                        player, removed, ExactObserverLifecycle.Removed);
                    removed.TriggerOnObserverRemoved(player);
                    onObserverRemoved?.Invoke(player, removed);
                }

                HashSetPool<NetworkIdentity>.Destroy(unconfirmed);
                ListPool<NetworkIdentity>.Destroy(children);
                ListPool<NetworkIdentity>.Destroy(confirmedRemoved);

                if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                {
                    _manager.FlushBatchedRPCs();
                    bool identityCovered = false;
                    for (var i = 0; i < cancelledRoots.Count; i++)
                    {
                        var cancelledRoot = cancelledRoots[i];
                        if (!cancelledRoot)
                            continue;
                        identityCovered |= identity.transform.IsChildOf(cancelledRoot.transform);
                        SendDespawnPacket(player, cancelledRoot, true);
                    }

                    for (var i = 0; i < failedRoots.Count; i++)
                    {
                        var failedRoot = failedRoots[i];
                        if (failedRoot)
                            identityCovered |= identity.transform.IsChildOf(failedRoot.transform);
                    }

                    if (!identityCovered)
                        SendDespawnPacket(player, identity, true);
                }
                ListPool<NetworkIdentity>.Destroy(cancelledRoots);
                ListPool<NetworkIdentity>.Destroy(failedRoots);
            }
        }

        private void SendDespawnPacket(PlayerID player, NetworkIdentity identity, bool batched)
        {
            var identityId = identity.GetNetworkID(_asServer) ?? identity.id;
            if (!identityId.HasValue)
                return;

            SendDespawnPacket(player, identityId.Value, batched);
        }

        private void SendDespawnPacket(PlayerID player, NetworkID identityId, bool batched)
        {

            // dont send despawn packet to the local player
            if (player == _manager.localPlayer)
                return;

            var packet = new DespawnPacket
            {
                sceneId = _sceneId,
                parentId = identityId
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

        private SpawnID SendSpawnPacket(PlayerID player, GameObjectPrototype prototype,
            List<NetworkIdentity> spawned, bool batched, bool isAsync = false)
        {
            var spawnId = new SpawnID(_nextPacketIdx++, player, _playersManager.localPlayerId);
            if (_asServer && isAsync)
                isAsync = MoveObserversToAsyncPending(spawnId, player, spawned);
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

            bool bypassPool = _hasConfiguredPoolBypass && ShouldBypassConfiguredPool(spawned);

            var packet = new SpawnPacket
            {
                sceneId = _sceneId,
                packetIdx = spawnId,
                bypassPool = bypassPool,
                isAsync = isAsync,
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
                if (!(_asServer && isAsync))
                    _toCompleteNextFrame.Add(spawnId);
            }

            return spawnId;
        }

        private bool ShouldBypassConfiguredPool(List<NetworkIdentity> spawned)
        {
            for (var i = 0; i < spawned.Count; i++)
            {
                var identity = spawned[i];
                if (!identity || identity.shouldBePooled)
                    continue;

                if (_manager.prefabProvider.TryGetPrefabData(identity.prefabId, out var prefabData) &&
                    prefabData.pooled)
                    return true;
            }
            return false;
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

            if (!ShouldAutoSpawn(obj, false))
                return;

            InternalSpawn(obj);
        }

        private bool ShouldAutoSpawn(GameObject obj, bool isAsync)
        {
            if (!obj.TryGetComponent<NetworkIdentity>(out var identity))
                return true;

            return identity.ShouldAutoSpawnOnInstantiate(_manager, isAsync);
        }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        private void OnAsyncInstantiateCompleted(UnityEngine.Object original, UnityEngine.Object instance)
        {
            var obj = GetAsyncGameObject(instance);
            var prefab = GetAsyncGameObject(original);

            if (!obj || !prefab)
                return;

            if (!_asServer && _manager.isServer)
                return;

            if (obj.scene.handle != _scene.handle)
                return;

            if (!_manager.prefabProvider.TryGetPrefabData(prefab, out var data))
                return;

            // Async-origin instances are never allowed into PurrNet's pool, even when the
            // registered prefab is normally poolable.
            _hasConfiguredPoolBypass = true;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            try
            {
                obj.GetComponentsInChildren(true, identities);
                NetworkManager.SetupPrefabInfo(obj, data.prefabId, false, identities);

                if (!ShouldAutoSpawn(obj, true))
                    return;

                if (!HasMatchingAsyncNetworkShape(data.prefab, obj, identities, out var mismatch))
                {
                    ReportAsyncShapeMismatch(data.prefab, obj, mismatch);
                    return;
                }
            }
            finally
            {
                ListPool<NetworkIdentity>.Destroy(identities);
            }

            InternalSpawn(obj, true);
        }

        private static GameObject GetAsyncGameObject(UnityEngine.Object obj)
        {
            return obj switch
            {
                Component component => component.gameObject,
                GameObject gameObject => gameObject,
                _ => null
            };
        }
#endif

        internal void InternalSpawn(GameObject gameObject, bool instantiateRemotelyAsync = false)
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
            ApplyParentChange(id, id.parent, id.invertedPathToNearestParent, false, applyToTransform: false);

            if (!_asServer)
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                var prototype = HierarchyPool.GetFullPrototype(gameObject.transform, children);
                SendSpawnPacket(default, prototype, children, false, instantiateRemotelyAsync);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                if (instantiateRemotelyAsync)
                    ++_asyncVisibilityDepth;

                try
                {
                    for (var i = 0; i < players.Count; i++)
                    {
                        var player = players[i];
                        _visibility.RefreshVisibilityForGameObject(player, gameObject.transform);
                    }
                }
                finally
                {
                    if (instantiateRemotelyAsync)
                        --_asyncVisibilityDepth;
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

            NetworkID? localDespawnId = null;
            if (!_asServer && !bypassBroadcast)
            {
                localDespawnId = children[0].GetNetworkID(false) ?? children[0].id;
                if (localDespawnId.HasValue)
                    TrackPendingLocalDespawnEcho(localDespawnId.Value);
            }

            bool isHost = IsServerHost();

            // Try to despawn the object properly if despawn was on the same tick (by first calling OnSpawned)
            for (var i = 0; i < c; i++)
                CompletePendingSpawnsFor(children[i], isHost);

            if (_asServer)
            {
                _visibility.ClearVisibilityForGameObject(gameObject.transform);
                
                for (var i = 0; i < c; i++)
                {
                    var child = children[i];
                    
                    TriggerDespawnEvent(child, child.shouldBePooled);
                }

                _manager.FlushBatchedRPCs();
                FlushSpawnPackets();
            }
            else if (!bypassBroadcast)
            {
                for (var i = 0; i < c; i++)
                {
                    var child = children[i];
                    
                    TriggerDespawnEvent(child, child.shouldBePooled);
                }

                _manager.FlushBatchedRPCs();
                if (localDespawnId.HasValue)
                    SendDespawnPacket(default, localDespawnId.Value, false);
            }
            else
            {
                for (var i = 0; i < c; i++)
                {
                    var child = children[i];
                    
                    TriggerDespawnEvent(child, child.shouldBePooled);
                }
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

        private void TrackPendingLocalDespawnEcho(NetworkID identityId)
        {
            if (_pendingLocalDespawnEchoes.isDisposed)
                _pendingLocalDespawnEchoes = DisposableList<NetworkID>.Create(1);
            if (!_pendingLocalDespawnEchoes.Contains(identityId))
                _pendingLocalDespawnEchoes.Add(identityId);
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

        private void FlushSpawnPackets(PlayerID? exactBarrierBypassPlayer = null,
            HostMigrationTransitionOptions exactTransition = default)
        {
            if (_spawnPackets.Count == 0)
                return;

            using var entries = DisposableList<KeyValuePair<PlayerID, SpawnPacketBatch>>
                .Create(_spawnPackets.Count);
            foreach (var pair in _spawnPackets)
                entries.Add(pair);
            _spawnPackets.Clear();

            var processed = 0;
            try
            {
                for (; processed < entries.Count; processed++)
                {
                    var (player, batch) = entries[processed];
                    using (batch)
                    {
                        int count = batch.spawnPackets.Count;
                        if (player.isServer)
                        {
                            _playersManager.SendToServer(batch, Channel.ReliableOrdered);
                        }
                        else
                        {
                            var exactSnapshotBatch = exactBarrierBypassPlayer.HasValue &&
                                                     exactBarrierBypassPlayer.Value == player &&
                                                     exactTransition.canReconcile;
                            if (exactSnapshotBatch)
                            {
                                if (!_playersManager.SendExactBarrierBypass(
                                        player, exactTransition, batch, Channel.ReliableOrdered))
                                {
                                    throw new InvalidOperationException(
                                        $"Scene {_sceneId} lost player {player}'s exact outbound " +
                                        "barrier before its declared spawn batch was sent.");
                                }
                            }
                            else
                                _playersManager.Send(player, batch, Channel.ReliableOrdered);

                            for (var i = 0; i < count; i++)
                            {
                                var packet = batch.spawnPackets[i];

                                if (packet.isAsync &&
                                    _pendingAsyncObservers.TryGetValue(packet.packetIdx, out var pendingAsync))
                                    pendingAsync.sent = true;

                                if (_asServer && packet.isAsync)
                                    continue;

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
                        {
                            var packet = batch.spawnPackets[i];
                            if (exactBarrierBypassPlayer.HasValue &&
                                exactBarrierBypassPlayer.Value == player &&
                                exactTransition.canReconcile)
                            {
                                _exactBarrierBypassFinishes[packet.packetIdx] = exactTransition;
                            }

                            if (!(_asServer && packet.isAsync))
                            {
                                _toCompleteNextFrame.Add(packet.packetIdx);
                            }
                        }
                    }
                }
            }
            finally
            {
                for (var i = processed + 1; i < entries.Count; i++)
                    entries[i].Value.Dispose();
            }
        }

        public void PreNetworkMessages()
        {
            _manager.FlushBatchedRPCs();
        }

        public void PostNetworkMessages()
        {
            ExpireTimedOutAsyncObservers();
            FlushSpawnPackets();
            SendDelayedObserverEvents();
            TriggerSpawnSentEvents();
            _manager.FlushBatchedRPCs();
            onPreFinishSpawn?.Invoke(_sceneId);
            SendDelayedSceneReconcileEnds();
            SendDelayedCompleteSpawns();
            SpawnDelayedIdentities();
        }

        private void TriggerSpawnSentEvents()
        {
            if (_toSpawnNextFrame.Count == 0)
                return;

            var snapshot = ListPool<NetworkIdentity>.Instantiate();
            snapshot.AddRange(_toSpawnNextFrame);

            for (var i = 0; i < snapshot.Count; i++)
            {
                var nid = snapshot[i];
                if (!nid || !nid.isSpawned)
                    continue;
                nid.TriggerOnSpawnSent();
            }

            ListPool<NetworkIdentity>.Destroy(snapshot);
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

                var exactSnapshotObserver = nid.exactTransition.canReconcile;
                if (exactSnapshotObserver)
                {
                    _manager.FlushBatchedRPCs();
                }

                if (!exactSnapshotObserver)
                {
                    nid.nid.TriggerOnObserverAdded(nid.player, nid.isSpawner);
                    onLateObserverAdded?.Invoke(nid.player, nid.nid);
                    continue;
                }

                if (!_playersManager.RunExactOutboundBarrierBypass(
                        nid.player, nid.exactTransition, () =>
                        {
                            nid.nid.TriggerOnObserverAdded(nid.player, nid.isSpawner);
                            onLateObserverAdded?.Invoke(nid.player, nid.nid);
                            _manager.FlushBatchedRPCs();
                        }))
                {
                    RejectExactSpawnSnapshot(nid.player, nid.exactTransition,
                        $"Scene {_sceneId} lost its exact outbound barrier while flushing " +
                        "observer callback RPC baselines.");
                }
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
                {
                    if (_exactBarrierBypassFinishes.Count == 0 ||
                        !_exactBarrierBypassFinishes.Remove(
                            toComplete, out var exactTransition) ||
                        !_playersManager.SendExactBarrierBypass(
                            toComplete.target, exactTransition, packet,
                            Channel.ReliableOrdered))
                    {
                        _playersManager.Send(toComplete.target, packet);
                    }
                }
                else _playersManager.SendToServer(packet);

                if (_asServer && _readyAsyncObservers.Count > 0)
                    _readyAsyncObservers.Remove(toComplete);
            }

            _toCompleteNextFrame.Clear();
        }

        private void SendDelayedSceneReconcileEnds()
        {
            if (_sceneReconcileEndsNextFrame.Count == 0)
                return;

            for (var i = 0; i < _sceneReconcileEndsNextFrame.Count; i++)
            {
                var pending = _sceneReconcileEndsNextFrame[i];
                if (pending.promotedListenClient != null)
                {
                    pending.promotedListenClient.OnSceneSpawnReconcilePacket(
                        pending.player, pending.packet, false);
                }
                else
                {
                    var transition = new HostMigrationTransitionOptions(
                        pending.packet.sessionId, pending.packet.epoch);
                    if (!transition.canReconcile ||
                        !_playersManager.SendExactBarrierBypass(
                            pending.player, transition, pending.packet,
                            Channel.ReliableOrdered))
                    {
                        _playersManager.Send(
                            pending.player, pending.packet, Channel.ReliableOrdered);
                    }
                }
            }

            _sceneReconcileEndsNextFrame.Clear();
        }

        private void CatchupClient(PlayerID playerId)
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];

                if (!identity.isSpawned)
                    continue;

                if (!identity.id.HasValue)
                    continue;

                if (_toSpawnNextFrame.Contains(identity))
                    continue;

                var needsClientLifecycle = !identity.IsSpawned(false);
                if (needsClientLifecycle)
                {
                    identity.SetIsSpawned(true, false);
                    identity.TriggerEarlySpawnEvent(false);
                }

                onSentSpawnPacket?.Invoke(playerId, _sceneId, identity.id.Value);

                if (identity.TryAddObserver(playerId))
                {
                    RecordExactSnapshotObserverLifecycle(
                        playerId, identity, ExactObserverLifecycle.Added);
                    onObserverAdded?.Invoke(playerId, identity);
                    identity.TriggerOnPreObserverAdded(playerId, false);
                    _triggerLateObserverAdded.Add(
                        CreateLateObserverEntry(playerId, identity, false));
                }

                if (needsClientLifecycle)
                {
                    identity.TriggerSpawnEvent(false);
                    onIdentityAdded?.Invoke(identity);
                }
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

            return FinalizePrototypeInstance(result, prototype, shouldActivate);
        }

        private static bool TryApplyPrototypeToExisting(GameObject result, GameObjectPrototype prototype,
            List<NetworkIdentity> createdNids, out bool shouldActivate)
        {
            shouldActivate = false;
            if (!result || prototype.framework.Count == 0 ||
                !result.TryGetComponent<NetworkIdentity>(out var root))
                return false;

            var actual = HierarchyPool.GetFullPrototype(result.transform, null, true);
            try
            {
                if (!HaveMatchingNetworkFramework(prototype, actual))
                    return false;
            }
            finally
            {
                actual.Dispose();
            }

            var queue = new Queue<NetworkIdentity>();
            queue.Enqueue(root);

            for (var i = 0; i < prototype.framework.Count; i++)
            {
                if (queue.Count == 0)
                    return false;

                var pieceRoot = queue.Dequeue();
                if (!pieceRoot)
                    return false;

                var current = prototype.framework[i];
                var siblings = ListPool<NetworkIdentity>.Instantiate();
                pieceRoot.gameObject.GetComponents(siblings);

                if (siblings.Count == 0)
                {
                    ListPool<NetworkIdentity>.Destroy(siblings);
                    return false;
                }

                for (var siblingIndex = 0; siblingIndex < siblings.Count; siblingIndex++)
                {
                    var sibling = siblings[siblingIndex];
                    sibling.SetID(new NetworkID(current.id, (ulong)siblingIndex));
                    sibling.parent = i == 0 ? null : sibling.GetNearestParent();
                    sibling.invertedPathToNearestParent = current.inversedRelativePath;
                }
                ListPool<NetworkIdentity>.Destroy(siblings);

                var directChildren = pieceRoot.directChildren;
                for (var childIndex = 0; childIndex < directChildren.Count; childIndex++)
                    queue.Enqueue(directChildren[childIndex]);

                current.localTransform.Apply(pieceRoot.transform);
                if (i != 0 && pieceRoot.gameObject.activeSelf != current.isActive)
                    pieceRoot.gameObject.SetActive(current.isActive);
            }

            if (queue.Count != 0)
                return false;

            shouldActivate = prototype.framework[0].isActive;
            if (!shouldActivate && result.activeSelf)
                result.SetActive(false);

            if (createdNids != null)
            {
                var ordered = HierarchyPool.GetFullPrototype(result.transform, createdNids, true);
                ordered.Dispose();
            }
            return true;
        }

        private static bool HaveMatchingNetworkFramework(GameObjectPrototype expected, GameObjectPrototype actual)
        {
            if (expected.framework.Count != actual.framework.Count)
                return false;

            for (var i = 0; i < expected.framework.Count; i++)
            {
                var a = expected.framework[i];
                var b = actual.framework[i];
                if (!a.pid.Equals(b.pid) || a.childCount != b.childCount ||
                    !HaveMatchingPath(a.inversedRelativePath, b.inversedRelativePath))
                    return false;
            }
            return true;
        }

        private static bool HaveMatchingPath(int[] a, int[] b)
        {
            int aLength = a?.Length ?? 0;
            int bLength = b?.Length ?? 0;
            if (aLength != bLength)
                return false;

            for (var i = 0; i < aLength; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        private readonly struct AsyncNetworkShapeEntry
        {
            public readonly Type type;
            public readonly int componentIndex;
            public readonly int[] transformPath;

            public AsyncNetworkShapeEntry(Type type, int componentIndex, int[] transformPath)
            {
                this.type = type;
                this.componentIndex = componentIndex;
                this.transformPath = transformPath;
            }
        }

        private static readonly HashSet<int> _reportedAsyncShapeMismatches = new();
        private static readonly Dictionary<GameObject, List<AsyncNetworkShapeEntry>> _cachedPrefabAsyncShapes = new();

        private static readonly List<AsyncNetworkShapeEntry> _emptyAsyncShape = new();

        private static List<AsyncNetworkShapeEntry> GetPrefabAsyncNetworkShape(GameObject prefab)
        {
            if (!prefab)
                return _emptyAsyncShape;

            if (_cachedPrefabAsyncShapes.TryGetValue(prefab, out var shape))
                return shape;

            shape = new List<AsyncNetworkShapeEntry>();
            CaptureAsyncNetworkShape(prefab, shape);
            _cachedPrefabAsyncShapes[prefab] = shape;
            return shape;
        }

        private static bool HasMatchingAsyncNetworkShape(GameObject prefab, GameObject instance,
            List<NetworkIdentity> instanceIdentities, out string mismatch)
        {
            var expected = GetPrefabAsyncNetworkShape(prefab);
            var actual = new List<AsyncNetworkShapeEntry>();
            CaptureAsyncNetworkShape(instance, instanceIdentities, actual);

            if (expected.Count != actual.Count)
            {
                mismatch = $"expected {expected.Count} NetworkIdentity components, but the result has {actual.Count}";
                return false;
            }

            for (var i = 0; i < expected.Count; i++)
            {
                var a = expected[i];
                var b = actual[i];
                if (a.type != b.type || a.componentIndex != b.componentIndex ||
                    !HaveMatchingPath(a.transformPath, b.transformPath))
                {
                    mismatch = $"NetworkIdentity component {i} changed type, component order, or transform path";
                    return false;
                }
            }

            mismatch = null;
            return true;
        }

        private static void CaptureAsyncNetworkShape(GameObject root, List<AsyncNetworkShapeEntry> result)
        {
            if (!root)
                return;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            root.GetComponentsInChildren(true, identities);
            CaptureAsyncNetworkShape(root, identities, result);
            ListPool<NetworkIdentity>.Destroy(identities);
        }

        private static void CaptureAsyncNetworkShape(GameObject root, List<NetworkIdentity> identities,
            List<AsyncNetworkShapeEntry> result)
        {
            if (!root)
                return;

            Transform runTransform = null;
            int runStart = 0;

            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (!identity)
                    continue;

                var trs = identity.transform;
                if (!ReferenceEquals(trs, runTransform))
                {
                    runTransform = trs;
                    runStart = i;
                }

                int componentIndex = i - runStart;

                var inversePath = ListPool<int>.Instantiate();
                var current = trs;
                while (current && current != root.transform)
                {
                    inversePath.Add(current.GetSiblingIndex());
                    current = current.parent;
                }

                var path = new int[inversePath.Count];
                for (var pathIndex = 0; pathIndex < inversePath.Count; pathIndex++)
                    path[pathIndex] = inversePath[inversePath.Count - pathIndex - 1];
                ListPool<int>.Destroy(inversePath);

                result.Add(new AsyncNetworkShapeEntry(identity.GetType(), componentIndex, path));
            }
        }

        private static void ReportAsyncShapeMismatch(GameObject prefab, GameObject instance, string mismatch)
        {
            if (!prefab || !_reportedAsyncShapeMismatches.Add(prefab.GetHashCode()))
                return;

            PurrLogger.LogError(
                $"`InstantiateAsync` could not network-spawn prefab `{prefab.name}` because its NetworkIdentity hierarchy changed during asynchronous instantiation ({mismatch}). " +
                "Do not add, remove, destroy, or reparent NetworkIdentity objects in Awake. Perform network hierarchy changes after spawning, or use regular Instantiate.",
                instance);
        }

        private GameObject CreateUnpooledPrototype(GameObjectPrototype prototype, List<NetworkIdentity> createdNids)
        {
            if (prototype.framework.Count == 0)
                return null;

            var poolRoot = new GameObject("[PurrNet] Unpooled Prototype Pieces")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            poolRoot.SetActive(false);
            var temporaryPool = new HierarchyPool(poolRoot.transform, _manager.prefabProvider, true);

            try
            {
                var pair = new PoolPair(_scenePool, temporaryPool);
                if (!HierarchyPool.TryBuildPrototype(pair, prototype, createdNids, out var result,
                        out var shouldActivate))
                    return null;

                if (createdNids != null)
                {
                    for (var i = 0; i < createdNids.Count; i++)
                    {
                        var identity = createdNids[i];
                        if (!identity || identity.prefabId < 0)
                            continue;
                        identity.PreparePrefabInfo(identity.prefabId, identity.componentIndex, false, false);
                    }
                }

                result = FinalizePrototypeInstance(result, prototype, shouldActivate);
                var actual = HierarchyPool.GetFullPrototype(result.transform, null, true);
                bool shapeMatches = HaveMatchingNetworkFramework(prototype, actual);
                actual.Dispose();
                if (!shapeMatches)
                {
                    int prefabId = prototype.framework[0].pid.prefabId;
                    if (_manager.prefabProvider.TryGetPrefabData(prefabId, out var prefabData))
                        ReportAsyncShapeMismatch(prefabData.prefab, result,
                            "the NetworkIdentity framework changed when the instance was activated");
                    else
                        PurrLogger.LogError(
                            "`InstantiateAsync` could not apply a spawn packet because its NetworkIdentity framework changed when the instance was activated.",
                            result);
                    createdNids?.Clear();
                    UnityProxy.DestroyDirectly(result);
                    return null;
                }

                return result;
            }
            finally
            {
                temporaryPool.Dispose();
            }
        }

        private GameObject FinalizePrototypeInstance(GameObject result, GameObjectPrototype prototype,
            bool shouldActivate)
        {

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
            ManualEarlySpawn(identity, id, default);
        }

        /// <summary>
        /// For manual spawning of identities with custom spawn data.
        /// Custom data is deserialized after identity setup and before early-spawn callbacks.
        /// After this call, you should call <see cref="ManualFinalizeSpawn(NetworkIdentity)"/> to finalize the spawning.
        /// This needs to be called manually on all conserned clients.
        /// </summary>
        public void ManualEarlySpawn(NetworkIdentity identity, NetworkID id, BitData customData)
        {
            _spawnedIdentities.Add(identity);
            _spawnedIdentitiesMap.Add(id, identity);

            bool isHost = IsServerHost();

            identity.isManualSpawn = true;
            identity.SetID(id);
            identity.SetIdentity(_manager, this, _sceneId, _asServer, isHost);

            if (customData.bitLength > 0 && customData.packer != null)
            {
                using var scope = customData.AutoScope();
                identity.TriggerOnDeserialize(customData.packer);
            }

            identity.TriggerEarlySpawnEvent(_asServer);
            if (isHost) identity.TriggerEarlySpawnEvent(false);

            onEarlyIdentityAdded?.Invoke(identity);
        }

        /// <summary>
        /// For manual despawning of identities.
        /// </summary>
        public void ManualDespawn(NetworkIdentity identity)
        {
            if (!identity || !identity.IsSpawned(_asServer))
                return;

            if (_asServer)
            {
                var observersCopy = ListPool<PlayerID>.Instantiate();
                observersCopy.AddRange(identity.observers);
                for (var i = 0; i < observersCopy.Count; i++)
                    ManualRemoveObserver(identity, observersCopy[i]);
                ListPool<PlayerID>.Destroy(observersCopy);
            }

            TriggerDespawnEvent(identity);
            UnregisterIdentity(identity);

            identity.SetIsSpawned(false, _asServer);
        }

        /// <summary>
        /// For manual finalization of spawning an identity.
        /// This needs to be called manually on all conserned clients.
        /// </summary>
        public void ManualFinalizeSpawn(NetworkIdentity identity)
        {
            bool isHost = IsServerHost();

            identity.TriggerOnSpawnReceived();

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

            if (!HasActiveAsyncObserverState)
            {
                if (identity.TryRemoveObserver(player))
                {
                    ClearPendingLateObserverAdded(player, identity);
                    identity.TriggerOnObserverRemoved(player);
                    onObserverRemoved?.Invoke(player, identity);
                }
                return;
            }

            var identities = ListPool<NetworkIdentity>.Instantiate();
            var cancelledRoots = ListPool<NetworkIdentity>.Instantiate();
            var confirmedRemoved = ListPool<NetworkIdentity>.Instantiate();
            var failedRoots = ListPool<NetworkIdentity>.Instantiate();
            identity.gameObject.GetComponents(identities);
            ConsumeFailedAsyncObserverRoots(player, identities, failedRoots);
            RemovePendingAsyncObservers(player, identities, null, cancelledRoots, confirmedRemoved);

            bool identityHandled = false;
            for (var i = 0; i < cancelledRoots.Count; i++)
            {
                var root = cancelledRoots[i];
                if (!root)
                    continue;
                identityHandled |= identity.transform.IsChildOf(root.transform);
                SendDespawnPacket(player, root, false);
            }
            for (var i = 0; i < failedRoots.Count; i++)
            {
                var root = failedRoots[i];
                if (root)
                    identityHandled |= identity.transform.IsChildOf(root.transform);
            }

            ListPool<NetworkIdentity>.Destroy(identities);
            ListPool<NetworkIdentity>.Destroy(cancelledRoots);
            ListPool<NetworkIdentity>.Destroy(failedRoots);

            for (var i = 0; i < confirmedRemoved.Count; i++)
            {
                var removed = confirmedRemoved[i];
                if (!removed)
                    continue;
                identityHandled |= removed == identity;
                ClearPendingLateObserverAdded(player, removed);
                removed.TriggerOnObserverRemoved(player);
                onObserverRemoved?.Invoke(player, removed);
            }
            ListPool<NetworkIdentity>.Destroy(confirmedRemoved);

            if (identityHandled)
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
        private void RegisterIdentity(NetworkIdentity identity, bool isLocalSpawn, bool triggerEarlySpawn = true)
        {
            if (identity && identity.id.HasValue)
            {
                _spawnedIdentities.Add(identity);
                _spawnedIdentitiesMap.Add(identity.id.Value, identity);

                if (triggerEarlySpawn)
                    TriggerEarlySpawnForRegisteredIdentity(identity);

                if (isLocalSpawn)
                    _toSpawnNextFrame.Add(identity);
            }
        }

        private void TriggerEarlySpawnForRegisteredIdentity(NetworkIdentity identity)
        {
            if (!identity || !identity.id.HasValue)
                return;

            identity.TriggerEarlySpawnEvent(_asServer);
            if (_asServer && _manager.isClient)
                identity.TriggerEarlySpawnEvent(false);

            onEarlyIdentityAdded?.Invoke(identity);
        }

        private void TriggerDespawnEvent(NetworkIdentity identity, bool preserveModules = false)
        {
            if (_asServer && IsServerHost())
                identity.TriggerDespawnEvent(false, preserveModules);
            identity.TriggerDespawnEvent(_asServer, preserveModules);
        }

        private void UnregisterIdentity(NetworkIdentity identity)
        {
            if (identity.id.HasValue)
            {
                RemoveFailedAsyncObserverRoots(identity.id.Value);
                _spawnedIdentities.Remove(identity);
                _spawnedIdentitiesMap.Remove(identity.id.Value);
                onIdentityRemoved?.Invoke(identity);
            }
        }

        private void RemoveFailedAsyncObserverRoots(NetworkID root)
        {
            if (_failedAsyncObserverRoots.Count == 0)
                return;

            _failedAsyncObserverRoots.RemoveWhere(pair => pair.root == root);
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

            if (!HasActiveAsyncObserverState)
            {
                if (_enabled && !_isDisposed && _asServer && _playersManager != null &&
                    identity.observers.Count > 0)
                {
                    using var syncTargets = DisposableList<PlayerID>.Create(identity.observers);
                    if (_playersManager.localPlayerId.HasValue)
                        syncTargets.Remove(_playersManager.localPlayerId.Value);
                    if (syncTargets.Count > 0)
                        _playersManager.Send(syncTargets,
                            new DespawnPacket { sceneId = _sceneId, parentId = nid.Value });
                }

                _spawnedIdentities.Remove(identity);
                _spawnedIdentitiesMap.Remove(nid.Value);
                onIdentityRemoved?.Invoke(identity);
                return;
            }

            var destroyed = ListPool<NetworkIdentity>.Instantiate();
            GetComponentsInChildren(identity.gameObject, destroyed);

            using var targets = DisposableList<PlayerID>.Create(identity.observers);
            for (var identityIndex = 0; identityIndex < destroyed.Count; identityIndex++)
            {
                var member = destroyed[identityIndex];
                if (!member)
                    continue;
                for (var i = 0; i < member.observers.Count; i++)
                {
                    var observer = member.observers[i];
                    if (!targets.Contains(observer))
                        targets.Add(observer);
                }
                for (var i = 0; i < member.pendingObservers.Count; i++)
                {
                    var pendingPlayer = member.pendingObservers[i];
                    if (!targets.Contains(pendingPlayer))
                        targets.Add(pendingPlayer);
                }
            }

            for (var i = targets.Count - 1; i >= 0; i--)
            {
                var target = targets[i];
                var cancelledRoots = ListPool<NetworkIdentity>.Instantiate();
                var confirmedRemoved = ListPool<NetworkIdentity>.Instantiate();
                var failedRoots = ListPool<NetworkIdentity>.Instantiate();
                ConsumeFailedAsyncObserverRoots(target, destroyed, failedRoots);
                RemovePendingAsyncObservers(target, destroyed, null, cancelledRoots, confirmedRemoved);

                for (var removedIndex = 0; removedIndex < confirmedRemoved.Count; removedIndex++)
                {
                    var removed = confirmedRemoved[removedIndex];
                    if (!removed || removed == identity)
                        continue;
                    ClearPendingLateObserverAdded(target, removed);
                    removed.TriggerOnObserverRemoved(target);
                    onObserverRemoved?.Invoke(target, removed);
                }

                if (_enabled && !_isDisposed && _asServer && _playersManager != null &&
                    (!_playersManager.localPlayerId.HasValue || target != _playersManager.localPlayerId.Value))
                {
                    bool identityCovered = false;
                    for (var rootIndex = 0; rootIndex < cancelledRoots.Count; rootIndex++)
                    {
                        var root = cancelledRoots[rootIndex];
                        if (!root)
                            continue;
                        identityCovered |= identity.transform.IsChildOf(root.transform);
                        SendDespawnPacket(target, root, false);
                    }
                    for (var rootIndex = 0; rootIndex < failedRoots.Count; rootIndex++)
                    {
                        var root = failedRoots[rootIndex];
                        if (root)
                            identityCovered |= identity.transform.IsChildOf(root.transform);
                    }

                    if (!identityCovered)
                        SendDespawnPacket(target, identity, false);
                }

                ListPool<NetworkIdentity>.Destroy(cancelledRoots);
                ListPool<NetworkIdentity>.Destroy(confirmedRemoved);
                ListPool<NetworkIdentity>.Destroy(failedRoots);
            }
            ListPool<NetworkIdentity>.Destroy(destroyed);
            RemoveFailedAsyncObserverRoots(nid.Value);

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
