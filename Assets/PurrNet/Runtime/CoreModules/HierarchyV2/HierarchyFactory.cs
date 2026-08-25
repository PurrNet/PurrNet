using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    internal enum ExactSceneAcknowledgementKind : byte
    {
        Loaded,
        Rebound
    }

    public class HierarchyFactory : INetworkModule, IFixedUpdate, IPreFixedUpdate, ICleanup, IPromoteToServerModule,
        ITransferToNewServer, IPostTransferToNewServer
    {
        private sealed class ExactOutboundTopologyTransaction
        {
            internal readonly PlayerID player;
            internal readonly HostMigrationTransitionOptions transition;
            internal readonly List<SceneID> orderedScenes;
            internal readonly Dictionary<SceneID, ExactSceneAcknowledgementKind> acknowledgements = new();
            internal bool committing;

            internal ExactOutboundTopologyTransaction(PlayerID player,
                HostMigrationTransitionOptions transition, List<SceneID> orderedScenes)
            {
                this.player = player;
                this.transition = transition;
                this.orderedScenes = orderedScenes;
            }
        }

        readonly ScenesModule _scenes;

        readonly NetworkManager _manager;

        readonly ScenePlayersModule _scenePlayersModule;

        readonly Dictionary<SceneID, HierarchyV2> _hierarchies = new();

        readonly List<HierarchyV2> _rawHierarchies = new();

        readonly PlayersManager _playersManager;

        private HostMigrationTransitionOptions _transferTransition;
        private HostMigrationTransitionOptions _receivedTransferSession;
        private bool _hasReceivedTransferSession;
        private bool _receivedTransferSessionMatched;
        private readonly Dictionary<PlayerID, ExactOutboundTopologyTransaction>
            _exactOutboundTopologyTransactions = new();
        private readonly List<SceneID> _exactInboundOrderedScenes = new();
        private readonly HashSet<SceneID> _exactInboundExpectedScenes = new();
        private readonly HashSet<SceneID> _exactInboundAcceptedScenes = new();
        private HostMigrationTransitionOptions _exactInboundTransition;
        private bool _exactInboundTopologyRegistered;
        private bool _exactInboundTopologyOpen;
        private bool _exactInboundTopologyFailed;
        private string _exactInboundTopologyFailure;

        internal bool isTransferReconciliationComplete
        {
            get
            {
                for (var i = 0; i < _rawHierarchies.Count; i++)
                {
                    if (!_rawHierarchies[i].isTransferReconciliationComplete)
                        return false;
                }

                return true;
            }
        }

        internal bool TryGetTransferReconciliationFailure(out Exception failure)
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                if (_rawHierarchies[i].TryGetTransferReconciliationFailure(out failure))
                    return true;
            }

            failure = null;
            return false;
        }

        internal Task GetPromotionReadinessTask()
        {
            List<Task> readiness = null;
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                var task = _rawHierarchies[i].promotionReadiness;
                if (task.IsCompletedSuccessfully)
                    continue;

                readiness ??= new List<Task>();
                readiness.Add(task);
            }

            return readiness == null ? Task.CompletedTask : Task.WhenAll(readiness);
        }

        public HierarchyFactory(NetworkManager manager, ScenesModule scenes, ScenePlayersModule scenePlayersModule,
            PlayersManager playersManager)
        {
            _manager = manager;
            _scenes = scenes;
            _scenePlayersModule = scenePlayersModule;
            _playersManager = playersManager;
        }

        readonly List<ValidateSpawnAction> _clientSpawnValidators = new();

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                if (value == null)
                    return;

                _clientSpawnValidators.Add(value);
                foreach (var hierarchy in _rawHierarchies)
                    hierarchy.onClientSpawnValidate += value;
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
                    break;
                }

                foreach (var hierarchy in _rawHierarchies)
                    hierarchy.onClientSpawnValidate -= value;
            }
        }

        public event IdentityAction onEarlyIdentityAdded;

        public event IdentityAction onIdentityAdded;

        public event IdentityAction onIdentityRemoved;

        public event ObserverAction onObserverAdded;

        public event ObserverAction onLateObserverAdded;

        public event SpawnedAction onSentSpawnPacket;

        public event Action<SceneID> onPreFinishSpawn;

        public void Enable(bool asServer)
        {
            var scenes = _scenes.sceneStates;

            foreach (var (id, sceneState) in scenes)
            {
                if (sceneState.scene.isLoaded)
                    OnPreSceneLoaded(id, asServer);
            }

            _scenes.onSceneRegistrationAdded += OnPreSceneLoaded;
            _scenes.onPreRetainedSceneRebound += OnPreSceneLoaded;
            _scenes.onSceneUnloaded += OnSceneUnloaded;
            _scenes.onSceneRegistrationRemoved += OnSceneUnloaded;
            if (asServer)
                _playersManager.onPlayerLeft += OnPlayerLeft;
        }

        public void Disable(bool asServer)
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].Disable();

            _scenes.onSceneRegistrationAdded -= OnPreSceneLoaded;
            _scenes.onPreRetainedSceneRebound -= OnPreSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;
            _scenes.onSceneRegistrationRemoved -= OnSceneUnloaded;
            if (asServer)
                _playersManager.onPlayerLeft -= OnPlayerLeft;
            ClearExactTopologyTransactions();
        }

        private void OnPreSceneLoaded(SceneID scene, bool asServer)
        {
            if (_hierarchies.ContainsKey(scene))
                return;

            if (!_scenes.TryGetRegisteredOrStagedSceneState(scene, out var sceneState))
            {
                PurrLogger.LogError($"Scene {scene} doesn't exist; trying to create hierarchy module for it?");
                return;
            }

            var hierarchy = new HierarchyV2(_manager, scene, sceneState.scene, _scenePlayersModule, _playersManager,
                asServer);

            try
            {
                hierarchy.onEarlyIdentityAdded += OnEarlyIdentityAdded;
                hierarchy.onObserverAdded += OnObserverAdded;
                hierarchy.onLateObserverAdded += OnLateObserverAdded;
                hierarchy.onIdentityAdded += OnIdentityAdded;
                hierarchy.onIdentityRemoved += OnIdentityRemoved;
                hierarchy.onSentSpawnPacket += OnSentSpawnPacket;
                hierarchy.onPreFinishSpawn += OnPreFinishSpawn;

                for (int i = 0; i < _clientSpawnValidators.Count; i++)
                    hierarchy.onClientSpawnValidate += _clientSpawnValidators[i];

                hierarchy.Enable();

                if (_transferTransition.canReconcile)
                {
                    hierarchy.TransferToNewServer();
                    if (_hasReceivedTransferSession)
                    {
                        hierarchy.ReceiveHostMigrationSession(
                            _receivedTransferSession, _receivedTransferSessionMatched);
                    }
                }

                _rawHierarchies.Add(hierarchy);
                _hierarchies.Add(scene, hierarchy);
            }
            catch
            {
                try
                {
                    hierarchy.Disable();
                }
                catch (Exception cleanupException)
                {
                    PurrLogger.LogError(
                        $"Failed to clean up hierarchy for scene {scene} after initialization failed: " +
                        cleanupException.Message);
                    PurrLogger.LogException(cleanupException);
                }

                throw;
            }
        }

        private void OnSentSpawnPacket(PlayerID player, SceneID scene, NetworkID identity) =>
            onSentSpawnPacket?.Invoke(player, scene, identity);

        private void OnPreFinishSpawn(SceneID scene) => onPreFinishSpawn?.Invoke(scene);

        private void OnLateObserverAdded(PlayerID player, NetworkIdentity identity) =>
            onLateObserverAdded?.Invoke(player, identity);

        private void OnEarlyIdentityAdded(NetworkIdentity identity) =>
            onEarlyIdentityAdded?.Invoke(identity);

        private void OnObserverAdded(PlayerID player, NetworkIdentity identity) =>
            onObserverAdded?.Invoke(player, identity);

        private void OnIdentityAdded(NetworkIdentity identity) =>
            onIdentityAdded?.Invoke(identity);

        private void OnIdentityRemoved(NetworkIdentity identity) =>
            onIdentityRemoved?.Invoke(identity);

        private void OnSceneUnloaded(SceneID scene, bool asserver)
        {
            if (!_hierarchies.TryGetValue(scene, out var hierarchy))
            {
                PurrLogger.LogError($"Hierarchy module for scene {scene} doesn't exist; trying to unload it?");
                return;
            }

            hierarchy.Disable();

            hierarchy.onEarlyIdentityAdded -= OnEarlyIdentityAdded;
            hierarchy.onObserverAdded -= OnObserverAdded;
            hierarchy.onLateObserverAdded -= OnLateObserverAdded;
            hierarchy.onIdentityAdded -= OnIdentityAdded;
            hierarchy.onIdentityRemoved -= OnIdentityRemoved;
            hierarchy.onSentSpawnPacket -= OnSentSpawnPacket;
            hierarchy.onPreFinishSpawn -= OnPreFinishSpawn;

            for (int i = 0; i < _clientSpawnValidators.Count; i++)
                hierarchy.onClientSpawnValidate -= _clientSpawnValidators[i];

            _rawHierarchies.Remove(hierarchy);
            _hierarchies.Remove(scene);
        }

        public void FixedUpdate()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PostNetworkMessages();
        }

        public void PreFixedUpdate()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PreNetworkMessages();
        }

        public bool TryGetHierarchy(SceneID sceneId, out HierarchyV2 o)
        {
            return _hierarchies.TryGetValue(sceneId, out o);
        }

        public bool TryGetHierarchy(Scene scene, out HierarchyV2 o)
        {
            if (_scenes.TryGetSceneID(scene, out var sceneId))
                return _hierarchies.TryGetValue(sceneId, out o);
            o = null;
            return false;
        }

        internal bool TryPreflightExactStaleSceneRetirement(
            SceneID sceneId, Scene physicalScene, bool asServer, out string failure)
        {
            if (!_hierarchies.TryGetValue(sceneId, out var hierarchy) || hierarchy == null)
            {
                failure = $"SceneID {sceneId} has no client hierarchy to retire";
                return false;
            }

            if (!_rawHierarchies.Contains(hierarchy))
            {
                failure = $"SceneID {sceneId} hierarchy is missing from the ordered registry";
                return false;
            }

            return hierarchy.TryPreflightExactStaleSceneRetirement(
                physicalScene, asServer, out failure);
        }

        internal bool TryRetireExactStaleSceneHierarchy(
            SceneID sceneId, Scene physicalScene, bool asServer, out string failure)
        {
            if (!TryPreflightExactStaleSceneRetirement(
                    sceneId, physicalScene, asServer, out failure))
                return false;

            return _hierarchies[sceneId].TryRetireExactStaleSceneHierarchy(
                physicalScene, asServer, out failure);
        }

        public bool TryGetIdentity(SceneID scene, NetworkID id, out NetworkIdentity result)
        {
            if (_hierarchies.TryGetValue(scene, out var hierarchy))
                return hierarchy.TryGetIdentity(id, out result);
            result = null;
            return false;
        }

        internal List<NetworkIdentity> CaptureSpawnedIdentitySnapshot()
        {
            var identities = new List<NetworkIdentity>();
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].AppendSpawnedIdentitySnapshot(identities);
            return identities;
        }

        public bool Cleanup()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                if (!_rawHierarchies[i].Cleanup())
                    return false;
            }

            return true;
        }

        public void PromoteToServerModule()
        {
            if (_manager.hostMigrationSession.canReconcile &&
                !TryValidateExactAuthoritySwitchPreflight(true, out var failure))
            {
                throw new InvalidOperationException(
                    $"Exact hierarchy promotion preflight failed: {failure}.");
            }

            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PromoteToServerModule();
        }

        public void PostPromoteToServerModule()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PostPromoteToServerModule();
        }

        public void TransferToNewServer()
        {
            var transferTransition = _manager.expectedHostMigrationSession;
            if (transferTransition.canReconcile &&
                !TryValidateExactAuthoritySwitchPreflight(false, out var failure))
            {
                throw new InvalidOperationException(
                    $"Exact hierarchy transfer preflight failed: {failure}.");
            }

            _transferTransition = transferTransition;
            _receivedTransferSession = default;
            _hasReceivedTransferSession = false;
            _receivedTransferSessionMatched = false;
            ClearInboundExactTopologyTransaction();

            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].TransferToNewServer();
        }

        internal bool TryValidateExactAuthoritySwitchQueues(out string failure)
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                var hierarchy = _rawHierarchies[i];
                if (hierarchy == null)
                {
                    failure = $"hierarchy list entry {i} is null";
                    return false;
                }

                if (hierarchy.TryGetAuthoritySwitchQueueFailure(out failure))
                    return false;
            }

            failure = null;
            return true;
        }

        internal bool TryValidateExactAuthoritySwitchPreflight(bool promotion, out string failure)
        {
            if (!TryValidateExactAuthoritySwitchQueues(out failure))
                return false;

            if (_scenes == null)
            {
                failure = "the retained hierarchy factory has no scene registry";
                return false;
            }

            if (_rawHierarchies.Count != _hierarchies.Count)
            {
                failure = $"hierarchy list/map counts differ " +
                          $"({_rawHierarchies.Count}/{_hierarchies.Count})";
                return false;
            }

            var raw = new HashSet<HierarchyV2>();
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                var hierarchy = _rawHierarchies[i];
                if (hierarchy == null || !raw.Add(hierarchy))
                {
                    failure = "the hierarchy list contains a null or duplicate entry";
                    return false;
                }
            }

            foreach (var pair in _hierarchies)
            {
                var sceneId = pair.Key;
                var hierarchy = pair.Value;
                if (hierarchy == null || !raw.Contains(hierarchy))
                {
                    failure = $"scene {sceneId} hierarchy is missing from the ordered registry";
                    return false;
                }

                if (!_scenes.TryGetSceneState(sceneId, out var state) ||
                    !state.scene.IsValid() || !state.scene.isLoaded)
                {
                    failure = $"scene {sceneId} hierarchy is not claimed as a loaded scene";
                    return false;
                }

                if (!hierarchy.TryValidateExactAuthoritySwitchGraph(
                        _manager, sceneId, state.scene, promotion, out failure))
                    return false;
            }

            foreach (var pair in _scenes.sceneStates)
            {
                if (!pair.Value.scene.IsValid() || !pair.Value.scene.isLoaded)
                    continue;

                if (!_hierarchies.ContainsKey(pair.Key))
                {
                    failure = $"loaded scene {pair.Key} has no retained hierarchy";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        public void PostTransferToNewServer()
        {
            _transferTransition = default;
            _receivedTransferSession = default;
            _hasReceivedTransferSession = false;
            _receivedTransferSessionMatched = false;
            if (!_exactInboundTopologyFailed)
                ClearInboundExactTopologyTransaction();
        }

        internal bool RegisterExactInboundSceneSet(
            HostMigrationTransitionOptions transition, IReadOnlyList<SceneID> orderedScenes,
            out string failure)
        {
            failure = null;
            if (!transition.canReconcile || orderedScenes == null || orderedScenes.Count == 0)
            {
                failure = "the exact inbound topology set is empty or has no valid migration session";
                return false;
            }

            if (_exactInboundTopologyRegistered)
            {
                failure = _exactInboundTransition == transition
                    ? "the exact inbound topology set was registered more than once"
                    : $"an exact inbound topology set is already registered for {_exactInboundTransition}";
                FailInboundExactTopologyTransaction(failure);
                return false;
            }

            var unique = new HashSet<SceneID>();
            for (var i = 0; i < orderedScenes.Count; i++)
            {
                var scene = orderedScenes[i];
                if (!unique.Add(scene))
                {
                    failure = $"SceneID {scene} appears more than once in the exact topology set";
                    FailInboundExactTopologyTransaction(failure);
                    return false;
                }

                if (!_hierarchies.ContainsKey(scene))
                {
                    failure = $"SceneID {scene} has no client hierarchy at exact topology registration";
                    FailInboundExactTopologyTransaction(failure);
                    return false;
                }
            }

            _exactInboundTransition = transition;
            _exactInboundOrderedScenes.AddRange(orderedScenes);
            _exactInboundExpectedScenes.UnionWith(unique);
            _exactInboundTopologyRegistered = true;
            _exactInboundTopologyOpen = false;
            _exactInboundTopologyFailed = false;
            _exactInboundTopologyFailure = null;
            return true;
        }

        internal bool RegisterExactOutboundSceneSet(PlayerID player,
            HostMigrationTransitionOptions transition, IReadOnlyList<SceneID> orderedScenes,
            out string failure)
        {
            failure = null;
            if (!transition.canReconcile ||
                !_playersManager.IsPendingRetainedHostMigrationPlayer(player, transition) ||
                orderedScenes == null || orderedScenes.Count == 0)
            {
                failure = "the exact outbound topology set is empty or the retained player/session is not pending";
                return false;
            }

            if (_exactOutboundTopologyTransactions.TryGetValue(player, out var existing))
            {
                if (existing.transition != transition)
                {
                    failure = $"player {player} already has an exact topology transaction for " +
                              $"{existing.transition}";
                    return false;
                }

                _exactOutboundTopologyTransactions.Remove(player);
            }

            var copy = new List<SceneID>(orderedScenes.Count);
            var unique = new HashSet<SceneID>();
            for (var i = 0; i < orderedScenes.Count; i++)
            {
                var scene = orderedScenes[i];
                if (!unique.Add(scene))
                {
                    failure = $"SceneID {scene} appears more than once in player {player}'s exact topology set";
                    return false;
                }

                if (!_hierarchies.ContainsKey(scene))
                {
                    failure = $"SceneID {scene} has no authoritative hierarchy for player {player}";
                    return false;
                }

                copy.Add(scene);
            }

            _exactOutboundTopologyTransactions.Add(player,
                new ExactOutboundTopologyTransaction(player, transition, copy));
            return true;
        }

        internal bool TryRecordExactSceneAcknowledgement(PlayerID player, SceneID scene,
            HostMigrationTransitionOptions transition, ExactSceneAcknowledgementKind kind,
            out string failure)
        {
            failure = null;
            if (!_exactOutboundTopologyTransactions.TryGetValue(player, out var transaction) ||
                transaction.transition != transition)
            {
                failure = $"no registered exact topology transaction matches player {player}, {transition}";
                _playersManager.RejectExactOutboundConnection(player, transition, failure);
                return false;
            }

            if (transaction.committing)
            {
                failure = $"player {player}'s exact topology transaction is already committing";
                FailExactOutboundTopologyTransaction(transaction, failure);
                return false;
            }

            if (!transaction.orderedScenes.Contains(scene))
            {
                failure = $"SceneID {scene} is not in player {player}'s authoritative exact scene set";
                FailExactOutboundTopologyTransaction(transaction, failure);
                return false;
            }

            if (!transaction.acknowledgements.TryAdd(scene, kind))
            {
                failure = $"player {player} acknowledged SceneID {scene} more than once";
                FailExactOutboundTopologyTransaction(transaction, failure);
                return false;
            }

            if (transaction.acknowledgements.Count != transaction.orderedScenes.Count)
                return true;

            transaction.committing = true;
            if (!TryCommitExactOutboundTopologyTransaction(transaction, out failure))
            {
                FailExactOutboundTopologyTransaction(transaction, failure);
                return false;
            }

            _exactOutboundTopologyTransactions.Remove(player);
            _scenePlayersModule.ReplayExactSceneCallbacks(
                player, transaction.orderedScenes, transaction.acknowledgements, true);
            return true;
        }

        internal bool IsAwaitingExactSceneAcknowledgement(PlayerID player, SceneID scene,
            HostMigrationTransitionOptions transition)
        {
            return _exactOutboundTopologyTransactions.TryGetValue(player, out var transaction) &&
                   transaction.transition == transition &&
                   transaction.orderedScenes.Contains(scene) &&
                   !transaction.acknowledgements.ContainsKey(scene) &&
                   !transaction.committing;
        }

        private bool TryCommitExactOutboundTopologyTransaction(
            ExactOutboundTopologyTransaction transaction, out string failure)
        {
            failure = null;
            if (!_scenePlayersModule.TryValidateExactPlayerSceneSet(
                    transaction.player, transaction.orderedScenes, out failure))
            {
                failure = "the retained player's authoritative scene set changed before exact " +
                          $"topology proof: {failure}";
                return false;
            }

            var plans = new List<HierarchyV2.ExactSceneSnapshotPlan>(
                transaction.orderedScenes.Count);
            var promoted = false;
            HierarchyFactory promotedClientFactory = null;
            try
            {
                for (var i = 0; i < transaction.orderedScenes.Count; i++)
                {
                    var scene = transaction.orderedScenes[i];
                    if (!_hierarchies.TryGetValue(scene, out var hierarchy) ||
                        !hierarchy.TryPreflightExactSceneSnapshot(
                            transaction.player, transaction.transition,
                            out var promotedClient, out failure))
                        return false;

                    var scenePromoted = promotedClient != null;
                    if (i == 0)
                        promoted = scenePromoted;
                    else if (scenePromoted != promoted)
                    {
                        failure = "the exact hierarchy set mixes remote and promoted-listen delivery";
                        return false;
                    }

                    if (!scenePromoted)
                        continue;

                    if (!hierarchy.TryStagePromotedListenSnapshotPlan(
                            transaction.player, transaction.transition, promotedClient,
                            out var stagedPlan, out failure))
                        return false;
                    plans.Add(stagedPlan);
                }

                if (promoted)
                {
                    if (!_manager.TryGetModule<HierarchyFactory>(false, out promotedClientFactory))
                    {
                        failure = "the promoted listen-client hierarchy factory is unavailable";
                        return false;
                    }

                    for (var i = 0; i < plans.Count; i++)
                    {
                        var plan = plans[i];
                        if (!plan.promotedListenClient.TryAttachPromotedListenGraphCore(
                                plan.hierarchy, out plan.promotedNewlyRegistered, out failure))
                            return false;
                    }

                    for (var i = 0; i < plans.Count; i++)
                    {
                        var plan = plans[i];
                        if (!plan.promotedListenClient.TryCapturePromotedClientSnapshotPlanProof(
                                plan, out failure))
                            return false;
                    }

                    for (var i = 0; i < plans.Count; i++)
                    {
                        var plan = plans[i];
                        if (!plan.promotedListenClient.TryCapturePromotedListenTransfer(
                                transaction.transition, ref plan.promotedManifest, out failure))
                            return false;
                    }
                }

                if (!_manager.TryBeginHostMigrationServerBaselineCapture(
                        transaction.player, transaction.transition, out failure))
                    return false;

                if (promoted)
                {
                    for (var i = 0; i < plans.Count; i++)
                    {
                        var plan = plans[i];
                        if (!plan.promotedListenClient.TryPublishPromotedListenRegistrySignals(
                                plan.promotedNewlyRegistered, out failure))
                            return false;
                    }

                    for (var i = 0; i < plans.Count; i++)
                    {
                        var client = plans[i].promotedListenClient;
                        promotedClientFactory.RegisterExactTransferPreamble(
                            client, transaction.transition, true, null);
                        if (!promotedClientFactory.TryAuthorizeExactTransferSnapshot(
                                client, transaction.transition, out failure) &&
                            i == plans.Count - 1)
                            return false;
                    }
                }

                if (!promoted)
                    plans.Clear();
                for (var i = 0; i < transaction.orderedScenes.Count; i++)
                {
                    var hierarchy = _hierarchies[transaction.orderedScenes[i]];
                    var staged = promoted ? plans[i] : null;
                    var promotedClient = staged?.promotedListenClient;
                    if (!hierarchy.TryPrepareExactSceneSnapshot(
                            transaction.player, transaction.transition, promotedClient,
                            staged, out var prepared, out failure))
                        return false;
                    if (!promoted)
                        plans.Add(prepared);
                }

                if (!_manager.TryPrepareHostMigrationServerBaselines(
                        transaction.player, transaction.transition, out failure))
                    return false;

                if (!_scenePlayersModule.TryValidateExactPlayerSceneSet(
                        transaction.player, transaction.orderedScenes, out failure))
                {
                    failure = "the retained player's authoritative scene set changed while exact " +
                              $"package baselines were captured: {failure}";
                    return false;
                }

                for (var i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i];
                    if (plan.hierarchy.TryValidateExactSceneSnapshotPlan(plan, out failure))
                        continue;

                    failure = $"Scene {plan.hierarchy.sceneId} retained graph changed after exact " +
                              $"snapshot staging and package hooks: {failure}";
                    return false;
                }

                for (var i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].hierarchy.TryPublishExactScenePreamble(plans[i], out failure))
                        return false;
                }

                for (var i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].hierarchy.TryPublishExactSpawnTopology(plans[i], out failure))
                        return false;
                }

                if (!_manager.TryPublishHostMigrationServerBaselines(
                        transaction.player, transaction.transition, out failure))
                    return false;

                for (var i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].hierarchy.TryCommitExactSceneSnapshot(plans[i], out failure))
                        return false;
                }

                for (var i = 0; i < plans.Count; i++)
                    plans[i].AcceptStaging();

                return true;
            }
            finally
            {
                for (var i = 0; i < plans.Count; i++)
                    plans[i]?.Dispose();
            }
        }

        private void FailExactOutboundTopologyTransaction(
            ExactOutboundTopologyTransaction transaction, string failure)
        {
            if (transaction == null)
                return;

            _exactOutboundTopologyTransactions.Remove(transaction.player);
            var hasBarrier = _playersManager.HasExactOutboundBarrier(
                transaction.player, transaction.transition);
            try
            {
                if (hasBarrier)
                    _manager.FlushBatchedRPCs();
                else if (_manager.TryGetModule<RPCModule>(true, out var rpcModule))
                    rpcModule.DropBatchedRPCs(transaction.player);
            }
            catch (Exception exception)
            {
                PurrLogger.LogException(exception);
                if (_manager.TryGetModule<RPCModule>(true, out var rpcModule))
                    rpcModule.DropBatchedRPCs(transaction.player);
            }
            for (var i = 0; i < transaction.orderedScenes.Count; i++)
            {
                if (_hierarchies.TryGetValue(transaction.orderedScenes[i], out var hierarchy))
                {
                    hierarchy.RejectExactSpawnSnapshotFromFactory(
                        transaction.player, transaction.transition, failure);
                }
            }

            _playersManager.RejectExactOutboundConnection(
                transaction.player, transaction.transition,
                failure ?? "the transaction-wide exact hierarchy proof failed");
        }

        internal void RegisterExactTransferPreamble(HierarchyV2 hierarchy,
            HostMigrationTransitionOptions transition, bool accepted, string reason)
        {
            if (_exactInboundTopologyFailed)
                return;

            if (!_exactInboundTopologyRegistered || transition != _exactInboundTransition ||
                hierarchy == null || !_exactInboundExpectedScenes.Contains(hierarchy.sceneId))
            {
                FailInboundExactTopologyTransaction(
                    $"Scene {hierarchy?.sceneId.ToString() ?? "<null>"} supplied an unexpected exact " +
                    $"topology preamble for {transition}.");
                return;
            }

            if (!accepted)
            {
                FailInboundExactTopologyTransaction(reason ??
                    $"Scene {hierarchy.sceneId} rejected its exact topology preamble.");
                return;
            }

            if (!_exactInboundAcceptedScenes.Add(hierarchy.sceneId))
            {
                FailInboundExactTopologyTransaction(
                    $"Scene {hierarchy.sceneId} accepted more than one exact topology preamble.");
                return;
            }

            if (_exactInboundAcceptedScenes.Count != _exactInboundExpectedScenes.Count)
                return;

            for (var i = 0; i < _exactInboundOrderedScenes.Count; i++)
            {
                var scene = _exactInboundOrderedScenes[i];
                string armFailure = null;
                if (!_hierarchies.TryGetValue(scene, out var participant) ||
                    !participant.TryArmTransferReconciliation(out armFailure))
                {
                    FailInboundExactTopologyTransaction(
                        armFailure ?? $"Scene {scene} disappeared while arming exact reconciliation.");
                    return;
                }
            }

            _exactInboundTopologyOpen = true;
        }

        internal bool TryAuthorizeExactTransferSnapshot(HierarchyV2 hierarchy,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (_exactInboundTopologyRegistered && !_exactInboundTopologyFailed &&
                _exactInboundTopologyOpen && transition == _exactInboundTransition &&
                hierarchy != null && _exactInboundExpectedScenes.Contains(hierarchy.sceneId))
            {
                failure = null;
                return true;
            }

            failure = _exactInboundTopologyFailure ??
                      (!_exactInboundTopologyRegistered
                          ? "the authoritative exact scene set has not been registered"
                          : $"only {_exactInboundAcceptedScenes.Count}/{_exactInboundExpectedScenes.Count} " +
                            "scene topology preambles have been accepted");
            return false;
        }

        private void FailInboundExactTopologyTransaction(string failure)
        {
            if (_exactInboundTopologyFailed)
                return;

            _exactInboundTopologyFailed = true;
            _exactInboundTopologyOpen = false;
            _exactInboundTopologyFailure = failure;
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                var hierarchy = _rawHierarchies[i];
                if (hierarchy != null && hierarchy.IsAwaitingExactTransferPreamble(_exactInboundTransition))
                    hierarchy.AbortExactTransferFromFactory(failure);
            }
        }

        private void ClearInboundExactTopologyTransaction()
        {
            _exactInboundOrderedScenes.Clear();
            _exactInboundExpectedScenes.Clear();
            _exactInboundAcceptedScenes.Clear();
            _exactInboundTransition = default;
            _exactInboundTopologyRegistered = false;
            _exactInboundTopologyOpen = false;
            _exactInboundTopologyFailed = false;
            _exactInboundTopologyFailure = null;
        }

        private void ClearExactTopologyTransactions()
        {
            _exactOutboundTopologyTransactions.Clear();
            ClearInboundExactTopologyTransaction();
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            _exactOutboundTopologyTransactions.Remove(player);
        }

        internal void ReceiveHostMigrationSession(HostMigrationTransitionOptions session, bool matched)
        {
            _receivedTransferSession = session;
            _hasReceivedTransferSession = true;
            _receivedTransferSessionMatched = matched;

            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].ReceiveHostMigrationSession(session, matched);
        }

        public void EvaluateVisibilityForPlayer(PlayerID player)
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].EvaluateVisibilityForPlayer(player);
        }
    }
}
