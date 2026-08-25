using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash = PurrNet.Utils.Hasher;

namespace PurrNet.Modules
{
    public struct PendingSceneOperation
    {
        public int buildIndex;
        public uint scenePathHash;
        public SceneID idToAssign;
        public PurrSceneSettings settings;
        [UsedImplicitly]
        public AsyncOperation operation;
    }

    internal struct PendingSceneUnload
    {
        public Scene scene;
        public AsyncOperation operation;
    }

    public struct SceneState
    {
        /// <summary>
        /// The unity scene object this ID is associated with
        /// </summary>
        public Scene scene;

        /// <summary>
        /// The network settings for this scene
        /// </summary>
        public PurrSceneSettings settings;

        public SceneState(Scene scene, PurrSceneSettings settings)
        {
            this.scene = scene;
            this.settings = settings;
        }
    }

    internal readonly struct PromotionSceneCandidate
    {
        public readonly SceneID id;
        public readonly LoadSceneMode retainedMode;
        public readonly LocalPhysicsMode physicsMode;
        public readonly bool isOriginalScene;
        public readonly bool isAddressable;

        public PromotionSceneCandidate(SceneID id, LoadSceneMode retainedMode,
            LocalPhysicsMode physicsMode, bool isOriginalScene, bool isAddressable = false)
        {
            this.id = id;
            this.retainedMode = retainedMode;
            this.physicsMode = physicsMode;
            this.isOriginalScene = isOriginalScene;
            this.isAddressable = isAddressable;
        }
    }

    internal readonly struct PromotedListenSceneBinding
    {
        public readonly SceneID id;
        public readonly SceneState state;

        public PromotedListenSceneBinding(SceneID id, SceneState state)
        {
            this.id = id;
            this.state = state;
        }
    }

    public struct PurrSceneSettings
    {
        public LoadSceneMode mode;
        public LocalPhysicsMode physicsMode;
        public bool isPublic;
    }

    public delegate void OnSceneActionEvent(SceneID scene, bool asServer);

    public delegate void OnSceneVisibilityEvent(SceneID scene, bool isVisible, bool asServer);

    public partial class ScenesModule : INetworkModule, IFixedUpdate, ICleanup, IConnectionStateListener,
        ITransferToNewServer, IPostTransferToNewServer, IPromoteToServerModule
    {
        private readonly struct ExactLoadedSceneAdoption
        {
            internal readonly SceneID newId;
            internal readonly Scene scene;
            internal readonly PurrSceneSettings settings;
            internal readonly LocalPhysicsMode authoritativePhysicsMode;

            internal ExactLoadedSceneAdoption(
                SceneID newId,
                Scene scene,
                PurrSceneSettings settings,
                LocalPhysicsMode authoritativePhysicsMode)
            {
                this.newId = newId;
                this.scene = scene;
                this.settings = settings;
                this.authoritativePhysicsMode = authoritativePhysicsMode;
            }
        }

        private static readonly Dictionary<int, uint> _buildIndexToHash = new Dictionary<int, uint>();
        private static readonly Dictionary<uint, int> _hashToBuildIndex = new Dictionary<uint, int>();
        private static readonly HashSet<uint> _ambiguousBuildSceneHashes = new HashSet<uint>();
        private static bool _sceneHashCacheBuilt;

        private readonly NetworkManager _networkManager;
        private readonly PlayersManager _players;

        private readonly SceneHistory _history;
        private bool _asServer;

        private readonly List<PendingSceneOperation> _pendingOperations = new List<PendingSceneOperation>();
        private readonly List<PendingSceneUnload> _pendingSceneUnloads = new List<PendingSceneUnload>();
        private readonly Queue<SceneAction> _actionsQueue = new Queue<SceneAction>();

        private readonly Dictionary<SceneID, SceneState> _scenes = new Dictionary<SceneID, SceneState>();
        private readonly Dictionary<Scene, SceneID> _idToScene = new Dictionary<Scene, SceneID>();
        private readonly List<SceneID> _rawScenes = new List<SceneID>();
        private readonly HashSet<SceneID> _sceneActionScenes = new HashSet<SceneID>();
        private readonly HashSet<SceneID> _preparedRetainedSceneRebounds = new HashSet<SceneID>();
        private readonly HashSet<SceneID> _retainedSceneRebounds = new HashSet<SceneID>();

        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onPreSceneLoaded;

        /// <summary>
        /// Callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for after onSceneLoaded has been called
        /// </summary>
        public event OnSceneActionEvent onPostSceneLoaded;

        /// <summary>
        /// Called when exact host migration reconciles an authoritative scene descriptor with
        /// the same already-loaded Unity scene. This is a continuity notification, not a scene
        /// load lifecycle event; <see cref="onPreSceneLoaded"/>, <see cref="onSceneLoaded"/> and
        /// <see cref="onPostSceneLoaded"/> are not replayed for the retained scene.
        /// </summary>
        public event OnSceneActionEvent onSceneRebound;

        internal event OnSceneActionEvent onPreRetainedSceneRebound;

        internal event OnSceneActionEvent onSceneRegistrationAdded;

        internal event OnSceneActionEvent onRetainedSceneRebound;

        /// <summary>
        /// First callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onPreSceneUnloaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Callback for after onSceneUnloaded has been called
        /// </summary>
        public event OnSceneActionEvent onPostSceneUnloaded;

        internal event OnSceneActionEvent onSceneRegistrationRemoved;

        /// <summary>
        /// Callback for when a scene's visibility changes
        /// </summary>
        public event OnSceneVisibilityEvent onSceneVisibilityChanged;

        private ushort _nextSceneID = 1;
        private ScenePlayersModule _scenePlayers;

        public IReadOnlyList<SceneID> scenes
        {
            get
            {
                if (_stagedExactScenes.Count == 0)
                    return _rawScenes;

                var published = new List<SceneID>(
                    Math.Max(0, _rawScenes.Count - _stagedExactScenes.Count));
                for (var i = 0; i < _rawScenes.Count; i++)
                {
                    if (!_stagedExactScenes.ContainsKey(_rawScenes[i]))
                        published.Add(_rawScenes[i]);
                }
                return published;
            }
        }

        public IReadOnlyDictionary<SceneID, SceneState> sceneStates
        {
            get
            {
                if (_stagedExactScenes.Count == 0)
                    return _scenes;

                var published = new Dictionary<SceneID, SceneState>(
                    Math.Max(0, _scenes.Count - _stagedExactScenes.Count));
                foreach (var pair in _scenes)
                {
                    if (!_stagedExactScenes.ContainsKey(pair.Key))
                        published.Add(pair.Key, pair.Value);
                }
                return published;
            }
        }

        private SceneID GetNextID() => new(_nextSceneID++);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneHashCache()
        {
            _buildIndexToHash.Clear();
            _hashToBuildIndex.Clear();
            _ambiguousBuildSceneHashes.Clear();
            _sceneHashCacheBuilt = false;
        }

        public ScenesModule(NetworkManager manager, PlayersManager players)
        {
            _networkManager = manager;
            _players = players;
            _history = new SceneHistory();
        }

        internal void SetScenePlayers(ScenePlayersModule scenePlayersModule)
        {
            _scenePlayers = scenePlayersModule;
        }

        public bool TryGetSceneState(SceneID sceneID, out SceneState state)
        {
            if (_stagedExactScenes.Count > 0 && _stagedExactScenes.ContainsKey(sceneID))
            {
                state = default;
                return false;
            }

            return _scenes.TryGetValue(sceneID, out state);
        }

        internal bool TryGetRegisteredOrStagedSceneState(
            SceneID sceneID,
            out SceneState state) => _scenes.TryGetValue(sceneID, out state);

        private void AddScene(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            if (!TryAddSceneRegistration(scene, settings, id))
                return;

            InvokeCoreSceneRegistrationAdded(id);
            PlayLoadEventsForScene(id);
        }

        private bool TryAddSceneRegistration(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            if (_scenes.TryGetValue(id, out var state))
            {
                PurrLogger.LogError($"Scene with ID {id} already exists under {state.scene.name}");
                return false;
            }

            if (_idToScene.TryGetValue(scene, out var existingId))
            {
                PurrLogger.LogError(
                    $"Unity scene '{scene.name}' is already registered under SceneID {existingId}");
                return false;
            }

            _scenes.Add(id, new SceneState(scene, settings));
            _idToScene.Add(scene, id);
            _rawScenes.Add(id);

            return true;
        }

        private void InvokeCoreSceneRegistrationAdded(SceneID id)
        {
            if (onSceneRegistrationAdded == null)
                return;

            var failures = new List<Exception>();
            var callbacks = onSceneRegistrationAdded.GetInvocationList();
            for (var i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((OnSceneActionEvent)callbacks[i]).Invoke(id, _asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                    failures.Add(e);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    $"One or more core modules failed registration for SceneID {id}.",
                    failures);
            }
        }

        private bool HasScene(Scene scene)
        {
            foreach (var state in _scenes.Values)
            {
                if (state.scene.handle == scene.handle)
                    return true;
            }

            return false;
        }

        private void PlayLoadEventsForScene(SceneID id)
        {
            onPreSceneLoaded?.Invoke(id, _asServer);
            onSceneLoaded?.Invoke(id, _asServer);
            onPostSceneLoaded?.Invoke(id, _asServer);
        }

        internal bool PlayRetainedSceneReboundForScene(SceneID id)
        {
            if (_retainedSceneRebounds.Contains(id))
                return true;

            if (_transferReconciliationFailure != null)
                return false;

            if (!PrepareRetainedSceneReboundForScene(id) ||
                !InvokeCoreSceneRebound(onRetainedSceneRebound, id, "rebound"))
                return false;

            _retainedSceneRebounds.Add(id);
            InvokePublicSceneRebound(id);
            return true;
        }

        private bool PrepareRetainedSceneReboundForScene(SceneID id)
        {
            if (_retainedSceneRebounds.Contains(id) ||
                _preparedRetainedSceneRebounds.Contains(id))
                return true;

            if (!InvokeCoreSceneRebound(onPreRetainedSceneRebound, id, "pre-rebound"))
                return false;

            _preparedRetainedSceneRebounds.Add(id);
            return true;
        }

        private bool InvokeCoreSceneRebound(
            OnSceneActionEvent callbacks,
            SceneID id,
            string phase)
        {
            if (_transferReconciliationFailure != null)
                return false;

            if (callbacks == null)
                return true;

            var invocationList = callbacks.GetInvocationList();
            for (var i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((OnSceneActionEvent)invocationList[i]).Invoke(id, _asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                    FailTransferReconciliation(
                        $"Core scene {phase} failed for retained SceneID {id}.", e);
                    return false;
                }

                if (_transferReconciliationFailure != null)
                    return false;
            }

            return true;
        }

        private void InvokePublicSceneRebound(SceneID id)
        {
            if (onSceneRebound == null)
                return;

            var callbacks = onSceneRebound.GetInvocationList();
            for (var i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((OnSceneActionEvent)callbacks[i]).Invoke(id, _asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }
            }
        }

        /// <summary>
        /// Used to modify whether the given scene is public or not
        /// </summary>
        /// <param name="scene">The SceneID of the scene to modify</param>
        /// <param name="isPublic">Whether the given scene should be public</param>
        public void UpdateSceneVisibility(SceneID scene, bool isPublic)
        {
            if (_asServer)
            {
                PurrLogger.LogError("Only clients can change scene visibility; for now at least ;)");
                return;
            }

            if ((_stagedExactScenes.Count > 0 && _stagedExactScenes.ContainsKey(scene)) ||
                !_scenes.TryGetValue(scene, out var state))
            {
                PurrLogger.LogError($"Scene with ID {scene} not found");
                return;
            }

            state.settings.isPublic = isPublic;
            _scenes[scene] = state;

            onSceneVisibilityChanged?.Invoke(scene, isPublic, _asServer);
        }

        private readonly List<SceneID> _scenesToTriggerUnloadEvent = new List<SceneID>();

        private void RemoveScene(Scene scene)
        {
            RemoveScene(scene, false);
        }

        private void RemoveScene(Scene scene, bool playUnloadEventsImmediately)
        {
            if (!TryRemoveSceneRegistration(scene, out var id))
                return;

            if (playUnloadEventsImmediately)
                PlayUnloadEventsForScene(id);
            else _scenesToTriggerUnloadEvent.Add(id);
        }

        private bool TryRemoveSceneRegistration(Scene scene, out SceneID id)
        {
            if (!_idToScene.TryGetValue(scene, out id))
                return false;

            _scenes.Remove(id);
            _idToScene.Remove(scene);
            _rawScenes.Remove(id);
            _sceneActionScenes.Remove(id);
            _preparedRetainedSceneRebounds.Remove(id);
            _retainedSceneRebounds.Remove(id);
            _scenesToTriggerUnloadEvent.Remove(id);

            return true;
        }

        internal bool DetachRetainedPhysicalSceneRegistration(Scene scene)
        {
            var scenes = new[] { scene };
            if (!TryDetachRetainedPhysicalSceneRegistrations(scenes, out _))
                return false;

            return true;
        }

        private bool TryDetachRetainedPhysicalSceneRegistrations(
            IReadOnlyList<Scene> scenes,
            out string failure,
            bool isolateCallbackFailures = false)
        {
            failure = null;
            var bindings = new List<KeyValuePair<Scene, SceneID>>(scenes.Count);
            var physicalScenes = new HashSet<Scene>();
            for (var i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                if (!scene.IsValid() || !physicalScenes.Add(scene) ||
                    !_idToScene.TryGetValue(scene, out var id) ||
                    !_scenes.TryGetValue(id, out var state) ||
                    state.scene.handle != scene.handle ||
                    !_rawScenes.Contains(id))
                {
                    failure = $"Retained scene '{scene.name}' changed before registration detachment.";
                    return false;
                }

                bindings.Add(new KeyValuePair<Scene, SceneID>(scene, id));
            }

            if (bindings.Count > 0 && _isPromotionHistoryRebuild)
                _promotionHistoryMaterialMutationStarted = true;

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (!TryRemoveSceneRegistration(binding.Key, out var removedId) ||
                    removedId != binding.Value)
                {
                    throw new InvalidOperationException(
                        "A retained scene registry changed during its callback-free batch detachment.");
                }
            }

            var callbackFailures = new List<Exception>();
            for (var i = 0; i < bindings.Count; i++)
                InvokeSceneRegistrationRemoved(bindings[i].Value, callbackFailures);

            if (callbackFailures.Count > 0)
            {
                if (isolateCallbackFailures)
                {
                    PurrLogger.LogException(new AggregateException(
                        "One or more core modules failed post-commit scene registration cleanup.",
                        callbackFailures));
                    return true;
                }

                throw new AggregateException(
                    "One or more core modules failed retained scene registration cleanup.",
                    callbackFailures);
            }

            return true;
        }

        private void InvokeSceneRegistrationRemoved(
            SceneID id,
            ICollection<Exception> failures)
        {
            if (onSceneRegistrationRemoved == null)
                return;

            var callbacks = onSceneRegistrationRemoved.GetInvocationList();
            for (var i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((OnSceneActionEvent)callbacks[i]).Invoke(id, _asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                    failures.Add(e);
                }
            }
        }

        private void PlayUnloadEventsForScene(SceneID id)
        {
            if (_exactStructuralSceneCommitStarted)
            {
                InvokeExactCommittedSceneCallbacks(onPreSceneUnloaded, id, "pre-unload");
                InvokeExactCommittedSceneCallbacks(onSceneUnloaded, id, "unload");
                InvokeExactCommittedSceneCallbacks(onPostSceneUnloaded, id, "post-unload");
                return;
            }

            onPreSceneUnloaded?.Invoke(id, _asServer);
            onSceneUnloaded?.Invoke(id, _asServer);
            onPostSceneUnloaded?.Invoke(id, _asServer);
        }

        public void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (state != ConnectionState.Connected)
                return;

            if (!_wasSetup)
                Setup(asServer);
        }

        private bool _wasSetup;

        static GameObject _dontDestroyOnLoad;

        private static Scene GetDontDestroyOnLoadScene()
        {
            if (_dontDestroyOnLoad)
                return _dontDestroyOnLoad.scene;
            _dontDestroyOnLoad = new GameObject("PurrNet:DontDestroyOnLoad")
            {
                hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy
            };
            UnityEngine.Object.DontDestroyOnLoad(_dontDestroyOnLoad);
            return _dontDestroyOnLoad.scene;
        }

        static bool IsDontDestroyOnLoadScene(Scene scene)
        {
            return scene.name is "DontDestroyOnLoad";
        }


        public void PromoteToServerModule()
        {
            _asServer = true;
            RemoveUnloadedSceneStates();
            AdvanceNextSceneIdPastRetainedScenes();
            _rebuildHistoryOnNextPlayerJoin = true;
            _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
            _players.Unsubscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            _players.onPrePlayerJoined += OnPlayerJoined;
            _players.onPreHostMigrationConnectionRebound += OnHostMigrationConnectionRebound;
            _scenePlayers.onPlayerJoinedScene += OnPlayerJoinedScene;
            _scenePlayers.onPlayerLeftScene += OnPlayerLeftScene;
        }

        private bool _rebuildHistoryOnNextPlayerJoin;
        private bool _isPromotionHistoryRebuild;
        private bool _promotionHistoryMaterialMutationStarted;

        private void AdvanceNextSceneIdPastRetainedScenes()
        {
            foreach (var pair in _scenes)
            {
                var nextSceneId = (int)pair.Key.id + 1;
                if (nextSceneId > _nextSceneID && nextSceneId <= ushort.MaxValue)
                    _nextSceneID = (ushort)nextSceneId;
            }
        }

        private void RemoveUnloadedSceneStates()
        {
            for (var i = _rawScenes.Count - 1; i >= 0; i--)
            {
                var id = _rawScenes[i];

                if (!_scenes.TryGetValue(id, out var state))
                {
                    _rawScenes.RemoveAt(i);
                    _sceneActionScenes.Remove(id);
                    _scenesToTriggerUnloadEvent.Remove(id);
                    continue;
                }

                if (state.scene.IsValid() && state.scene.isLoaded)
                    continue;

                _scenes.Remove(id);
                _rawScenes.RemoveAt(i);
                _sceneActionScenes.Remove(id);
                _scenesToTriggerUnloadEvent.Remove(id);

                if (_idToScene.TryGetValue(state.scene, out var mappedId) && mappedId == id)
                    _idToScene.Remove(state.scene);
            }
        }

        private void RebuildHistoryFromLoadedBuildScenes()
        {
            var exactMigration = _networkManager.hostMigrationSession.canReconcile;
            var priorHistory = exactMigration ? _history.Capture() : null;
            _isPromotionHistoryRebuild = exactMigration;
            _promotionHistoryMaterialMutationStarted = false;
            try
            {
                RebuildHistoryFromLoadedBuildScenesCore();
            }
            catch
            {
                if (priorHistory != null && !_promotionHistoryMaterialMutationStarted)
                    _history.Restore(priorHistory);
                throw;
            }
            finally
            {
                _isPromotionHistoryRebuild = false;
                _promotionHistoryMaterialMutationStarted = false;
            }
        }

        private void RebuildHistoryFromLoadedBuildScenesCore()
        {
            var exactMigration = _networkManager.hostMigrationSession.canReconcile;
            var staleKeptPromotionScenes = exactMigration ? new List<Scene>() : null;
            var promotionActiveScene = default(Scene);
#if ADDRESSABLES_PURRNET_SUPPORT
            if (exactMigration)
            {
                for (var i = 0; i < _pendingAddressableOperations.Count; i++)
                {
                    var pending = _pendingAddressableOperations[i];
                    if (!pending.handle.IsValid() ||
                        (pending.handle.IsDone &&
                         pending.handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded))
                    {
                        throw new InvalidOperationException(
                            $"Cannot promote because Addressable scene {pending.idToAssign} " +
                            $"('{pending.guid}') failed before the authoritative manifest was rebuilt.",
                            pending.handle.IsValid() ? pending.handle.OperationException : null);
                    }
                }
            }
#endif
            ProcessCompletedAddressableLoads();
            if (_pendingSceneUnloads.Count > 0
#if ADDRESSABLES_PURRNET_SUPPORT
                || _pendingAddressableUnloads.Count > 0
#endif
               )
                PollTransferReconciliationOperations();

            if (exactMigration && (_pendingOperations.Count > 0 || _pendingSceneUnloads.Count > 0
#if ADDRESSABLES_PURRNET_SUPPORT
                                   || _pendingAddressableOperations.Count > 0 ||
                                   _pendingAddressableUnloads.Count > 0
#endif
                                  ))
            {
                throw new InvalidOperationException(
                    "Cannot promote while scene load or unload operations are still in flight; " +
                    "the promoted host could not advertise an authoritative scene manifest.");
            }

            _history.Clear();

            for (var i = _rawScenes.Count - 1; i >= 0; i--)
            {
                var id = _rawScenes[i];
                if (!_scenes.TryGetValue(id, out var state) ||
                    !state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                if (state.scene.buildIndex >= 0)
                    continue;

#if ADDRESSABLES_PURRNET_SUPPORT
                if (HasAddressableSceneRegistration(id))
                    continue;
#endif

                if (!exactMigration)
                    continue;

                string sceneRetirementFailure = null;
                if (!_networkManager.TryGetModule<HierarchyFactory>(true, out var hierarchyFactory) ||
                    !hierarchyFactory.TryPreflightExactStaleSceneRetirement(
                        id, state.scene, true, out sceneRetirementFailure))
                {
                    throw new InvalidOperationException(
                        $"Cannot promote descriptorless bootstrap scene '{state.scene.name}' " +
                        $"(SceneID {id}) because its server hierarchy cannot retire in place: " +
                        (sceneRetirementFailure ?? "the promoted server hierarchy factory is unavailable"));
                }

                staleKeptPromotionScenes.Add(state.scene);
            }

            if (exactMigration)
            {
                var candidates = new List<PromotionSceneCandidate>();
                var originalScene = _networkManager.originalScene;

                for (var i = 0; i < _rawScenes.Count; i++)
                {
                    var id = _rawScenes[i];
                    if (!_scenes.TryGetValue(id, out var state) ||
                        !state.scene.IsValid() || !state.scene.isLoaded ||
                        state.scene.buildIndex < 0)
                        continue;

#if ADDRESSABLES_PURRNET_SUPPORT
                    if (HasAddressableSceneRegistration(id))
                        continue;
#endif

                    if (!TryGetPhysicalLocalPhysicsMode(
                            state.scene, out var physicalMode, out var physicsFailure))
                    {
                        throw new InvalidOperationException(
                            $"Cannot promote SceneID {id} because {physicsFailure}");
                    }

                    candidates.Add(new PromotionSceneCandidate(
                        id,
                        state.settings.mode,
                        physicalMode,
                        originalScene.IsValid() && state.scene.handle == originalScene.handle));
                }

                CollectAddressablePromotionSceneCandidates(candidates);

                if (!TrySelectPromotionBaseScene(candidates, out var selectedBase, out var baseFailure))
                {
                    throw new InvalidOperationException(
                        $"Cannot promote because the authoritative scene manifest has no " +
                        $"trustworthy Single-mode base: {baseFailure}");
                }

                var activeScene = SceneManager.GetActiveScene();
                SceneID? retainedActive = null;
                if (activeScene.IsValid() && activeScene.isLoaded &&
                    _idToScene.TryGetValue(activeScene, out var retainedActiveId))
                    retainedActive = retainedActiveId;

                if (!TrySelectPromotionActiveScene(
                        candidates, selectedBase, retainedActive,
                        out var selectedActive, out _, out var activeFailure) ||
                    !_scenes.TryGetValue(selectedActive, out var activeState) ||
                    !activeState.scene.IsValid() || !activeState.scene.isLoaded)
                {
                    throw new InvalidOperationException(
                        "Cannot promote because no authoritative active scene can be selected: " +
                        (activeFailure ?? $"SceneID {selectedActive} is no longer loaded"));
                }
                promotionActiveScene = activeState.scene;

                var orderedCandidates = OrderPromotionSceneCandidates(candidates, selectedBase);
                for (var i = 0; i < orderedCandidates.Count; i++)
                {
                    var candidate = orderedCandidates[i];
                    if (candidate.isAddressable)
                        AddLoadedAddressableSceneToHistory(candidate.id, i == 0);
                    else AddLoadedBuildSceneToHistory(candidate.id, true, i == 0);
                }

                _history.AddSetActiveAction(new SetActiveSceneAction
                {
                    sceneID = selectedActive
                });
            }
            else
            {
                for (var i = 0; i < _rawScenes.Count; i++)
                {
                    var id = _rawScenes[i];
                    if (!_scenes.TryGetValue(id, out var state))
                        continue;

                    if (!state.scene.IsValid() || !state.scene.isLoaded)
                        continue;

#if ADDRESSABLES_PURRNET_SUPPORT
                    if (HasAddressableSceneRegistration(id))
                        continue;
#endif

                    if (state.scene.buildIndex < 0)
                        continue;

                    AddLoadedBuildSceneToHistory(id, false, false);
                }

                RebuildAddressableHistoryFromLoadedScenes();
            }

            _history.Flush();

            if (exactMigration &&
                !TryValidateExactSceneManifestShape(
                    _history.GetFullHistory().actions, out var uniquenessFailure))
            {
                _history.Clear();
                throw new InvalidOperationException(
                    $"Cannot promote because the authoritative scene manifest is ambiguous: " +
                    uniquenessFailure);
            }

            if (!exactMigration)
                return;

            var currentActiveScene = SceneManager.GetActiveScene();
            if ((!currentActiveScene.IsValid() ||
                 currentActiveScene.handle != promotionActiveScene.handle) &&
                !SceneManager.SetActiveScene(promotionActiveScene))
            {
                _history.Clear();
                throw new InvalidOperationException(
                    $"Cannot promote because Unity refused authoritative active scene " +
                    $"'{promotionActiveScene.name}'.");
            }

            if (staleKeptPromotionScenes.Count > 0)
                _promotionHistoryMaterialMutationStarted = true;

            if (!TryRetireExactStaleSceneHierarchies(
                    staleKeptPromotionScenes, true, out var retirementFailure))
            {
                _history.Clear();
                throw new InvalidOperationException(
                    "Cannot promote because retained bootstrap hierarchy retirement failed: " +
                    retirementFailure);
            }

            if (!TryDetachRetainedPhysicalSceneRegistrations(
                    staleKeptPromotionScenes, out var detachFailure, true))
            {
                _history.Clear();
                throw new InvalidOperationException(
                    "Cannot promote because retained bootstrap registration cleanup failed preflight: " +
                    detachFailure);
            }
        }

        internal static bool TrySelectPromotionBaseScene(
            IReadOnlyList<PromotionSceneCandidate> candidates,
            out SceneID baseScene,
            out string failure)
        {
            baseScene = default;
            failure = null;

            if (candidates == null || candidates.Count == 0)
            {
                failure = "no stable loaded scene descriptors were found.";
                return false;
            }

            var retainedSingleCount = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.retainedMode != LoadSceneMode.Single)
                    continue;

                retainedSingleCount++;
                baseScene = candidate.id;
            }

            if (retainedSingleCount == 1)
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    if (candidate.id != baseScene)
                        continue;

                    if (candidate.physicsMode == LocalPhysicsMode.None)
                        return true;

                    failure = $"retained Single scene {candidate.id} uses " +
                              $"LocalPhysicsMode.{candidate.physicsMode}.";
                    return false;
                }
            }

            if (retainedSingleCount > 1)
            {
                failure = $"{retainedSingleCount} retained scene descriptors claim to be the Single base.";
                return false;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.isOriginalScene)
                    continue;

                if (candidate.physicsMode != LocalPhysicsMode.None)
                {
                    failure = $"the original build scene {candidate.id} uses " +
                              $"LocalPhysicsMode.{candidate.physicsMode}.";
                    return false;
                }

                baseScene = candidate.id;
                return true;
            }

            failure = "no retained Single scene or loaded original build scene was found.";
            return false;
        }

        internal static List<PromotionSceneCandidate> OrderPromotionSceneCandidates(
            IReadOnlyList<PromotionSceneCandidate> candidates, SceneID baseScene)
        {
            var result = new List<PromotionSceneCandidate>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id == baseScene)
                {
                    result.Add(candidates[i]);
                    break;
                }
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id != baseScene)
                    result.Add(candidates[i]);
            }

            return result;
        }

        internal static bool TrySelectPromotionActiveScene(
            IReadOnlyList<PromotionSceneCandidate> candidates,
            SceneID baseScene,
            SceneID? retainedActiveScene,
            out SceneID activeScene,
            out bool usedBaseFallback,
            out string failure)
        {
            activeScene = default;
            usedBaseFallback = false;
            failure = null;

            var retainedActiveMatches = 0;
            if (retainedActiveScene.HasValue)
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].id == retainedActiveScene.Value)
                        retainedActiveMatches++;
                }

                if (retainedActiveMatches == 1)
                {
                    activeScene = retainedActiveScene.Value;
                    return true;
                }

                if (retainedActiveMatches > 1)
                {
                    failure = $"retained active SceneID {retainedActiveScene.Value} has " +
                              $"{retainedActiveMatches} stable descriptors";
                    return false;
                }
            }

            var baseMatches = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id == baseScene)
                    baseMatches++;
            }

            if (baseMatches != 1)
            {
                failure = $"selected promotion base SceneID {baseScene} has " +
                          $"{baseMatches} stable descriptors";
                return false;
            }

            activeScene = baseScene;
            usedBaseFallback = true;
            return true;
        }

        internal static PurrSceneSettings GetPromotionManifestSettings(
            PurrSceneSettings retained, bool exactMigration, bool isPromotionBase)
        {
            if (exactMigration)
            {
                retained.mode = isPromotionBase
                    ? LoadSceneMode.Single
                    : LoadSceneMode.Additive;
            }

            return retained;
        }

        private void AddLoadedBuildSceneToHistory(
            SceneID id, bool exactMigration, bool isPromotionBase)
        {
            if (!_scenes.TryGetValue(id, out var state) ||
                !state.scene.IsValid() || !state.scene.isLoaded ||
                state.scene.buildIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Cannot describe promotion SceneID {id} as a loaded build scene.");
            }

            _sceneActionScenes.Add(id);
            var scenePathHash = ScenePathHashFromBuildIndex(state.scene.buildIndex);
            if (exactMigration && IsBuildScenePathHashAmbiguous(scenePathHash))
            {
                throw new InvalidOperationException(
                    $"Cannot promote because build scene '{state.scene.path}' uses path hash " +
                    $"'{scenePathHash}', which is shared by more than one build-settings scene.");
            }

            var effectiveSettings = state.settings;
            if (exactMigration)
            {
                if (!TryGetPhysicalLocalPhysicsMode(
                        state.scene, out var physicalMode, out var physicsFailure))
                {
                    throw new InvalidOperationException(
                        $"Cannot describe promotion SceneID {id}: {physicsFailure}");
                }

                effectiveSettings.physicsMode = physicalMode;
                _scenes[id] = new SceneState(state.scene, effectiveSettings);
            }

            var manifestSettings = GetPromotionManifestSettings(
                effectiveSettings, exactMigration, isPromotionBase);

            _history.AddLoadAction(new LoadSceneAction
            {
                scenePathHash = scenePathHash,
                sceneID = id,
                parameters = manifestSettings
            });
        }

        internal static bool TryValidateExactSceneManifestUniqueness(
            IReadOnlyList<SceneAction> actions,
            out string failure)
        {
            if (actions == null)
            {
                failure = "the scene manifest is null.";
                return false;
            }

            var sceneIds = new HashSet<SceneID>();
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                SceneID sceneId;

                switch (action.type)
                {
                    case SceneActionType.Load:
                    {
                        var load = action.loadSceneAction;
                        sceneId = load.sceneID;
                        break;
                    }
                    case SceneActionType.LoadAddressable:
                    {
                        var load = action.loadAddressableSceneAction;
                        sceneId = load.sceneID;
                        var guid = load.guid.value;
                        if (string.IsNullOrEmpty(guid))
                        {
                            failure = $"Addressable SceneID {sceneId} has an empty GUID.";
                            return false;
                        }

                        break;
                    }
                    default:
                        continue;
                }

                if (!sceneIds.Add(sceneId))
                {
                    failure = $"SceneID {sceneId} is described more than once.";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        internal static bool TryValidateExactSceneManifestShape(
            IReadOnlyList<SceneAction> actions,
            out string failure)
        {
            if (!TryValidateExactSceneManifestUniqueness(actions, out failure))
                return false;

            if (actions.Count == 0)
            {
                failure = "the scene manifest is empty and has no authoritative Single base scene.";
                return false;
            }

            var loadedSceneIds = new HashSet<SceneID>();
            var loadCount = 0;
            var sawSetActive = false;

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                LoadSceneMode mode;
                LocalPhysicsMode physicsMode;
                SceneID sceneId;
                switch (action.type)
                {
                    case SceneActionType.Load:
                        mode = action.loadSceneAction.parameters.mode;
                        physicsMode = action.loadSceneAction.parameters.physicsMode;
                        sceneId = action.loadSceneAction.sceneID;
                        break;
                    case SceneActionType.LoadAddressable:
                        mode = action.loadAddressableSceneAction.parameters.mode;
                        physicsMode = action.loadAddressableSceneAction.parameters.physicsMode;
                        sceneId = action.loadAddressableSceneAction.sceneID;
                        break;
                    case SceneActionType.SetActive:
                        if (sawSetActive)
                        {
                            failure = "the initial scene manifest contains more than one SetActive action.";
                            return false;
                        }

                        if (i != actions.Count - 1)
                        {
                            failure = "SetActive must be the final action in the initial scene manifest.";
                            return false;
                        }

                        if (!loadedSceneIds.Contains(action.setActiveSceneAction.sceneID))
                        {
                            failure = $"SetActive targets SceneID {action.setActiveSceneAction.sceneID}, " +
                                      "which is not described by the manifest.";
                            return false;
                        }

                        sawSetActive = true;
                        continue;
                    default:
                        failure = $"the initial scene manifest contains unsupported action '{action.type}'.";
                        return false;
                }

                if (sawSetActive)
                {
                    failure = "a scene load appears after the final SetActive action.";
                    return false;
                }

                if (loadCount == 0 && mode != LoadSceneMode.Single)
                {
                    failure = "the first scene descriptor is not the authoritative Single base scene.";
                    return false;
                }

                if (loadCount == 0 && physicsMode != LocalPhysicsMode.None)
                {
                    failure = "the authoritative Single base scene cannot use a local physics scene.";
                    return false;
                }

                if (loadCount > 0 && mode != LoadSceneMode.Additive)
                {
                    failure = "a Single-mode load appears after the authoritative base scene.";
                    return false;
                }

                loadedSceneIds.Add(sceneId);
                loadCount++;
            }

            if (loadCount == 0)
            {
                failure = "the scene manifest has no authoritative Single base scene.";
                return false;
            }

            failure = null;
            return true;
        }

        internal static bool TryNormalizeExactSceneManifestForPlayer(
            List<SceneAction> actions,
            out string failure)
        {
            if (!TryValidateExactSceneManifestUniqueness(actions, out failure))
                return false;

            if (actions.Count == 0)
            {
                failure = "the filtered scene manifest is empty.";
                return false;
            }

            var loads = new List<SceneAction>(actions.Count);
            var loadIds = new HashSet<SceneID>();
            var selectedBaseIndex = -1;
            var singleCount = 0;
            var hasSetActive = false;
            var setActive = default(SceneAction);

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                SceneID sceneId;
                PurrSceneSettings settings;
                switch (action.type)
                {
                    case SceneActionType.Load:
                        sceneId = action.loadSceneAction.sceneID;
                        settings = action.loadSceneAction.parameters;
                        break;
                    case SceneActionType.LoadAddressable:
                        sceneId = action.loadAddressableSceneAction.sceneID;
                        settings = action.loadAddressableSceneAction.parameters;
                        break;
                    case SceneActionType.SetActive:
                        if (hasSetActive || i != actions.Count - 1)
                        {
                            failure = "the filtered manifest has an invalid SetActive action.";
                            return false;
                        }

                        hasSetActive = true;
                        setActive = action;
                        continue;
                    default:
                        failure = $"the filtered manifest contains unsupported action '{action.type}'.";
                        return false;
                }

                if (settings.mode == LoadSceneMode.Single)
                {
                    singleCount++;
                    selectedBaseIndex = loads.Count;
                }

                loadIds.Add(sceneId);
                loads.Add(action);
            }

            if (singleCount > 1)
            {
                failure = "the filtered manifest contains more than one Single base scene.";
                return false;
            }

            if (selectedBaseIndex < 0)
            {
                for (var i = 0; i < loads.Count; i++)
                {
                    var physicsMode = loads[i].type == SceneActionType.Load
                        ? loads[i].loadSceneAction.parameters.physicsMode
                        : loads[i].loadAddressableSceneAction.parameters.physicsMode;
                    if (physicsMode == LocalPhysicsMode.None)
                    {
                        selectedBaseIndex = i;
                        break;
                    }
                }
            }

            if (selectedBaseIndex < 0)
            {
                failure = "the filtered manifest has no scene eligible to be its Single base; " +
                          "all retained targets use local physics.";
                return false;
            }

            var selectedBasePhysics = loads[selectedBaseIndex].type == SceneActionType.Load
                ? loads[selectedBaseIndex].loadSceneAction.parameters.physicsMode
                : loads[selectedBaseIndex].loadAddressableSceneAction.parameters.physicsMode;
            if (selectedBasePhysics != LocalPhysicsMode.None)
            {
                failure = "the filtered manifest's Single base uses a local physics scene.";
                return false;
            }

            if (hasSetActive && !loadIds.Contains(setActive.setActiveSceneAction.sceneID))
            {
                failure = $"SetActive targets filtered SceneID {setActive.setActiveSceneAction.sceneID}, " +
                          "which has no retained load descriptor.";
                return false;
            }

            if (!hasSetActive)
            {
                var selectedBase = loads[selectedBaseIndex];
                var selectedBaseId = selectedBase.type == SceneActionType.Load
                    ? selectedBase.loadSceneAction.sceneID
                    : selectedBase.loadAddressableSceneAction.sceneID;
                setActive = new SceneAction
                {
                    type = SceneActionType.SetActive,
                    setActiveSceneAction = new SetActiveSceneAction
                    {
                        sceneID = selectedBaseId
                    }
                };
                hasSetActive = true;
            }

            var normalized = new List<SceneAction>(loads.Count + 1);
            for (var outputIndex = 0; outputIndex < loads.Count; outputIndex++)
            {
                var sourceIndex = outputIndex == 0
                    ? selectedBaseIndex
                    : outputIndex <= selectedBaseIndex ? outputIndex - 1 : outputIndex;
                var action = loads[sourceIndex];
                if (action.type == SceneActionType.Load)
                {
                    var load = action.loadSceneAction;
                    load.parameters.mode = outputIndex == 0
                        ? LoadSceneMode.Single
                        : LoadSceneMode.Additive;
                    action.loadSceneAction = load;
                }
                else
                {
                    var load = action.loadAddressableSceneAction;
                    load.parameters.mode = outputIndex == 0
                        ? LoadSceneMode.Single
                        : LoadSceneMode.Additive;
                    action.loadAddressableSceneAction = load;
                }

                normalized.Add(action);
            }

            normalized.Add(setActive);

            if (!TryValidateExactSceneManifestShape(normalized, out failure))
                return false;

            actions.Clear();
            actions.AddRange(normalized);
            return true;
        }

        internal static bool RequiresStaleKeptSceneHierarchyRetirement(
            bool isAuthoritativeTarget,
            bool keepPhysicalScene)
        {
            return !isAuthoritativeTarget && keepPhysicalScene;
        }

        private bool _isTransferingToNewServer;
        private bool _requiresTransferReconciliation;
        private Exception _transferReconciliationFailure;
        private List<SceneAction> _deferredExactTransferManifest;
        private List<SceneID> _deferredRetainedSceneRebounds;
        private List<SceneAction> _deferredExactIncrementalActions;
        private bool _exactTransferBaselineReceived;
        private bool _isCommittingExactTransferManifest;
        private int _exactActiveSceneCommitFrame = -1;
        private bool _exactPromotedListenSceneBoundaryActive;
        private bool _exactStructuralSceneCommitStarted;
        private HashSet<SceneID> _deferredStagedExactSceneLoads;
        private readonly Dictionary<SceneID, SceneState> _stagedExactScenes =
            new Dictionary<SceneID, SceneState>();
        private readonly HashSet<SceneID> _stagedExactRetainedSceneAdoptions =
            new HashSet<SceneID>();
        private readonly HashSet<SceneID> _bestEffortPhysicsFallbackWarnings =
            new HashSet<SceneID>();
        private bool _isRetiringStagedExactScenes;

        internal void DriveTransferReconciliation()
        {
            PollTransferReconciliationOperations();
            TryCommitDeferredExactTransferManifest();
        }

        internal bool isTransferReconciliationComplete
        {
            get
            {
                return _transferReconciliationFailure == null &&
                       !_isTransferingToNewServer &&
                       _deferredExactTransferManifest == null &&
                       _stagedExactScenes.Count == 0 &&
                       _actionsQueue.Count == 0 &&
                       _pendingOperations.Count == 0 &&
                       _pendingSceneUnloads.Count == 0
#if ADDRESSABLES_PURRNET_SUPPORT
                       && _pendingAddressableOperations.Count == 0 &&
                       _pendingAddressableUnloads.Count == 0
#endif
                    ;
            }
        }

        internal bool TryGetTransferReconciliationFailure(out Exception failure)
        {
            failure = _transferReconciliationFailure;
            return failure != null;
        }

        private void FailTransferReconciliation(string message, Exception innerException = null)
        {
            if (!_requiresTransferReconciliation || _transferReconciliationFailure != null)
                return;

            _transferReconciliationFailure = innerException == null
                ? new InvalidOperationException(message)
                : new InvalidOperationException(message, innerException);
            PurrLogger.LogError(message);
            if (!_exactStructuralSceneCommitStarted)
            {
                RetireAllStagedExactScenes();
            }
            else
            {
                PublishStagedExactScenesAfterStructuralCommit();
            }
        }

        private void WarnBestEffortPhysicsFallback(
            SceneID id,
            Scene scene,
            LocalPhysicsMode authoritativeMode,
            LocalPhysicsMode localMode)
        {
            if (!_bestEffortPhysicsFallbackWarnings.Add(id))
                return;

            PurrLogger.LogWarning(
                $"Retaining Unity scene '{scene.name}' for SceneID {id} with local " +
                $"LocalPhysicsMode.{localMode}; the new authority advertised " +
                $"LocalPhysicsMode.{authoritativeMode}. Scene instances and network state will " +
                "reconcile in place, but this peer's immutable physics world remains a best-effort fallback.");
        }

        private void PublishStagedExactScenesAfterStructuralCommit()
        {
            if (_stagedExactScenes.Count == 0)
                return;

            var ids = new List<SceneID>(_stagedExactScenes.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
#if ADDRESSABLES_PURRNET_SUPPORT
                CommitStagedExactAddressableScene(ids[i]);
#endif
            }
            _stagedExactScenes.Clear();
            _stagedExactRetainedSceneAdoptions.Clear();
        }

        private bool TryStageExactScene(
            Scene scene,
            PurrSceneSettings settings,
            SceneID id,
            out string failure,
            bool preserveLoadedSceneOnRollback = false)
        {
            failure = null;
            if (!_requiresTransferReconciliation || _deferredExactTransferManifest == null)
            {
                failure = "No exact scene manifest is accepting staged Additive loads.";
                return false;
            }

            if (!scene.IsValid() || !scene.isLoaded ||
                IsSceneUnloading(scene))
            {
                failure = $"Authoritative SceneID {id} did not produce a stable loaded Unity scene.";
                return false;
            }

            if (!ArePhysicalSceneSettingsCompatible(scene, settings, out var physicsFailure))
            {
                failure = $"Authoritative SceneID {id} loaded with incompatible physical topology: " +
                          physicsFailure;
                return false;
            }

            if (_stagedExactScenes.ContainsKey(id) || _scenes.ContainsKey(id) ||
                _idToScene.ContainsKey(scene))
            {
                failure = $"Authoritative SceneID {id} or Unity scene '{scene.name}' was already registered.";
                return false;
            }

            foreach (var pair in _stagedExactScenes)
            {
                if (pair.Value.scene.handle != scene.handle)
                    continue;

                failure = $"Unity scene '{scene.name}' was staged for both SceneID {pair.Key} and {id}.";
                return false;
            }

            if (!TryAddSceneRegistration(scene, settings, id))
            {
                failure = $"Authoritative SceneID {id} could not be registered transactionally.";
                return false;
            }

            _stagedExactScenes.Add(id, new SceneState(scene, settings));
            if (preserveLoadedSceneOnRollback)
                _stagedExactRetainedSceneAdoptions.Add(id);
            try
            {
                InvokeCoreSceneRegistrationAdded(id);
                return true;
            }
            catch (Exception e)
            {
                failure = $"Core scene registration failed for staged SceneID {id}: {e.Message}";
                return false;
            }
        }

        private void RetireAllStagedExactScenes()
        {
            if (_isRetiringStagedExactScenes)
                return;

            _isRetiringStagedExactScenes = true;
            try
            {
                var stagedBindings = new List<KeyValuePair<SceneID, SceneState>>(
                    _stagedExactScenes);
                var scenes = new List<Scene>(stagedBindings.Count);
                for (var i = 0; i < stagedBindings.Count; i++)
                    scenes.Add(stagedBindings[i].Value.scene);

                var detached = false;
                try
                {
                    detached = TryDetachRetainedPhysicalSceneRegistrations(
                        scenes, out var detachFailure);
                    if (!detached)
                    {
                        PurrLogger.LogError(
                            "Could not roll back staged exact scene registrations: " + detachFailure);
                    }
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }

                if (!detached)
                    return;

                var addressableScenes = new HashSet<SceneID>();
                UnregisterStagedExactAddressableMetadata(addressableScenes);

                for (var i = 0; i < stagedBindings.Count; i++)
                {
                    var id = stagedBindings[i].Key;
                    if (!IsExactScenePoolSharedWithOppositeRole(id))
                        NetworkPoolManager.RemovePool(
                            _networkManager, stagedBindings[i].Value.scene, id);
                }

                for (var i = 0; i < stagedBindings.Count; i++)
                {
                    var binding = stagedBindings[i];
                    _sceneActionScenes.Remove(binding.Key);
                    if (addressableScenes.Contains(binding.Key) ||
                        _stagedExactRetainedSceneAdoptions.Contains(binding.Key))
                        continue;

                    var scene = binding.Value.scene;
                    if (!scene.IsValid() || !scene.isLoaded ||
                        ShouldKeepLocalSceneDuringTransfer(scene))
                        continue;

                    TrackSceneUnload(scene, SceneManager.UnloadSceneAsync(scene),
                        $"roll back unpublished exact SceneID {binding.Key}");
                }

                RetireStagedExactAddressableScenes();
                _stagedExactScenes.Clear();
                _stagedExactRetainedSceneAdoptions.Clear();
            }
            finally
            {
                _isRetiringStagedExactScenes = false;
            }
        }

        private bool IsExactScenePoolSharedWithOppositeRole(SceneID id)
        {
            return _networkManager.TryGetModule<HierarchyFactory>(!_asServer, out var factory) &&
                   factory.TryGetHierarchy(id, out _);
        }

        private void PollTransferReconciliationOperations()
        {
            for (var i = _pendingSceneUnloads.Count - 1; i >= 0; i--)
            {
                var pending = _pendingSceneUnloads[i];
                if (pending.operation != null && !pending.operation.isDone)
                    continue;

                _pendingSceneUnloads.RemoveAt(i);
            }

            PollCompletedAddressableUnloads();
        }

        private AsyncOperation TrackSceneUnload(Scene scene, AsyncOperation operation, string context)
        {
            if (operation == null)
            {
                FailTransferReconciliation($"Unity did not start the required scene unload ({context}).");
                return null;
            }

            if (!operation.isDone)
            {
                _pendingSceneUnloads.Add(new PendingSceneUnload
                {
                    scene = scene,
                    operation = operation
                });
            }

            return operation;
        }

        private bool IsSceneUnloading(Scene scene)
        {
            PollTransferReconciliationOperations();
            for (var i = 0; i < _pendingSceneUnloads.Count; i++)
            {
                if (_pendingSceneUnloads[i].scene.handle == scene.handle)
                    return true;
            }

            return false;
        }

        internal static bool AreLoadedSceneSettingsCompatible(
            PurrSceneSettings retained,
            PurrSceneSettings authoritative)
        {
            return retained.physicsMode == authoritative.physicsMode;
        }

        internal static bool TryReconcileLoadedSceneSettings(
            Scene scene,
            PurrSceneSettings retained,
            PurrSceneSettings authoritative,
            out PurrSceneSettings reconciled,
            out bool repairedRetainedMetadata,
            out bool usedLocalPhysicsFallback,
            out string failure)
        {
            reconciled = authoritative;
            repairedRetainedMetadata = false;
            usedLocalPhysicsFallback = false;
            if (!TryGetPhysicalLocalPhysicsMode(scene, out var physicalMode, out failure))
                return false;

            repairedRetainedMetadata = retained.physicsMode != physicalMode;
            usedLocalPhysicsFallback = authoritative.physicsMode != physicalMode;
            reconciled.physicsMode = physicalMode;
            failure = null;
            return true;
        }

        internal static bool IsLoadedTargetSelectionAmbiguous(
            int loadedMatchCount,
            bool hasStableAuthoritativeBinding)
        {
            return loadedMatchCount > 1 && !hasStableAuthoritativeBinding;
        }

        internal static SceneAction NormalizeExactMissingLoadForStaging(SceneAction action)
        {
            switch (action.type)
            {
                case SceneActionType.Load:
                {
                    var load = action.loadSceneAction;
                    load.parameters.mode = LoadSceneMode.Additive;
                    action.loadSceneAction = load;
                    break;
                }
                case SceneActionType.LoadAddressable:
                {
                    var load = action.loadAddressableSceneAction;
                    load.parameters.mode = LoadSceneMode.Additive;
                    action.loadAddressableSceneAction = load;
                    break;
                }
            }

            return action;
        }

        internal static bool TryGetPhysicalLocalPhysicsMode(
            Scene scene,
            out LocalPhysicsMode mode,
            out string failure)
        {
            mode = LocalPhysicsMode.None;
            failure = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                failure = "the Unity scene is invalid or not loaded.";
                return false;
            }

            try
            {
                var hasLocal3D = false;
                var hasLocal2D = false;
#if UNITY_PHYSICS_3D
                var physics3D = scene.GetPhysicsScene();
                if (!physics3D.IsValid())
                {
                    failure = "Unity returned an invalid 3D physics scene.";
                    return false;
                }
                hasLocal3D = physics3D != Physics.defaultPhysicsScene;
#endif

#if UNITY_PHYSICS_2D
                var physics2D = scene.GetPhysicsScene2D();
                if (!physics2D.IsValid())
                {
                    failure = "Unity returned an invalid 2D physics scene.";
                    return false;
                }
                hasLocal2D = physics2D != Physics2D.defaultPhysicsScene;
#endif
                mode = LocalPhysicsMode.None;
                if (hasLocal2D)
                    mode |= LocalPhysicsMode.Physics2D;
                if (hasLocal3D)
                    mode |= LocalPhysicsMode.Physics3D;
                return true;
            }
            catch (Exception e)
            {
                failure = $"Unity could not inspect the scene's physical world: {e.Message}";
                return false;
            }
        }

        internal static bool ArePhysicalSceneSettingsCompatible(
            Scene scene,
            PurrSceneSettings settings,
            out string failure)
        {
            if (!TryGetPhysicalLocalPhysicsMode(scene, out var physicalMode, out failure))
                return false;

            if (physicalMode == settings.physicsMode)
                return true;

            failure = $"stored LocalPhysicsMode.{settings.physicsMode} does not match " +
                      $"Unity's LocalPhysicsMode.{physicalMode}.";
            return false;
        }

        internal static bool ShouldRejectLoadedTargetReplacement(
            bool requiresExactReconciliation,
            bool targetIsLoaded,
            bool stableIdentityMatches,
            bool immutableSettingsMatch,
            bool topologyIsStable)
        {
            return requiresExactReconciliation && targetIsLoaded &&
                   (!stableIdentityMatches || !immutableSettingsMatch || !topologyIsStable);
        }

        internal static bool ArePendingSceneSettingsCompatible(
            PurrSceneSettings pending,
            PurrSceneSettings authoritative)
        {
            return pending.mode == authoritative.mode &&
                   pending.physicsMode == authoritative.physicsMode;
        }

        internal static bool IsExactSceneDescriptorIdentityMatch(
            SceneID retainedId,
            SceneID authoritativeId,
            bool retainedIsAddressable,
            bool authoritativeIsAddressable)
        {
            return retainedId == authoritativeId &&
                   retainedIsAddressable == authoritativeIsAddressable;
        }

        public void PostPromoteToServerModule()
        {
            if (!_rebuildHistoryOnNextPlayerJoin)
                return;

            _rebuildHistoryOnNextPlayerJoin = false;
            RebuildHistoryFromLoadedBuildScenes();
        }

        public void TransferToNewServer()
        {
            _isTransferingToNewServer = true;
            _requiresTransferReconciliation =
                _networkManager.expectedHostMigrationSession.canReconcile;
            _transferReconciliationFailure = null;
            _deferredExactTransferManifest = null;
            _deferredRetainedSceneRebounds = null;
            _deferredExactIncrementalActions = _requiresTransferReconciliation
                ? new List<SceneAction>()
                : null;
            _exactTransferBaselineReceived = false;
            _isCommittingExactTransferManifest = false;
            _exactActiveSceneCommitFrame = -1;
            _exactPromotedListenSceneBoundaryActive = ShouldUseExactPromotedListenSetup(
                _asServer,
                _networkManager.isPromotingToServer,
                _networkManager.isServer,
                _networkManager.expectedHostMigrationSession);
            _exactStructuralSceneCommitStarted = false;
            _deferredStagedExactSceneLoads = null;
            _preparedRetainedSceneRebounds.Clear();
            _retainedSceneRebounds.Clear();
            _bestEffortPhysicsFallbackWarnings.Clear();

            if (_requiresTransferReconciliation && _stagedExactScenes.Count > 0)
            {
                FailTransferReconciliation(
                    "Cannot begin exact scene reconciliation with unpublished staged scenes from a prior transaction.");
                return;
            }

            if (_requiresTransferReconciliation && HasUnsettledExactTransferSceneOperations())
            {
                FailTransferReconciliation(
                    "Cannot begin exact scene reconciliation while an old-authority scene " +
                    "action, load, or unload is still in flight.");
                _actionsQueue.Clear();
                return;
            }

            PollTransferReconciliationOperations();
        }

        public void PostTransferToNewServer()
        {
            var incrementalActions = _deferredExactIncrementalActions;
            _requiresTransferReconciliation = false;
            _transferReconciliationFailure = null;
            _deferredExactTransferManifest = null;
            _deferredRetainedSceneRebounds = null;
            _deferredExactIncrementalActions = null;
            _exactTransferBaselineReceived = false;
            _isCommittingExactTransferManifest = false;
            _exactActiveSceneCommitFrame = -1;
            _exactPromotedListenSceneBoundaryActive = false;
            _exactStructuralSceneCommitStarted = false;
            _deferredStagedExactSceneLoads = null;
            _preparedRetainedSceneRebounds.Clear();

            if (incrementalActions != null && incrementalActions.Count > 0)
                HandleScenes(incrementalActions);

        }

        private bool HasExactPromotedListenClientSceneBoundary()
        {
            if (!_asServer || !_networkManager.isHost || !_networkManager.isServer)
                return false;

            var clientScenes = _networkManager.GetModule<ScenesModule>(false);
            return clientScenes != null && clientScenes._requiresTransferReconciliation &&
                   clientScenes._exactPromotedListenSceneBoundaryActive;
        }

        private void ThrowIfExactPromotedListenSceneMutationIsFenced(string operation)
        {
            if (!HasExactPromotedListenClientSceneBoundary())
                return;

            throw new InvalidOperationException(
                $"Cannot {operation} while the promoted listen-client role is reconciling its exact " +
                "scene baseline. Await the promotion operation before mutating shared Unity scenes.");
        }

        private void Setup(bool asServer)
        {
            _wasSetup = true;
            _asServer = asServer;

            if (ShouldUseExactPromotedListenSetup(
                    asServer,
                    _networkManager.isPromotingToServer,
                    _networkManager.isServer,
                    _networkManager.expectedHostMigrationSession))
            {
                _requiresTransferReconciliation = true;
                _transferReconciliationFailure = null;
                _exactPromotedListenSceneBoundaryActive = true;
                SetupExactPromotedListenClientScenes();
                SubscribeSceneRole(false);
                SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
                return;
            }

            var currentScene = _networkManager.gameObject.scene;
            var originalScene = _networkManager.originalScene;

            var hasDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(currentScene) ||
                                          IsDontDestroyOnLoadScene(originalScene);

            AddScene(currentScene, new PurrSceneSettings
            {
                mode = LoadSceneMode.Single,
                isPublic = true,
                physicsMode = LocalPhysicsMode.None
            }, GetNextID());

            if (currentScene != originalScene && originalScene.IsValid())
            {
                AddScene(originalScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None
                }, GetNextID());
            }

            var rules = _networkManager.networkRules;

            if (!hasDontDestroyOnLoadScene && rules && rules.ShouldAlwaysIncludeDontDestroyOnLoadScene())
            {
                var dontDestroyScene = GetDontDestroyOnLoadScene();
                AddScene(dontDestroyScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None
                }, GetNextID());
            }

            if (!asServer)
                MirrorAlreadyLoadedHostScenes();

            SubscribeSceneRole(asServer);

            SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
        }

        internal static bool ShouldUseExactPromotedListenSetup(
            bool asServer,
            bool isPromotingToServer,
            bool isServer,
            HostMigrationTransitionOptions expectedTransition) =>
            !asServer && isPromotingToServer && isServer && expectedTransition.canReconcile;

        internal bool TryValidateExactAuthoritySwitchPreflight(
            bool promotion,
            out string failure)
        {
            failure = null;
            if (_actionsQueue.Count > 0 || _scenesToTriggerUnloadEvent.Count > 0 ||
                _pendingOperations.Count > 0 || _pendingSceneUnloads.Count > 0 ||
                _history.hasUnflushedActions || _deferredExactTransferManifest != null ||
                _deferredExactIncrementalActions != null || _isCommittingExactTransferManifest)
            {
                failure = "An ordinary scene transaction or deferred lifecycle action is still pending.";
                return false;
            }

            if (_stagedExactScenes.Count > 0)
            {
                failure = "An unpublished exact Additive scene registration is still staged.";
                return false;
            }

            ValidateExactAddressableAuthoritySwitchState(ref failure);
            if (failure != null)
                return false;

            var registeredIds = new HashSet<SceneID>();
            var physicalScenes = new HashSet<Scene>();
            var candidates = promotion ? new List<PromotionSceneCandidate>() : null;
            var originalScene = _networkManager.originalScene;

            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var id = _rawScenes[i];
                if (!registeredIds.Add(id) || !_scenes.TryGetValue(id, out var state) ||
                    !_idToScene.TryGetValue(state.scene, out var reverseId) || reverseId != id)
                {
                    failure = $"The retained scene registry is not one-to-one at SceneID {id}.";
                    return false;
                }

                if (!state.scene.IsValid() || !state.scene.isLoaded ||
                    !physicalScenes.Add(state.scene))
                {
                    failure = $"Retained SceneID {id} has invalid, unloaded, or duplicate physical topology.";
                    return false;
                }

                if (!TryGetPhysicalLocalPhysicsMode(
                        state.scene, out var physicalMode, out var physicsFailure))
                {
                    failure = $"Retained SceneID {id} has uninspectable physical topology: " +
                              physicsFailure;
                    return false;
                }

                var isAddressable = false;
                ValidateExactAddressableAuthoritySwitchScene(
                    id, ref isAddressable, ref failure);
                if (failure != null)
                    return false;

                if (isAddressable)
                {
                    if (promotion)
                    {
                        candidates.Add(new PromotionSceneCandidate(
                            id, state.settings.mode, physicalMode,
                            false, true));
                    }
                    continue;
                }

                if (state.scene.buildIndex >= 0)
                {
                    var scenePathHash = ScenePathHashFromBuildIndex(state.scene.buildIndex);
                    if (IsBuildScenePathHashAmbiguous(scenePathHash))
                    {
                        failure = $"Retained build SceneID {id} has a scene-path hash that maps to " +
                                  "more than one build-settings entry.";
                        return false;
                    }

                    if (promotion)
                    {
                        candidates.Add(new PromotionSceneCandidate(
                            id, state.settings.mode, physicalMode,
                            originalScene.IsValid() &&
                            state.scene.handle == originalScene.handle));
                    }
                    continue;
                }

                string sceneRetirementFailure = null;
                if (!_networkManager.TryGetModule<HierarchyFactory>(false, out var hierarchyFactory) ||
                    !hierarchyFactory.TryPreflightExactStaleSceneRetirement(
                        id, state.scene, false, out sceneRetirementFailure))
                {
                    failure = $"Descriptorless retained SceneID {id} cannot retire its client " +
                              $"hierarchy in place: " +
                              (sceneRetirementFailure ?? "the client hierarchy factory is unavailable");
                    return false;
                }
            }

            if (registeredIds.Count != _scenes.Count || registeredIds.Count != _idToScene.Count)
            {
                failure = "The retained scene registry contains an unindexed SceneID or Unity scene.";
                return false;
            }

            foreach (var actionScene in _sceneActionScenes)
            {
                if (registeredIds.Contains(actionScene))
                    continue;

                failure = $"Scene action history references unregistered SceneID {actionScene}.";
                return false;
            }

            if (!promotion)
                return true;

            if (!TrySelectPromotionBaseScene(candidates, out var selectedBase, out var baseFailure))
            {
                failure = "The promoted scene manifest has no trustworthy Single base: " + baseFailure;
                return false;
            }

            var activeScene = SceneManager.GetActiveScene();
            SceneID? retainedActive = null;
            if (activeScene.IsValid() && activeScene.isLoaded &&
                _idToScene.TryGetValue(activeScene, out var activeId))
                retainedActive = activeId;

            return TrySelectPromotionActiveScene(
                candidates, selectedBase, retainedActive,
                out _, out _, out failure);
        }

        private void SubscribeSceneRole(bool asServer)
        {
            if (!asServer)
            {
                _players.Subscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.Subscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            }
            else
            {
                _players.onPrePlayerJoined += OnPlayerJoined;
                _players.onPreHostMigrationConnectionRebound += OnHostMigrationConnectionRebound;
                _scenePlayers.onPlayerJoinedScene += OnPlayerJoinedScene;
                _scenePlayers.onPlayerLeftScene += OnPlayerLeftScene;
            }
        }

        private void SetupExactPromotedListenClientScenes()
        {
            if (!_networkManager.TryGetModule(out ScenesModule serverModule, true))
            {
                FailTransferReconciliation(
                    "The promoted server scene module was unavailable while binding its exact listen client.");
                return;
            }

            if (!TryBuildPromotedListenSceneBindingPlan(
                    serverModule, out var bindings, out var failure))
            {
                FailTransferReconciliation(failure);
                return;
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var id = binding.id;
                _scenes.Add(id, binding.state);
                _idToScene.Add(binding.state.scene, id);
                _rawScenes.Add(id);
                _sceneActionScenes.Add(id);
                CopyPromotedListenAddressableMetadata(serverModule, id);

                var nextSceneId = (int)id.id + 1;
                if (nextSceneId > _nextSceneID && nextSceneId <= ushort.MaxValue)
                    _nextSceneID = (ushort)nextSceneId;
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                if (!PrepareRetainedSceneReboundForScene(bindings[i].id))
                    return;
            }
        }

        internal bool TryBuildPromotedListenSceneBindingPlan(
            ScenesModule serverModule,
            out List<PromotedListenSceneBinding> bindings,
            out string failure)
        {
            bindings = new List<PromotedListenSceneBinding>(serverModule._rawScenes.Count);
            failure = null;

            if (_scenes.Count != 0 || _idToScene.Count != 0 || _rawScenes.Count != 0 ||
                _sceneActionScenes.Count != 0)
            {
                failure = "The exact promoted listen-client scene role was not empty before binding.";
                return false;
            }

            var sourceIds = new HashSet<SceneID>();
            var physicalScenes = new HashSet<Scene>();
            for (var i = 0; i < serverModule._rawScenes.Count; i++)
            {
                var id = serverModule._rawScenes[i];
                if (!sourceIds.Add(id))
                {
                    failure = $"The promoted server scene registry repeats SceneID {id}.";
                    return false;
                }

                if (!serverModule._sceneActionScenes.Contains(id) ||
                    !serverModule._scenes.TryGetValue(id, out var state) ||
                    !serverModule._idToScene.TryGetValue(state.scene, out var reverseId) ||
                    reverseId != id)
                {
                    failure = $"The promoted server has an incomplete manifest binding for SceneID {id}.";
                    return false;
                }

                if (!state.scene.IsValid() || !state.scene.isLoaded)
                {
                    failure = $"The promoted server manifest contains unloaded SceneID {id}.";
                    return false;
                }

                if (!ArePhysicalSceneSettingsCompatible(
                        state.scene, state.settings, out var physicsFailure))
                {
                    failure = $"Promoted SceneID {id} has incompatible physical topology: " +
                              physicsFailure;
                    return false;
                }

                if (!physicalScenes.Add(state.scene))
                {
                    failure = $"The promoted server manifest claims Unity scene '{state.scene.name}' more than once.";
                    return false;
                }

                ValidatePromotedListenAddressableMetadata(
                    serverModule, id, ref failure);
                if (failure != null)
                    return false;

                bindings.Add(new PromotedListenSceneBinding(id, state));
            }

            if (bindings.Count == 0)
            {
                failure = "The promoted server has no authoritative scene bindings.";
                return false;
            }

            if (sourceIds.Count != serverModule._scenes.Count ||
                sourceIds.Count != serverModule._idToScene.Count ||
                sourceIds.Count != serverModule._sceneActionScenes.Count)
            {
                failure = "The promoted server scene registry is not one-to-one with its exact manifest.";
                return false;
            }

            return true;
        }

        private void MirrorAlreadyLoadedHostScenes()
        {
            if (!_networkManager.isServer)
                return;

            if (!_networkManager.TryGetModule<ScenesModule>(true, out var serverModule))
                return;

            foreach (var pair in serverModule.sceneStates)
            {
                var sceneId = pair.Key;
                var state = pair.Value;

                if (!serverModule._sceneActionScenes.Contains(sceneId))
                    continue;

                if (!state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                if (_scenes.ContainsKey(sceneId) || HasScene(state.scene))
                    continue;

                _sceneActionScenes.Add(sceneId);
                AddScene(state.scene, state.settings, sceneId);

                var nextSceneId = (int)sceneId.id + 1;
                if (nextSceneId > _nextSceneID && nextSceneId <= ushort.MaxValue)
                    _nextSceneID = (ushort)nextSceneId;
            }
        }

        public void Enable(bool asServer)
        {
            // Setup(asServer);
        }

        public void Disable(bool asServer)
        {
            if (!asServer)
            {
                _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.Unsubscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            }
            else
            {
                _players.onPrePlayerJoined -= OnPlayerJoined;
                _players.onPreHostMigrationConnectionRebound -= OnHostMigrationConnectionRebound;
                _scenePlayers.onPlayerJoinedScene -= OnPlayerJoinedScene;
                _scenePlayers.onPlayerLeftScene -= OnPlayerLeftScene;
            }

            SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            SendInitialSceneManifest(player, isReconnect, asServer, false);
        }

        private void OnHostMigrationConnectionRebound(
            PlayerID player, bool isReconnect, bool asServer)
        {
            SendInitialSceneManifest(player, isReconnect, asServer, true);
        }

        private void SendInitialSceneManifest(
            PlayerID player, bool isReconnect, bool asServer, bool exactConnectionRebound)
        {
            if (!asServer)
                return;

            if (_rebuildHistoryOnNextPlayerJoin)
            {
                _rebuildHistoryOnNextPlayerJoin = false;
                RebuildHistoryFromLoadedBuildScenes();
            }

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            for (var i = 0; i < history.actions.Count; i++)
            {
                var action = history.actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (ShouldSendSceneActionOnJoin(player, target, isReconnect))
                    _playerFilteredActions.Add(action);
            }

            if (exactConnectionRebound &&
                !TryNormalizeExactSceneManifestForPlayer(
                    _playerFilteredActions, out var normalizationFailure))
            {
                PurrLogger.LogError(
                    $"Cannot reconcile retained player {player}'s scene manifest: " +
                    normalizationFailure);

                _playerFilteredActions.Clear();
            }

            if (exactConnectionRebound && _playerFilteredActions.Count > 0)
            {
                var transition = _networkManager.hostMigrationSession;
                var topologyScenes = GetExactTopologySceneSet(_playerFilteredActions);
                string topologyFailure = null;
                if (!_networkManager.TryGetModule<HierarchyFactory>(true, out var hierarchyFactory) ||
                    !hierarchyFactory.RegisterExactOutboundSceneSet(
                        player, transition, topologyScenes, out topologyFailure))
                {
                    throw new InvalidOperationException(
                        $"Cannot register retained player {player}'s exact hierarchy set: " +
                        (topologyFailure ?? "the server hierarchy factory is unavailable"));
                }
            }

            _players.Send(player, new FirstSceneActionsBatch { actions = _playerFilteredActions });
        }

        private static List<SceneID> GetExactTopologySceneSet(IReadOnlyList<SceneAction> actions)
        {
            var result = new List<SceneID>();
            var unique = new HashSet<SceneID>();
            if (actions == null)
                return result;

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action.type != SceneActionType.Load &&
                    action.type != SceneActionType.LoadAddressable)
                    continue;

                var scene = GetSceneActionId(action);
                if (unique.Add(scene))
                    result.Add(scene);
            }

            return result;
        }

        private bool ShouldSendSceneActionOnJoin(PlayerID player, SceneID target, bool isReconnect)
        {
            if (_scenePlayers.IsPlayerInScene(player, target))
                return true;

            if (!isReconnect)
                return false;

            return _scenes.TryGetValue(target, out var state) && state.settings.isPublic;
        }

        private void OnPlayerLeftScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!asServer)
                return;

            bool isSceneStillValid = _scenes.TryGetValue(scene, out var state) && state.scene.IsValid();

            if (!isSceneStillValid)
                return;

            _playerFilteredActions.Clear();
            _playerFilteredActions.Add(new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction
                {
                    sceneID = scene,
                    options = UnloadSceneOptions.None
                }
            });

            _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        private void OnPlayerJoinedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!asServer)
                return;

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            // send all actions for the scene
            FilterActionsForPlayerBySceneID(player, scene, history.actions, _playerFilteredActions);

            if (_playerFilteredActions.Count > 0)
                _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        /// <summary>
        /// Returns the pending operations for this module.
        /// This allows you to check if a scene is still loading or unloading and the progress of the operation.
        /// </summary>
        /// <returns>List of pending operations</returns>
        public IReadOnlyList<PendingSceneOperation> GetPendingOperations()
        {
            return _pendingOperations;
        }

        private void SceneManagerOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var loadedHash = Hash.Hash(scene.path);
            var matchingIndex = -1;
            var matchingCount = 0;

            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];

                if (operation.scenePathHash == loadedHash && operation.settings.mode == mode)
                {
                    if (matchingIndex == -1)
                        matchingIndex = i;
                    matchingCount++;
                }
            }

            if (matchingIndex == -1)
                return;

            if (_requiresTransferReconciliation && matchingCount != 1)
            {
                FailTransferReconciliation(
                    $"Loaded scene '{scene.path}' matches {matchingCount} pending scene operations; " +
                    "Unity did not provide enough information to assign the authoritative SceneID safely.");
                RetireUnclaimedLoadedScene(scene, matchingIndex,
                    "ambiguous host-migration scene load");
                return;
            }

            var matchingOperation = _pendingOperations[matchingIndex];
            if (_requiresTransferReconciliation && _transferReconciliationFailure != null)
            {
                RetireUnclaimedLoadedScene(scene, matchingIndex,
                    "scene load completed after host-migration reconciliation failed");
                return;
            }

            if (_requiresTransferReconciliation &&
                _scenes.TryGetValue(matchingOperation.idToAssign, out var existing) &&
                existing.scene.handle != scene.handle)
            {
                FailTransferReconciliation(
                    $"Loaded scene '{scene.path}' cannot claim SceneID {matchingOperation.idToAssign}; " +
                    $"that ID is already registered to '{existing.scene.name}'.");
                RetireUnclaimedLoadedScene(scene, matchingIndex,
                    $"SceneID {matchingOperation.idToAssign} collision");
                return;
            }

            if (_requiresTransferReconciliation)
            {
                if (scene.buildIndex != matchingOperation.buildIndex ||
                    Hash.Hash(scene.path) != matchingOperation.scenePathHash)
                {
                    _pendingOperations.RemoveAt(matchingIndex);
                    FailTransferReconciliation(
                        $"Unity loaded the wrong physical build scene for authoritative SceneID " +
                        $"{matchingOperation.idToAssign}.");
                    RetireUnclaimedLoadedScene(scene, -1,
                        $"wrong physical scene for SceneID {matchingOperation.idToAssign}");
                    return;
                }

                if (!TryStageExactScene(
                        scene, matchingOperation.settings, matchingOperation.idToAssign,
                        out var stagingFailure))
                {
                    _pendingOperations.RemoveAt(matchingIndex);
                    FailTransferReconciliation(stagingFailure);
                    RetireUnclaimedLoadedScene(scene, -1,
                        $"failed staged registration for SceneID {matchingOperation.idToAssign}");
                    return;
                }

                _pendingOperations.RemoveAt(matchingIndex);
                return;
            }

            _sceneActionScenes.Add(matchingOperation.idToAssign);
            AddScene(scene, matchingOperation.settings, matchingOperation.idToAssign);
            _pendingOperations.RemoveAt(matchingIndex);
        }

        private void RegisterBuildSceneCompletionCallback(PendingSceneOperation pending)
        {
            if (pending.operation == null)
                return;

            pending.operation.completed += _ =>
            {
                if (!_requiresTransferReconciliation)
                    return;

                for (var i = _pendingOperations.Count - 1; i >= 0; i--)
                {
                    var candidate = _pendingOperations[i];
                    if (candidate.operation != pending.operation ||
                        candidate.idToAssign != pending.idToAssign)
                        continue;

                    _pendingOperations.RemoveAt(i);
                    FailTransferReconciliation(
                        $"Unity completed the load operation for authoritative SceneID " +
                        $"{pending.idToAssign}, but no compatible loaded scene was registered.");
                    break;
                }
            };
        }

        private void RetireUnclaimedLoadedScene(Scene scene, int pendingIndex, string context)
        {
            if (pendingIndex >= 0 && pendingIndex < _pendingOperations.Count)
                _pendingOperations.RemoveAt(pendingIndex);

            if (!scene.IsValid() || !scene.isLoaded || HasScene(scene) || IsSceneUnloading(scene) ||
                ShouldKeepLocalSceneDuringTransfer(scene))
                return;

            TrackSceneUnload(scene, SceneManager.UnloadSceneAsync(scene), context);
        }

        private bool IsScenePending(SceneID sceneId)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                if (_pendingOperations[i].idToAssign == sceneId)
                    return true;
            }
#if ADDRESSABLES_PURRNET_SUPPORT
            if (IsScenePendingAddressable(sceneId))
                return true;
#endif
            return false;
        }

        int GetCurrentLoadedScenes()
        {
            int count = 0;

            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (IsDontDestroyOnLoadScene(state.scene))
                        continue;

                    if (state.scene.isLoaded)
                        count++;
                }
            }

            return count;
        }

        private void HandleNextSceneAction()
        {
            if (_actionsQueue.Count == 0) return;

            PollTransferReconciliationOperations();

            var action = _actionsQueue.Peek();
            if (_requiresTransferReconciliation &&
                (action.type == SceneActionType.Load || action.type == SceneActionType.LoadAddressable) &&
                (_pendingSceneUnloads.Count > 0 || _pendingOperations.Count > 0
#if ADDRESSABLES_PURRNET_SUPPORT
                 || _pendingAddressableOperations.Count > 0 || _pendingAddressableUnloads.Count > 0
#endif
                ))
            {
                return;
            }

            switch (action.type)
            {
                case SceneActionType.Load:
                    {
                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

                        var loadAction = action.loadSceneAction;

                        // A reconnect delivers the same load action twice: once in the
                        // first-join batch and once when the player is re-added to the
                        // public scene. Loading again would duplicate the unity scene
                        // and clash with the already assigned SceneID.
                        if (_scenes.ContainsKey(loadAction.sceneID) || IsScenePending(loadAction.sceneID))
                        {
                            _sceneActionScenes.Add(loadAction.sceneID);
                            _actionsQueue.Dequeue();
                            break;
                        }

                        var localBuildIndex = BuildIndexFromScenePathHash(loadAction.scenePathHash);

                        if (localBuildIndex == -1)
                        {
                            PurrLogger.LogError($"Scene with path hash '{loadAction.scenePathHash}' not found in build settings");
                            FailTransferReconciliation(
                                $"Authoritative scene path hash '{loadAction.scenePathHash}' " +
                                "is not present in this client's build settings.");
                            _actionsQueue.Dequeue();
                            break;
                        }

                        AsyncOperation operation;

                        try
                        {
                            operation = SceneManager.LoadSceneAsync(localBuildIndex, loadAction.GetLoadSceneParameters());
                        }
                        catch (Exception e)
                        {
                            PurrLogger.LogError($"Error loading scene: {e}");
                            if (_requiresTransferReconciliation)
                            {
                                FailTransferReconciliation(
                                    $"Unity threw while loading authoritative scene {loadAction.sceneID}.", e);
                                _actionsQueue.Dequeue();
                            }
                            break;
                        }

                        if (operation == null)
                        {
                            PurrLogger.LogError(
                                $"Unity did not start loading scene {loadAction.sceneID} " +
                                $"(path hash '{loadAction.scenePathHash}').");
                            FailTransferReconciliation(
                                $"Unity did not start loading authoritative scene {loadAction.sceneID}.");
                            _actionsQueue.Dequeue();
                            break;
                        }

                        if (loadAction.parameters.mode == LoadSceneMode.Single)
                        {
                            for (int i = 0; i < _rawScenes.Count; i++)
                            {
                                if (!IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene))
                                    RemoveScene(_scenes[_rawScenes[i]].scene);
                            }
                        }

                        var pendingOperation = new PendingSceneOperation
                        {
                            buildIndex = localBuildIndex,
                            scenePathHash = loadAction.scenePathHash,
                            settings = loadAction.parameters,
                            idToAssign = loadAction.sceneID,
                            operation = operation
                        };
                        _pendingOperations.Add(pendingOperation);
                        RegisterBuildSceneCompletionCallback(pendingOperation);
                        _sceneActionScenes.Add(loadAction.sceneID);

                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.LoadAddressable:
                    {
                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

#if ADDRESSABLES_PURRNET_SUPPORT
                        ProcessLoadAddressableAction(action.loadAddressableSceneAction);
#else
                        PurrLogger.LogError("Received LoadAddressable scene action but Addressables support is not available");
                        FailTransferReconciliation(
                            "The authoritative scene manifest requires Addressables, but this client " +
                            "was built without PurrNet Addressables support.");
#endif
                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.Unload:
                    {
                        var currentlyLoadedCount = GetCurrentLoadedScenes();
                        if (currentlyLoadedCount == 1)
                        {
                            // wait for the next load action
                            break;
                        }

                        var idx = action.unloadSceneAction.sceneID;

                        if (_networkManager.isHost && !_asServer)
                        {
                            _scenesToTriggerUnloadEvent.Add(idx);
                            _actionsQueue.Dequeue();
                            break;
                        }

                        // if the scene is pending, don't do anything for now
                        if (IsScenePending(idx)) break;
#if ADDRESSABLES_PURRNET_SUPPORT
                        if (TryUnloadAddressableScene(idx, action.unloadSceneAction.options))
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }
#endif

                        if (!_scenes.TryGetValue(idx, out var sceneState))
                        {
                            PurrLogger.LogError($"Couldn't find scene with index {idx} to unload");
                            break;
                        }

                        TrackSceneUnload(sceneState.scene,
                            SceneManager.UnloadSceneAsync(sceneState.scene, action.unloadSceneAction.options),
                            $"apply unload for SceneID {idx}");
                        RemoveScene(sceneState.scene);
                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.SetActive:
                    {
                        var sceneId = action.setActiveSceneAction.sceneID;

                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

                        if (IsScenePending(sceneId))
                            break;

                        if (!_scenes.TryGetValue(sceneId, out var sceneState) ||
                            !sceneState.scene.IsValid() || !sceneState.scene.isLoaded)
                        {
                            PurrLogger.LogError(
                                $"Couldn't set active SceneID {sceneId}; its retained scene is not loaded.");
                            FailTransferReconciliation(
                                $"Authoritative active SceneID {sceneId} is not loaded.");
                            _actionsQueue.Dequeue();
                            break;
                        }

                        if (!SceneManager.SetActiveScene(sceneState.scene))
                        {
                            PurrLogger.LogError($"Unity rejected active SceneID {sceneId}.");
                            FailTransferReconciliation(
                                $"Unity rejected authoritative active SceneID {sceneId}.");
                        }

                        _actionsQueue.Dequeue();
                        break;
                    }
            }
        }

        private void OnSceneActionsBatch(PlayerID player, FirstSceneActionsBatch data, bool asServer)
        {
            if (_requiresTransferReconciliation && _exactTransferBaselineReceived)
            {
                FailTransferReconciliation(
                    "The replacement authority sent more than one initial scene manifest.");
                return;
            }

            if (!_isTransferingToNewServer)
            {
                HandleScenes(data.actions);
                return;
            }

            _isTransferingToNewServer = false;
            _exactTransferBaselineReceived = _requiresTransferReconciliation;

            ReconcileTransferScenes(data.actions);
        }

        private bool ValidateTransferManifest(List<SceneAction> actions)
        {
            if (!_requiresTransferReconciliation)
                return actions != null;

            if (!TryValidateExactSceneManifestShape(actions, out var uniquenessFailure))
            {
                FailTransferReconciliation(
                    $"The authoritative scene manifest is invalid: {uniquenessFailure}");
                return false;
            }

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                switch (action.type)
                {
                    case SceneActionType.Load:
                        if (BuildIndexFromScenePathHash(action.loadSceneAction.scenePathHash) == -1)
                        {
                            FailTransferReconciliation(
                                $"Authoritative scene path hash '{action.loadSceneAction.scenePathHash}' " +
                                "is not present in this client's build settings.");
                            return false;
                        }
                        if (IsBuildScenePathHashAmbiguous(action.loadSceneAction.scenePathHash))
                        {
                            FailTransferReconciliation(
                                $"Authoritative scene path hash '{action.loadSceneAction.scenePathHash}' " +
                                "matches more than one scene in this client's build settings.");
                            return false;
                        }
                        break;
                    case SceneActionType.LoadAddressable:
#if ADDRESSABLES_PURRNET_SUPPORT
                        break;
#else
                        FailTransferReconciliation(
                            "The authoritative scene manifest requires Addressables, but this client " +
                            "was built without PurrNet Addressables support.");
                        return false;
#endif
                    case SceneActionType.SetActive:
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private void ReconcileTransferScenes(List<SceneAction> actions)
        {
            if (_transferReconciliationFailure != null)
                return;

            ProcessCompletedAddressableLoads();
            PollTransferReconciliationOperations();
            _actionsQueue.Clear();

            if (!ValidateTransferManifest(actions))
                return;

            if (_requiresTransferReconciliation)
            {
                ReconcileExactTransferScenes(actions);
                return;
            }

            var targetScenes = new HashSet<SceneID>();
            var targetBuildScenes = new Dictionary<SceneID, LoadSceneAction>();
#if ADDRESSABLES_PURRNET_SUPPORT
            var targetAddressableScenes = new Dictionary<SceneID, LoadAddressableSceneAction>();
#endif
            var missingActions = new List<SceneAction>();
            var retainedSceneEvents = new List<SceneID>();

            for (var i = 0; i < actions.Count; i++)
            {
                if (_transferReconciliationFailure != null)
                    break;

                var action = actions[i];

                switch (action.type)
                {
                    case SceneActionType.Load:
                    {
                        var loadAction = action.loadSceneAction;
                        targetScenes.Add(loadAction.sceneID);
                        targetBuildScenes[loadAction.sceneID] = loadAction;
                        _sceneActionScenes.Add(loadAction.sceneID);

                        var buildIndex = BuildIndexFromScenePathHash(loadAction.scenePathHash);
                        if (buildIndex == -1)
                        {
                            FailTransferReconciliation(
                                $"Authoritative scene path hash '{loadAction.scenePathHash}' " +
                                "is not present in this client's build settings.");
                            missingActions.Add(action);
                            break;
                        }

                        if (TryReconcileLoadedTransferScene(loadAction, buildIndex, retainedSceneEvents))
                            break;

                        var buildPending = IsBuildScenePending(loadAction);
                        if (!buildPending)
                            missingActions.Add(action);

                        break;
                    }
                    case SceneActionType.LoadAddressable:
                    {
                        var loadAction = action.loadAddressableSceneAction;
                        targetScenes.Add(loadAction.sceneID);
                        _sceneActionScenes.Add(loadAction.sceneID);
#if ADDRESSABLES_PURRNET_SUPPORT
                        var guid = loadAction.guid.value;
                        targetAddressableScenes[loadAction.sceneID] = loadAction;

                        if (TryReconcileLoadedAddressableTransferScene(loadAction, retainedSceneEvents))
                            break;

                        var addressablePending = IsAddressableScenePending(loadAction);
                        if (!addressablePending)
                            missingActions.Add(action);
#else
                        FailTransferReconciliation(
                            "The authoritative scene manifest requires Addressables, but this client " +
                            "was built without PurrNet Addressables support.");
                        missingActions.Add(action);
#endif
                        break;
                    }
                    case SceneActionType.Unload:
                    case SceneActionType.SetActive:
                    default:
                        missingActions.Add(action);
                        break;
                }
            }

            if (_transferReconciliationFailure != null)
                return;

#if ADDRESSABLES_PURRNET_SUPPORT
            RemoveStaleAddressableTransferScenes(targetAddressableScenes);
#endif
            RemoveStaleTransferScenes(targetScenes, targetBuildScenes);

            if (_transferReconciliationFailure != null)
                return;

            for (var i = 0; i < retainedSceneEvents.Count; i++)
                PlayLoadEventsForScene(retainedSceneEvents[i]);

            if (missingActions.Count > 0)
                HandleScenes(missingActions);
        }

        private static HashSet<uint> GetRepeatedBuildSceneHashes(
            IReadOnlyList<SceneAction> actions)
        {
            var firstSeen = new HashSet<uint>();
            var repeated = new HashSet<uint>();
            for (var i = 0; i < actions.Count; i++)
            {
                if (actions[i].type != SceneActionType.Load)
                    continue;

                var hash = actions[i].loadSceneAction.scenePathHash;
                if (!firstSeen.Add(hash))
                    repeated.Add(hash);
            }

            return repeated;
        }

        private void ReconcileExactTransferScenes(List<SceneAction> actions)
        {
            var targetScenes = new HashSet<SceneID>();
            var targetBuildScenes = new Dictionary<SceneID, LoadSceneAction>();
#if ADDRESSABLES_PURRNET_SUPPORT
            var targetAddressableScenes = new Dictionary<SceneID, LoadAddressableSceneAction>();
#endif
            var claimedPhysicalScenes = new Dictionary<Scene, SceneID>();
            var missingActions = new List<SceneAction>();
            var retainedSceneRebounds = new List<SceneID>();
            var repeatedBuildSceneHashes = GetRepeatedBuildSceneHashes(actions);

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                bool isRetained;
                switch (action.type)
                {
                    case SceneActionType.Load:
                    {
                        var load = action.loadSceneAction;
                        targetScenes.Add(load.sceneID);
                        targetBuildScenes.Add(load.sceneID, load);
                        var buildIndex = BuildIndexFromScenePathHash(load.scenePathHash);
                        if (!TryPreflightLoadedBuildTarget(
                                load, buildIndex,
                                !repeatedBuildSceneHashes.Contains(load.scenePathHash),
                                claimedPhysicalScenes, out isRetained))
                            return;

                        if (isRetained)
                        {
                            retainedSceneRebounds.Add(load.sceneID);
                            break;
                        }

                        var isPending = IsBuildScenePending(load);
                        if (_transferReconciliationFailure != null)
                            return;
                        if (!isPending)
                            missingActions.Add(NormalizeExactMissingLoadForStaging(action));
                        break;
                    }
                    case SceneActionType.LoadAddressable:
                    {
#if ADDRESSABLES_PURRNET_SUPPORT
                        var load = action.loadAddressableSceneAction;
                        targetScenes.Add(load.sceneID);
                        targetAddressableScenes.Add(load.sceneID, load);
                        if (!TryPreflightLoadedAddressableTarget(
                                load, claimedPhysicalScenes, out isRetained))
                            return;

                        if (isRetained)
                        {
                            retainedSceneRebounds.Add(load.sceneID);
                            break;
                        }

                        var isPending = IsAddressableScenePending(load);
                        if (_transferReconciliationFailure != null)
                            return;
                        if (!isPending)
                            missingActions.Add(NormalizeExactMissingLoadForStaging(action));
                        break;
#else
                        return;
#endif
                    }
                    case SceneActionType.SetActive:
                        continue;
                    default:
                        return;
                }
            }

            if (!ValidateNoStaleBuildSceneLoads(targetBuildScenes))
                return;
#if ADDRESSABLES_PURRNET_SUPPORT
            if (!ValidateNoStaleAddressableSceneLoads(targetAddressableScenes))
                return;
#endif
            if (!ValidateExactRegisteredSceneTopology(targetScenes))
                return;

            _deferredExactTransferManifest = new List<SceneAction>(actions);
            _deferredRetainedSceneRebounds = retainedSceneRebounds;
            _exactActiveSceneCommitFrame = -1;

            if (missingActions.Count > 0)
                HandleScenes(missingActions);

            TryCommitDeferredExactTransferManifest();
        }

        private bool TryPreflightLoadedBuildTarget(
            LoadSceneAction loadAction,
            int buildIndex,
            bool allowUnregisteredAdoption,
            IDictionary<Scene, SceneID> claimedPhysicalScenes,
            out bool isRetained)
        {
            isRetained = false;
            var loadedMatches = GetLoadedBuildScenes(buildIndex);
            var hasStableAuthoritativeBinding = false;

            if (_scenes.TryGetValue(loadAction.sceneID, out var existing))
            {
                var existingIsLoaded = existing.scene.IsValid() && existing.scene.isLoaded;
                var identityMatches = existingIsLoaded && existing.scene.buildIndex == buildIndex;
#if ADDRESSABLES_PURRNET_SUPPORT
                identityMatches = identityMatches && !HasAddressableSceneRegistration(loadAction.sceneID);
#endif
                var topologyIsStable = existingIsLoaded && !IsSceneUnloading(existing.scene);
                var immutableSettingsMatch = existingIsLoaded &&
                    TryReconcileLoadedSceneSettings(
                        existing.scene, existing.settings, loadAction.parameters,
                        out _, out _, out _, out _);
                hasStableAuthoritativeBinding =
                    identityMatches && topologyIsStable && immutableSettingsMatch;

                if (hasStableAuthoritativeBinding)
                {
                    isRetained = TryClaimExactPhysicalScene(
                        existing.scene, loadAction.sceneID, claimedPhysicalScenes);
                    return isRetained;
                }

                if (ShouldRejectLoadedTargetReplacement(
                        true, existingIsLoaded, identityMatches,
                        immutableSettingsMatch, topologyIsStable))
                {
                    FailTransferReconciliation(
                        $"Loaded scene registered for authoritative SceneID {loadAction.sceneID} does not " +
                        "match its build identity or stable topology. Its SceneID cannot move " +
                        "without destroying scene-authored roots owned by the old hierarchy pool.");
                    return false;
                }
            }

            if (IsLoadedTargetSelectionAmbiguous(
                    loadedMatches.Count, hasStableAuthoritativeBinding))
            {
                return true;
            }

            if (loadedMatches.Count == 0)
                return true;

            var loadedScene = loadedMatches[0];
            if (IsSceneUnloading(loadedScene))
            {
                return true;
            }

            if (!TryInspectExactLoadedSceneRegistration(
                    loadedScene, out var hasRetainedRegistration,
                    out var retainedId, out var retainedState, out var registrationFailure))
            {
                FailTransferReconciliation(
                    $"Loaded scene '{loadedScene.name}' matches authoritative SceneID " +
                    $"{loadAction.sceneID} by build path, but {registrationFailure}.");
                return false;
            }

            if (!hasRetainedRegistration)
            {
                if (!allowUnregisteredAdoption)
                    return true;

                if (claimedPhysicalScenes.ContainsKey(loadedScene))
                    return true;

                if (!TryPreflightUnregisteredExactSceneAdoption(
                        loadedScene, out var adoptionFailure))
                {
                    PurrLogger.LogWarning(
                        $"Loaded scene '{loadedScene.name}' was not adopted as authoritative " +
                        $"SceneID {loadAction.sceneID}: {adoptionFailure}. Loading a separate " +
                        "Additive instance instead.");
                    return true;
                }

                if (!TryReconcileLoadedSceneSettings(
                        loadedScene, loadAction.parameters, loadAction.parameters,
                        out _, out _, out _, out var adoptionPhysicsFailure))
                {
                    PurrLogger.LogWarning(
                        $"Loaded scene '{loadedScene.name}' was not adopted as authoritative " +
                        $"SceneID {loadAction.sceneID}: {adoptionPhysicsFailure}. Loading a " +
                        "separate Additive instance instead.");
                    return true;
                }

                isRetained = TryClaimExactPhysicalScene(
                    loadedScene, loadAction.sceneID, claimedPhysicalScenes);
                return isRetained;
            }

#if ADDRESSABLES_PURRNET_SUPPORT
            var retainedIsAddressable = HasAddressableSceneRegistration(retainedId);
#else
            const bool retainedIsAddressable = false;
#endif
            if (!IsExactSceneDescriptorIdentityMatch(
                    retainedId, loadAction.sceneID, retainedIsAddressable, false))
            {
                return true;
            }

            if (!TryReconcileLoadedSceneSettings(
                    loadedScene, retainedState.settings, loadAction.parameters,
                    out _, out _, out _, out var physicsFailure))
            {
                PurrLogger.LogWarning(
                    $"Retained SceneID {retainedId} could not supply authoritative SceneID " +
                    $"{loadAction.sceneID}: {physicsFailure}. Loading a separate Additive " +
                    "instance instead.");
                return true;
            }

            isRetained = TryClaimExactPhysicalScene(
                loadedScene, loadAction.sceneID, claimedPhysicalScenes);
            return isRetained;
        }

        private bool TryInspectExactLoadedSceneRegistration(
            Scene scene,
            out bool isRegistered,
            out SceneID id,
            out SceneState state,
            out string failure)
        {
            isRegistered = false;
            id = default;
            state = default;
            failure = null;

            var stateMatches = 0;
            var matchedId = default(SceneID);
            var matchedState = default(SceneState);
            foreach (var pair in _scenes)
            {
                if (pair.Value.scene.handle != scene.handle)
                    continue;

                stateMatches++;
                matchedId = pair.Key;
                matchedState = pair.Value;
            }

            var hasReverse = _idToScene.TryGetValue(scene, out var reverseId);
            if (!hasReverse && stateMatches == 0)
                return true;

            if (!hasReverse || stateMatches != 1 || reverseId != matchedId ||
                !_rawScenes.Contains(reverseId))
            {
                failure = "its retained scene registry is not a one-to-one physical binding";
                return false;
            }

            isRegistered = true;
            id = reverseId;
            state = matchedState;
            return true;
        }

        internal static bool TryPreflightUnregisteredExactSceneAdoption(
            Scene scene,
            out string failure)
        {
            failure = null;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var identities = roots[i].GetComponentsInChildren<NetworkIdentity>(true);
                for (var j = 0; j < identities.Length; j++)
                {
                    var identity = identities[j];
                    if (!identity)
                        continue;

                    if (identity.isSpawned || identity.id.HasValue ||
                        identity.networkManager || identity.sceneId != default ||
                        identity.isManualSpawn || identity.isInPool)
                    {
                        failure = $"NetworkIdentity '{identity.name}' already carries a live or " +
                                  "previously materialized network lifetime";
                        return false;
                    }

                    var ancestry = new HashSet<NetworkIdentity> { identity };
                    var parent = identity.parent;
                    while (!ReferenceEquals(parent, null))
                    {
                        if (!parent || parent.gameObject.scene.handle != scene.handle ||
                            !ancestry.Add(parent))
                        {
                            failure = $"NetworkIdentity '{identity.name}' has a dead, cyclic, or " +
                                      "cross-scene static network parent";
                            return false;
                        }

                        parent = parent.parent;
                    }
                }
            }

            return true;
        }

        private bool TryClaimExactPhysicalScene(
            Scene scene,
            SceneID sceneId,
            IDictionary<Scene, SceneID> claimedPhysicalScenes)
        {
            if (claimedPhysicalScenes.TryGetValue(scene, out var priorId))
            {
                FailTransferReconciliation(
                    $"The loaded Unity scene '{scene.name}' is claimed by both authoritative " +
                    $"SceneID {priorId} and SceneID {sceneId}.");
                return false;
            }

            claimedPhysicalScenes.Add(scene, sceneId);
            return true;
        }

        private bool TryCollectExactLoadedSceneAdoption(
            SceneAction action,
            IReadOnlyDictionary<Scene, SceneID> claimedPhysicalScenes,
            ICollection<ExactLoadedSceneAdoption> adoptions,
            ISet<SceneID> adoptionIds,
            out string failure)
        {
            var id = GetSceneActionId(action);
            failure = null;
            if (action.type != SceneActionType.Load)
            {
                failure = $"Loaded Addressable SceneID {id} has no retained GUID/handle metadata; " +
                          "a Unity Scene alone cannot prove Addressables identity or handle ownership.";
                return false;
            }

            if (!TryGetClaimedExactPhysicalScene(
                    claimedPhysicalScenes, id, out var scene))
            {
                failure = $"Authoritative SceneID {id} has no uniquely claimed loaded Unity scene.";
                return false;
            }

            var authoritativeSettings = action.loadSceneAction.parameters;

            if (!TryInspectExactLoadedSceneRegistration(
                    scene, out var hasRegistration, out var oldId,
                    out var retainedState, out var registrationFailure))
            {
                failure = $"Loaded Unity scene '{scene.name}' cannot bind authoritative SceneID " +
                          $"{id} because {registrationFailure}.";
                return false;
            }

            if (hasRegistration)
            {
                failure = $"Loaded Unity scene '{scene.name}' is already registered as SceneID " +
                          $"{oldId}; live SceneID re-keying cannot preserve scene-authored roots.";
                return false;
            }

            if (!TryPreflightUnregisteredExactSceneAdoption(scene, out var adoptionFailure))
            {
                failure = $"Loaded Unity scene '{scene.name}' cannot be adopted as SceneID {id}: " +
                          adoptionFailure;
                return false;
            }

            if (!TryReconcileLoadedSceneSettings(
                    scene, authoritativeSettings, authoritativeSettings,
                    out var reconciledSettings, out _, out _, out var physicsFailure))
            {
                failure = $"Loaded Unity scene '{scene.name}' for authoritative SceneID {id} " +
                          $"has unproven physical topology: {physicsFailure}";
                return false;
            }

            if (!adoptionIds.Add(id))
            {
                failure = $"Authoritative SceneID {id} has more than one loaded-scene adoption.";
                return false;
            }

            adoptions.Add(new ExactLoadedSceneAdoption(
                id,
                scene,
                reconciledSettings,
                authoritativeSettings.physicsMode));
            return true;
        }

        private static bool TryGetClaimedExactPhysicalScene(
            IReadOnlyDictionary<Scene, SceneID> claimedPhysicalScenes,
            SceneID id,
            out Scene scene)
        {
            foreach (var pair in claimedPhysicalScenes)
            {
                if (pair.Value != id)
                    continue;

                scene = pair.Key;
                return true;
            }

            scene = default;
            return false;
        }

        private bool TryValidateExactLoadedSceneAdoptionBatch(
            IReadOnlyList<ExactLoadedSceneAdoption> adoptions,
            out string failure)
        {
            failure = null;
            for (var i = 0; i < adoptions.Count; i++)
            {
                var adoption = adoptions[i];
                if (!_scenes.TryGetValue(adoption.newId, out var occupant))
                    continue;

                failure = $"Authoritative SceneID {adoption.newId} is occupied by Unity scene " +
                          $"'{occupant.scene.name}'; the unregistered target " +
                          $"'{adoption.scene.name}' cannot be adopted reversibly.";
                return false;
            }

            return true;
        }

        private bool ValidateNoStaleBuildSceneLoads(
            IReadOnlyDictionary<SceneID, LoadSceneAction> targetBuildScenes)
        {
            for (var i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];
                if (targetBuildScenes.TryGetValue(operation.idToAssign, out var target) &&
                    operation.scenePathHash == target.scenePathHash &&
                    operation.operation != null &&
                    ArePendingSceneSettingsCompatible(operation.settings, target.parameters))
                    continue;

                FailTransferReconciliation(
                    $"A stale load for SceneID {operation.idToAssign} is still in flight; " +
                    "Unity scene loads cannot be cancelled safely.");
                return false;
            }

            return true;
        }

        private bool HasUnsettledExactTransferSceneOperations()
        {
            return _history.hasUnflushedActions ||
                   _actionsQueue.Count > 0 ||
                   _scenesToTriggerUnloadEvent.Count > 0 ||
                   _pendingOperations.Count > 0 ||
                   _pendingSceneUnloads.Count > 0
#if ADDRESSABLES_PURRNET_SUPPORT
                   || _pendingAddressableOperations.Count > 0 ||
                   _pendingAddressableUnloads.Count > 0
#endif
                ;
        }

        private void TryCommitDeferredExactTransferManifest()
        {
            if (!_requiresTransferReconciliation ||
                _deferredExactTransferManifest == null ||
                _transferReconciliationFailure != null ||
                _isCommittingExactTransferManifest ||
                HasUnsettledExactTransferSceneOperations())
                return;

            _isCommittingExactTransferManifest = true;
            try
            {
                CommitDeferredExactTransferManifest();
            }
            catch (Exception e)
            {
                FailTransferReconciliation(
                    "The exact scene manifest could not be committed without replacing retained state.", e);
            }
            finally
            {
                _isCommittingExactTransferManifest = false;
            }
        }

        private void CommitDeferredExactTransferManifest()
        {
            var actions = _deferredExactTransferManifest;
            if (!ValidateTransferManifest(actions))
                return;

            var targetScenes = new HashSet<SceneID>();
            var targetBuildScenes = new Dictionary<SceneID, LoadSceneAction>();
#if ADDRESSABLES_PURRNET_SUPPORT
            var targetAddressableScenes = new Dictionary<SceneID, LoadAddressableSceneAction>();
#endif
            var claimedPhysicalScenes = new Dictionary<Scene, SceneID>();
            var loadedSceneAdoptions = new List<ExactLoadedSceneAdoption>();
            var loadedSceneAdoptionIds = new HashSet<SceneID>();
            var repeatedBuildSceneHashes = GetRepeatedBuildSceneHashes(actions);
            SceneID? desiredActiveScene = null;

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                bool isRetained;
                switch (action.type)
                {
                    case SceneActionType.Load:
                    {
                        var load = action.loadSceneAction;
                        targetScenes.Add(load.sceneID);
                        targetBuildScenes.Add(load.sceneID, load);
                        if (!TryPreflightLoadedBuildTarget(
                                load, BuildIndexFromScenePathHash(load.scenePathHash),
                                !repeatedBuildSceneHashes.Contains(load.scenePathHash),
                                claimedPhysicalScenes, out isRetained))
                            return;
                        break;
                    }
                    case SceneActionType.LoadAddressable:
                    {
#if ADDRESSABLES_PURRNET_SUPPORT
                        var load = action.loadAddressableSceneAction;
                        targetScenes.Add(load.sceneID);
                        targetAddressableScenes.Add(load.sceneID, load);
                        if (!TryPreflightLoadedAddressableTarget(
                                load, claimedPhysicalScenes, out isRetained))
                            return;
                        break;
#else
                        return;
#endif
                    }
                    case SceneActionType.SetActive:
                        desiredActiveScene = action.setActiveSceneAction.sceneID;
                        continue;
                    default:
                        return;
                }

                if (!isRetained)
                {
                    FailTransferReconciliation(
                        "All authoritative scene loads settled, but SceneID " +
                        $"{GetSceneActionId(action)} still has no compatible loaded target.");
                    return;
                }

                var targetId = GetSceneActionId(action);
                if (!_scenes.ContainsKey(targetId))
                {
                    if (!TryCollectExactLoadedSceneAdoption(
                            action, claimedPhysicalScenes, loadedSceneAdoptions,
                            loadedSceneAdoptionIds, out var adoptionFailure))
                    {
                        FailTransferReconciliation(adoptionFailure);
                        return;
                    }
                }
            }

            if (!ValidateNoStaleBuildSceneLoads(targetBuildScenes))
                return;
#if ADDRESSABLES_PURRNET_SUPPORT
            if (!ValidateNoStaleAddressableSceneLoads(targetAddressableScenes))
                return;
#endif
            if (!ValidateExactRegisteredSceneTopology(targetScenes))
                return;

#if ADDRESSABLES_PURRNET_SUPPORT
            var staleAddressableFailure = default(string);
            ValidateExactAddressableAuthoritySwitchState(ref staleAddressableFailure);
            if (staleAddressableFailure != null)
            {
                FailTransferReconciliation(
                    "The stale Addressables registry cannot be cleaned transactionally: " +
                    staleAddressableFailure);
                return;
            }
#endif

            Scene preflightActiveScene = default;
            if (desiredActiveScene.HasValue)
            {
                var activeId = desiredActiveScene.Value;
                if (!targetScenes.Contains(activeId) ||
                    !TryGetClaimedExactPhysicalScene(
                        claimedPhysicalScenes, activeId, out preflightActiveScene) ||
                    !preflightActiveScene.IsValid() || !preflightActiveScene.isLoaded ||
                    IsSceneUnloading(preflightActiveScene))
                {
                    FailTransferReconciliation(
                        $"Authoritative active SceneID {activeId} is not a stable loaded target.");
                    return;
                }
            }

            var ignoredEvents = new List<SceneID>();
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action.type == SceneActionType.SetActive)
                    continue;

                if (loadedSceneAdoptionIds.Contains(GetSceneActionId(action)))
                    continue;

                var applied = action.type switch
                {
                    SceneActionType.Load => TryReconcileLoadedTransferScene(
                        action.loadSceneAction,
                        BuildIndexFromScenePathHash(action.loadSceneAction.scenePathHash),
                        ignoredEvents),
#if ADDRESSABLES_PURRNET_SUPPORT
                    SceneActionType.LoadAddressable => TryReconcileLoadedAddressableTransferScene(
                        action.loadAddressableSceneAction, ignoredEvents),
#endif
                    _ => false
                };

                if (!applied || _transferReconciliationFailure != null)
                {
                    if (_transferReconciliationFailure == null)
                    {
                        FailTransferReconciliation(
                            $"Authoritative SceneID {GetSceneActionId(action)} disappeared after exact preflight.");
                    }
                    return;
                }
            }

            if (!_exactStructuralSceneCommitStarted)
            {
                if (!TryValidateExactLoadedSceneAdoptionBatch(
                        loadedSceneAdoptions, out var adoptionBatchFailure))
                {
                    FailTransferReconciliation(adoptionBatchFailure);
                    return;
                }

                for (var i = 0; i < loadedSceneAdoptions.Count; i++)
                {
                    var adoption = loadedSceneAdoptions[i];
                    if (!TryStageExactScene(
                            adoption.scene, adoption.settings, adoption.newId,
                            out var adoptionFailure, true))
                    {
                        FailTransferReconciliation(
                            $"Could not adopt loaded Unity scene '{adoption.scene.name}' as " +
                            $"authoritative SceneID {adoption.newId}: {adoptionFailure}");
                        return;
                    }

                    _sceneActionScenes.Add(adoption.newId);
                    if (adoption.settings.physicsMode != adoption.authoritativePhysicsMode)
                    {
                        WarnBestEffortPhysicsFallback(
                            adoption.newId, adoption.scene,
                            adoption.authoritativePhysicsMode, adoption.settings.physicsMode);
                    }
                }

                var topologyScenes = GetExactTopologySceneSet(actions);
                string topologyFailure = null;
                if (!_networkManager.TryGetModule<HierarchyFactory>(false, out var hierarchyFactory) ||
                    !hierarchyFactory.RegisterExactInboundSceneSet(
                        _networkManager.expectedHostMigrationSession, topologyScenes, out topologyFailure))
                {
                    FailTransferReconciliation(
                        "The exact scene graph could not prepare its hierarchy transaction: " +
                        (topologyFailure ?? "the client hierarchy factory is unavailable"));
                    return;
                }

                _deferredStagedExactSceneLoads = new HashSet<SceneID>(_stagedExactScenes.Keys);
                _deferredStagedExactSceneLoads.ExceptWith(
                    _stagedExactRetainedSceneAdoptions);
                _exactStructuralSceneCommitStarted = true;
                PublishStagedExactScenesAfterStructuralCommit();
#if ADDRESSABLES_PURRNET_SUPPORT
                var retainedStaleScenes = new List<Scene>();
                var unloadableStaleAddressableIds = new List<SceneID>();
                RemoveStaleAddressableTransferScenes(
                    targetAddressableScenes,
                    retainedStaleScenes,
                    unloadableStaleAddressableIds);
                if (_transferReconciliationFailure != null)
                    return;
#endif
                RemoveStaleTransferScenes(
                    targetScenes,
                    targetBuildScenes,
#if ADDRESSABLES_PURRNET_SUPPORT
                    retainedStaleScenes,
                    new HashSet<SceneID>(unloadableStaleAddressableIds)
#else
                    null,
                    null
#endif
                );
                if (_transferReconciliationFailure != null)
                    return;
#if ADDRESSABLES_PURRNET_SUPPORT
                RemovePlannedStaleAddressableScenes(unloadableStaleAddressableIds);
                if (_transferReconciliationFailure != null)
                    return;
#endif
            }

            if (desiredActiveScene.HasValue)
            {
                var activeId = desiredActiveScene.Value;
                if (!_scenes.TryGetValue(activeId, out var activeState) ||
                    !activeState.scene.IsValid() || !activeState.scene.isLoaded ||
                    activeState.scene.handle != preflightActiveScene.handle)
                {
                    FailTransferReconciliation(
                        $"Unity could not commit authoritative active SceneID {activeId}: " +
                        $"registered={_scenes.ContainsKey(activeId)}, " +
                        $"valid={activeState.scene.IsValid()}, loaded={activeState.scene.isLoaded}, " +
                        $"handle={activeState.scene.handle}, preflightHandle={preflightActiveScene.handle}.");
                    return;
                }

                if (_exactActiveSceneCommitFrame < 0)
                {
                    if (SceneManager.GetActiveScene().handle != activeState.scene.handle &&
                        !SceneManager.SetActiveScene(activeState.scene))
                    {
                        FailTransferReconciliation(
                            $"Unity refused SetActiveScene for authoritative active SceneID {activeId} " +
                            $"('{activeState.scene.name}', loaded={activeState.scene.isLoaded}).");
                        return;
                    }

                    _exactActiveSceneCommitFrame = Time.frameCount;
                    return;
                }

                if (Time.frameCount <= _exactActiveSceneCommitFrame)
                    return;

                var committedActive = SceneManager.GetActiveScene();
                if (!committedActive.IsValid() || !committedActive.isLoaded ||
                    committedActive.handle != activeState.scene.handle)
                {
                    FailTransferReconciliation(
                        $"Authoritative active SceneID {activeId} changed during commit callbacks.");
                    return;
                }
            }

            var retainedRebounds = _deferredRetainedSceneRebounds == null
                ? null
                : new HashSet<SceneID>(_deferredRetainedSceneRebounds);
            var stagedScenes = _deferredStagedExactSceneLoads ?? new HashSet<SceneID>();

            _exactStructuralSceneCommitStarted = false;
            _deferredStagedExactSceneLoads = null;
            _deferredExactTransferManifest = null;
            _deferredRetainedSceneRebounds = null;
            _exactActiveSceneCommitFrame = -1;

            for (var i = 0; i < actions.Count; i++)
            {
                var id = GetSceneActionId(actions[i]);
                if (actions[i].type == SceneActionType.SetActive)
                    continue;

                if (stagedScenes.Contains(id))
                {
                    PlayExactCommittedSceneLoadEvents(id);
                    continue;
                }

                if (retainedRebounds != null && retainedRebounds.Contains(id) &&
                    !PlayRetainedSceneReboundForScene(id))
                    return;
            }
        }

        private void PlayExactCommittedSceneLoadEvents(SceneID id)
        {
#if ADDRESSABLES_PURRNET_SUPPORT
            PlayStagedExactAddressableStartEvent(id);
#endif
            InvokeExactCommittedSceneCallbacks(onPreSceneLoaded, id, "pre-load");
            InvokeExactCommittedSceneCallbacks(onSceneLoaded, id, "load");
            InvokeExactCommittedSceneCallbacks(onPostSceneLoaded, id, "post-load");
#if ADDRESSABLES_PURRNET_SUPPORT
            PlayStagedExactAddressableLoadedEvent(id);
#endif
        }

        private void InvokeExactCommittedSceneCallbacks(
            OnSceneActionEvent callbacks,
            SceneID id,
            string phase)
        {
            if (callbacks == null)
                return;

            var invocationList = callbacks.GetInvocationList();
            for (var i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((OnSceneActionEvent)invocationList[i]).Invoke(id, _asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogError(
                        $"Exact SceneID {id} {phase} observer failed after commit: {e.Message}");
                    PurrLogger.LogException(e);
                }
            }
        }

        private bool ValidateExactRegisteredSceneTopology(ISet<SceneID> targetScenes)
        {
            var registeredIds = new HashSet<SceneID>();
            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var id = _rawScenes[i];
                if (!registeredIds.Add(id))
                {
                    FailTransferReconciliation(
                        $"Retained scene registry contains duplicate SceneID {id} entries.");
                    return false;
                }

                if (!_scenes.TryGetValue(id, out var state) ||
                    !state.scene.IsValid() || !state.scene.isLoaded ||
                    !_idToScene.TryGetValue(state.scene, out var mappedId) || mappedId != id)
                {
                    FailTransferReconciliation(
                        $"Retained SceneID {id} does not have a unique, loaded Unity scene binding.");
                    return false;
                }

                if (!RequiresStaleKeptSceneHierarchyRetirement(
                        targetScenes.Contains(id),
                        ShouldKeepLocalSceneDuringTransfer(state.scene)))
                    continue;

                if (!_networkManager.TryGetModule<HierarchyFactory>(false, out var hierarchyFactory))
                {
                    FailTransferReconciliation(
                        $"Stale bootstrap scene '{state.scene.name}' (SceneID {id}) has no client " +
                        "hierarchy factory available for pure retirement preflight.");
                    return false;
                }

                if (!hierarchyFactory.TryPreflightExactStaleSceneRetirement(
                        id, state.scene, false, out var retirementFailure))
                {
                    FailTransferReconciliation(
                        $"Stale bootstrap scene '{state.scene.name}' (SceneID {id}) cannot retire " +
                        $"its hierarchy in place: {retirementFailure}.");
                    return false;
                }
            }

            if (registeredIds.Count != _scenes.Count || registeredIds.Count != _idToScene.Count)
            {
                FailTransferReconciliation(
                    "Retained scene registry is not one-to-one across SceneID and Unity scene bindings.");
                return false;
            }

            return true;
        }

        private static SceneID GetSceneActionId(SceneAction action)
        {
            return action.type switch
            {
                SceneActionType.Load => action.loadSceneAction.sceneID,
                SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                SceneActionType.Unload => action.unloadSceneAction.sceneID,
                SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                _ => default
            };
        }

        private bool TryReconcileLoadedTransferScene(
            LoadSceneAction loadAction,
            int buildIndex,
            ICollection<SceneID> retainedSceneEvents)
        {
            var loadedMatches = GetLoadedBuildScenes(buildIndex);
            var hasStableAuthoritativeBinding = false;

            if (_scenes.TryGetValue(loadAction.sceneID, out var existing))
            {
                var existingIsLoaded = existing.scene.IsValid() && existing.scene.isLoaded;
                var identityMatches = existingIsLoaded && existing.scene.buildIndex == buildIndex;
#if ADDRESSABLES_PURRNET_SUPPORT
                identityMatches = identityMatches && !HasAddressableSceneRegistration(loadAction.sceneID);
#endif
                var topologyIsStable = existingIsLoaded && !IsSceneUnloading(existing.scene);
                var reconciledSettings = loadAction.parameters;
                var repairedRetainedMetadata = false;
                var usedLocalPhysicsFallback = false;
                var immutableSettingsMatch = !_requiresTransferReconciliation
                    ? AreLoadedSceneSettingsCompatible(existing.settings, loadAction.parameters)
                    : existingIsLoaded && TryReconcileLoadedSceneSettings(
                        existing.scene, existing.settings, loadAction.parameters,
                        out reconciledSettings, out repairedRetainedMetadata,
                        out usedLocalPhysicsFallback, out _);
                hasStableAuthoritativeBinding =
                    identityMatches && topologyIsStable &&
                    (!_requiresTransferReconciliation || immutableSettingsMatch);

                if (hasStableAuthoritativeBinding)
                {
                    _scenes[loadAction.sceneID] = new SceneState(existing.scene, reconciledSettings);
                    if (repairedRetainedMetadata)
                    {
                        PurrLogger.LogWarning(
                            $"Repaired stale LocalPhysicsMode metadata for retained SceneID " +
                            $"{loadAction.sceneID} from its loaded Unity scene.");
                    }
                    if (usedLocalPhysicsFallback)
                    {
                        WarnBestEffortPhysicsFallback(
                            loadAction.sceneID, existing.scene,
                            loadAction.parameters.physicsMode, reconciledSettings.physicsMode);
                    }
                    _sceneActionScenes.Add(loadAction.sceneID);
                    retainedSceneEvents.Add(loadAction.sceneID);
                    return true;
                }

                if (ShouldRejectLoadedTargetReplacement(
                        _requiresTransferReconciliation,
                        existingIsLoaded,
                        identityMatches,
                        immutableSettingsMatch,
                        topologyIsStable))
                {
                    FailTransferReconciliation(
                        $"Loaded scene registered for authoritative SceneID {loadAction.sceneID} does not " +
                        "match its build identity, immutable LocalPhysicsMode, or stable topology. Exact " +
                        "reconciliation cannot unload or replace retained target state.");
                    return true;
                }

                if (!RemoveExistingBuildTransferScene(existing, loadAction.sceneID))
                    return true;
            }

            if (_requiresTransferReconciliation && IsLoadedTargetSelectionAmbiguous(
                    loadedMatches.Count, hasStableAuthoritativeBinding))
            {
                FailTransferReconciliation(
                    $"Authoritative scene {loadAction.sceneID} matches {loadedMatches.Count} loaded " +
                    $"instances of build index {buildIndex}; no stable SceneID binding selects one " +
                    "retained instance deterministically.");
                return true;
            }

            if (loadedMatches.Count == 0)
                return false;

            var loadedScene = loadedMatches[0];
            if (IsSceneUnloading(loadedScene))
            {
                FailTransferReconciliation(
                    $"Loaded build scene '{loadedScene.name}' for authoritative SceneID " +
                    $"{loadAction.sceneID} is already unloading and cannot be rebound safely.");
                return _requiresTransferReconciliation;
            }

            if (_idToScene.TryGetValue(loadedScene, out var retainedId) &&
                _scenes.TryGetValue(retainedId, out var retainedState))
            {
                if (_requiresTransferReconciliation &&
                    retainedState.scene.handle != loadedScene.handle)
                {
                    FailTransferReconciliation(
                        $"Loaded build scene '{loadedScene.name}' has inconsistent retained SceneID " +
                        $"topology and cannot be rebound to authoritative SceneID {loadAction.sceneID}.");
                    return true;
                }

#if ADDRESSABLES_PURRNET_SUPPORT
                var retainedIsAddressable = HasAddressableSceneRegistration(retainedId);
#else
                const bool retainedIsAddressable = false;
#endif
                if (_requiresTransferReconciliation &&
                    !IsExactSceneDescriptorIdentityMatch(
                        retainedId, loadAction.sceneID, retainedIsAddressable, false))
                {
                    FailTransferReconciliation(
                        $"Loaded scene '{loadedScene.name}' is retained under SceneID {retainedId}, " +
                        $"but the new authority describes build SceneID {loadAction.sceneID}. " +
                        "Exact reconciliation cannot re-key a live scene without replaying lifecycle state.");
                    return true;
                }

                var reconciledSettings = loadAction.parameters;
                var repairedRetainedMetadata = false;
                var usedLocalPhysicsFallback = false;
                if (_requiresTransferReconciliation &&
                    !TryReconcileLoadedSceneSettings(
                        loadedScene, retainedState.settings, loadAction.parameters,
                        out reconciledSettings, out repairedRetainedMetadata,
                        out usedLocalPhysicsFallback, out var physicsFailure))
                {
                    FailTransferReconciliation(
                        $"Authoritative scene {loadAction.sceneID} has incompatible physical topology: " +
                        physicsFailure);
                    return true;
                }

                if (_requiresTransferReconciliation && repairedRetainedMetadata)
                {
                    PurrLogger.LogWarning(
                        $"Repaired stale LocalPhysicsMode metadata for retained SceneID " +
                        $"{loadAction.sceneID} from its loaded Unity scene.");
                }

                if (_requiresTransferReconciliation && usedLocalPhysicsFallback)
                {
                    WarnBestEffortPhysicsFallback(
                        loadAction.sceneID, loadedScene,
                        loadAction.parameters.physicsMode, reconciledSettings.physicsMode);
                }

                if (_requiresTransferReconciliation)
                    loadAction.parameters = reconciledSettings;
            }
            else if (_requiresTransferReconciliation)
            {
                FailTransferReconciliation(
                    $"Loaded scene '{loadedScene.name}' matches authoritative SceneID " +
                    $"{loadAction.sceneID} by build path but has no consistent retained descriptor.");
                return true;
            }

            BindLoadedTransferScene(loadedScene, loadAction.parameters, loadAction.sceneID);
            retainedSceneEvents.Add(loadAction.sceneID);
            return true;
        }

        private static List<Scene> GetLoadedBuildScenes(int buildIndex)
        {
            var matches = new List<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.buildIndex == buildIndex)
                    matches.Add(scene);
            }

            return matches;
        }

        private bool RemoveExistingBuildTransferScene(SceneState state, SceneID authoritativeId)
        {
            if (ShouldKeepLocalSceneDuringTransfer(state.scene))
            {
                if (_requiresTransferReconciliation)
                {
                    FailTransferReconciliation(
                        $"Bootstrap scene '{state.scene.name}' registered for SceneID {authoritativeId} " +
                        "does not match the authoritative build or immutable LocalPhysicsMode.");
                    return false;
                }

                RemoveScene(state.scene, true);
                return true;
            }

            RemoveScene(state.scene, true);
            if (state.scene.IsValid() && state.scene.isLoaded)
            {
                TrackSceneUnload(state.scene, SceneManager.UnloadSceneAsync(state.scene),
                    $"replace scene '{state.scene.name}' for SceneID {authoritativeId}");
            }

            return true;
        }

        private bool IsBuildScenePending(LoadSceneAction loadAction)
        {
            var matchingLoadCallbacks = 0;
            PendingSceneOperation? matchingSceneId = null;

            for (var i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];
                if (operation.scenePathHash == loadAction.scenePathHash &&
                    operation.settings.mode == loadAction.parameters.mode)
                    matchingLoadCallbacks++;

                if (operation.idToAssign == loadAction.sceneID)
                    matchingSceneId = operation;
            }

            if (!matchingSceneId.HasValue)
                return false;

            var pending = matchingSceneId.Value;
            if (pending.operation == null ||
                pending.scenePathHash != loadAction.scenePathHash ||
                !ArePendingSceneSettingsCompatible(pending.settings, loadAction.parameters))
            {
                FailTransferReconciliation(
                    $"SceneID {loadAction.sceneID} is already loading with settings that do not " +
                    "match the authoritative scene descriptor; Unity scene loads cannot be cancelled safely.");
                return true;
            }

            if (_requiresTransferReconciliation && matchingLoadCallbacks != 1)
            {
                FailTransferReconciliation(
                    $"Scene path hash '{loadAction.scenePathHash}' has {matchingLoadCallbacks} indistinguishable " +
                    "pending loads. Unity's sceneLoaded callback cannot safely assign their SceneIDs.");
            }

            return true;
        }

        private void BindLoadedTransferScene(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            if (_idToScene.TryGetValue(scene, out var oldId))
            {
                if (oldId == id)
                {
                    _scenes[id] = new SceneState(scene, settings);
                    _sceneActionScenes.Add(id);
                    _scenesToTriggerUnloadEvent.Remove(id);
                    return;
                }

                RemoveScene(scene, true);
            }

            if (_scenes.TryGetValue(id, out var oldState))
                RemoveScene(oldState.scene, true);

            _scenes[id] = new SceneState(scene, settings);
            _idToScene[scene] = id;
            _sceneActionScenes.Add(id);
            if (!_rawScenes.Contains(id))
                _rawScenes.Add(id);

            _scenesToTriggerUnloadEvent.Remove(id);
        }

        private void RemoveStaleTransferScenes(
            HashSet<SceneID> targetScenes,
            IReadOnlyDictionary<SceneID, LoadSceneAction> targetBuildScenes,
            IReadOnlyCollection<Scene> preplannedRetainedScenes = null,
            ISet<SceneID> deferredAddressableUnloads = null)
        {
            for (var i = _pendingOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingOperations[i];
                if (targetBuildScenes.TryGetValue(operation.idToAssign, out var target) &&
                    operation.scenePathHash == target.scenePathHash &&
                    ArePendingSceneSettingsCompatible(operation.settings, target.parameters))
                {
                    continue;
                }

                if (_requiresTransferReconciliation)
                {
                    FailTransferReconciliation(
                        $"A stale load for SceneID {operation.idToAssign} is still in flight; " +
                        "Unity scene loads cannot be cancelled safely.");
                    return;
                }

                _pendingOperations.RemoveAt(i);
            }

            if (_requiresTransferReconciliation)
            {
                var retainedPhysicalScenes = new List<Scene>();
                var retainedScenes = new HashSet<Scene>();
                if (preplannedRetainedScenes != null)
                {
                    foreach (var scene in preplannedRetainedScenes)
                    {
                        if (scene.IsValid() && retainedScenes.Add(scene))
                            retainedPhysicalScenes.Add(scene);
                    }
                }

                for (var i = 0; i < _rawScenes.Count; i++)
                {
                    var id = _rawScenes[i];
                    if (targetScenes.Contains(id) || !_scenes.TryGetValue(id, out var state) ||
                        (deferredAddressableUnloads != null && deferredAddressableUnloads.Contains(id)) ||
                        !ShouldKeepLocalSceneDuringTransfer(state.scene))
                        continue;

                    if (retainedScenes.Add(state.scene))
                        retainedPhysicalScenes.Add(state.scene);
                }

                if (!TryRetireExactStaleSceneHierarchies(
                        retainedPhysicalScenes, false, out var retirementFailure))
                {
                    FailTransferReconciliation(
                        "Stale retained scene hierarchies changed before exact cleanup: " +
                        retirementFailure);
                    return;
                }

                if (!TryDetachRetainedPhysicalSceneRegistrations(
                        retainedPhysicalScenes, out var detachFailure, true))
                {
                    FailTransferReconciliation(
                        "Stale retained scene registrations changed before exact cleanup: " +
                        detachFailure);
                    return;
                }
            }

            for (var i = _rawScenes.Count - 1; i >= 0; i--)
            {
                var id = _rawScenes[i];
                if (targetScenes.Contains(id))
                    continue;

                if (deferredAddressableUnloads != null && deferredAddressableUnloads.Contains(id))
                    continue;

                if (!_scenes.TryGetValue(id, out var state))
                    continue;

                var keepPhysicalScene = ShouldKeepLocalSceneDuringTransfer(state.scene);
                if (keepPhysicalScene)
                {
                    DetachRetainedPhysicalSceneRegistration(state.scene);
                }
                else
                {
                    RemoveScene(state.scene, true);
                    if (state.scene.IsValid() && state.scene.isLoaded)
                    {
                        TrackSceneUnload(state.scene, SceneManager.UnloadSceneAsync(state.scene),
                            $"remove stale SceneID {id}");
                    }
                }
            }
        }

        private bool TryRetireExactStaleSceneHierarchies(
            IReadOnlyList<Scene> scenes, bool asServer, out string failure)
        {
            failure = null;
            if (scenes == null || scenes.Count == 0)
                return true;

            if (!_networkManager.TryGetModule<HierarchyFactory>(asServer, out var hierarchyFactory))
            {
                failure = $"the {(asServer ? "server" : "client")} hierarchy factory is unavailable";
                return false;
            }

            var retirements = new List<KeyValuePair<SceneID, Scene>>(scenes.Count);
            var seenIds = new HashSet<SceneID>();
            for (var i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                if (!scene.IsValid() || !scene.isLoaded ||
                    !_idToScene.TryGetValue(scene, out var sceneId) ||
                    !_scenes.TryGetValue(sceneId, out var state) || state.scene != scene ||
                    !seenIds.Add(sceneId))
                {
                    failure = $"retained physical scene '{scene.name}' has no unique loaded SceneID binding";
                    return false;
                }

                if (!hierarchyFactory.TryPreflightExactStaleSceneRetirement(
                        sceneId, scene, asServer, out failure))
                {
                    failure = $"SceneID {sceneId}: {failure}";
                    return false;
                }

                retirements.Add(new KeyValuePair<SceneID, Scene>(sceneId, scene));
            }

            for (var i = 0; i < retirements.Count; i++)
            {
                var retirement = retirements[i];
                if (hierarchyFactory.TryRetireExactStaleSceneHierarchy(
                        retirement.Key, retirement.Value, asServer, out failure))
                    continue;

                failure = $"SceneID {retirement.Key}: {failure}";
                return false;
            }

            failure = null;
            return true;
        }

        private bool ShouldKeepLocalSceneDuringTransfer(Scene scene)
        {
            if (!scene.IsValid())
                return false;

            if (IsDontDestroyOnLoadScene(scene))
                return true;

            if (_networkManager.gameObject.scene.handle == scene.handle)
                return true;

            var originalScene = _networkManager.originalScene;
            return originalScene.IsValid() && originalScene.handle == scene.handle;
        }

        private void OnSceneActionsBatch(PlayerID player, SceneActionsBatch data, bool asServer)
        {
            if (_requiresTransferReconciliation && _deferredExactIncrementalActions != null)
            {
                if (data.actions != null && data.actions.Count > 0)
                    _deferredExactIncrementalActions.AddRange(data.actions);
                return;
            }

            HandleScenes(data.actions);
        }

        private void HandleScenes(List<SceneAction> actions)
        {
            if (_networkManager.isServer || _asServer)
            {
                var serverModule = _networkManager.GetModule<ScenesModule>(true);
                for (var i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];

                    switch (action.type)
                    {
                        case SceneActionType.Load:
                        {
                            if (_scenes.ContainsKey(action.loadSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.loadSceneAction.sceneID, out var state))
                            {
                                _sceneActionScenes.Add(action.loadSceneAction.sceneID);
                                AddScene(state.scene, state.settings, action.loadSceneAction.sceneID);
                            }
                            break;
                        }
                        case SceneActionType.LoadAddressable:
                        {
                            if (_scenes.ContainsKey(action.loadAddressableSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.loadAddressableSceneAction.sceneID, out var state))
                            {
                                _sceneActionScenes.Add(action.loadAddressableSceneAction.sceneID);
#if ADDRESSABLES_PURRNET_SUPPORT
                                CopyPromotedListenAddressableMetadata(
                                    serverModule, action.loadAddressableSceneAction.sceneID);
#endif
                                AddScene(state.scene, state.settings, action.loadAddressableSceneAction.sceneID);
                            }
                            break;
                        }
                        case SceneActionType.Unload:
                        {
                            var id = action.unloadSceneAction.sceneID;
                            if (!_scenes.TryGetValue(id, out var localState))
                                continue;

#if ADDRESSABLES_PURRNET_SUPPORT
                            UnregisterAddressableScene(id);
#endif
                            RemoveScene(localState.scene);
                            break;
                        }

                        case SceneActionType.SetActive:
                        default:
                            break;
                    }
                }

                return;
            }

            for (var i = 0; i < actions.Count; i++)
                _actionsQueue.Enqueue(actions[i]);

            HandleNextSceneAction();
        }

        private static int SceneNameToBuildIndex(string name)
        {
            var bIdxCount = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < bIdxCount; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                if (sceneName == name)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void EnsureSceneHashCacheBuilt()
        {
            if (_sceneHashCacheBuilt)
                return;

            _sceneHashCacheBuilt = true;

            var count = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var hash = Hash.Hash(path);
                _buildIndexToHash[i] = hash;
                if (_hashToBuildIndex.ContainsKey(hash))
                    _ambiguousBuildSceneHashes.Add(hash);
                _hashToBuildIndex[hash] = i;
            }
        }

        private static uint ScenePathHashFromBuildIndex(int buildIndex)
        {
            EnsureSceneHashCacheBuilt();
            return _buildIndexToHash[buildIndex];
        }

        private static int BuildIndexFromScenePathHash(uint scenePathHash)
        {
            EnsureSceneHashCacheBuilt();
            return _hashToBuildIndex.GetValueOrDefault(scenePathHash, -1);
        }

        private static bool IsBuildScenePathHashAmbiguous(uint scenePathHash)
        {
            EnsureSceneHashCacheBuilt();
            return _ambiguousBuildSceneHashes.Contains(scenePathHash);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(sceneIndex, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        public AsyncOperation LoadSceneAsync(string sceneName, PurrSceneSettings settings)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, settings);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneParameters parameters)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            return LoadSceneAsync(sceneIndex, new PurrSceneSettings
            {
                mode = parameters.loadSceneMode,
                physicsMode = parameters.localPhysicsMode,
                isPublic = true
            });
        }

        public SceneID lastSceneId => new((ushort)(_nextSceneID - 1));

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            ThrowIfExactPromotedListenSceneMutationIsFenced(
                $"load build scene {sceneIndex}");

            var idToAssign = GetNextID();
            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);

            if (settings.mode == LoadSceneMode.Single)
            {
                if (TryGetSceneID(_networkManager.gameObject.scene, out var nmId) &&
                    TryGetSceneState(nmId, out var nmScene))
                {
                    if (!IsDontDestroyOnLoadScene(nmScene.scene))
                    {
                        PurrLogger.LogError("Network manager scene is not DontDestroyOnLoad and you are trying to" +
                                            " load a new scene with LoadSceneMode.Single");
                    }
                }

                for (int i = 0; i < _rawScenes.Count; i++)
                {
                    bool isDontDestroyOnLoad = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoad)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            var scenePathHash = ScenePathHashFromBuildIndex(sceneIndex);

            _history.AddLoadAction(new LoadSceneAction
            {
                scenePathHash = scenePathHash,
                sceneID = idToAssign,
                parameters = settings
            });
            _sceneActionScenes.Add(idToAssign);

            var op = SceneManager.LoadSceneAsync(sceneIndex, parameters);
            var operation = new PendingSceneOperation
            {
                buildIndex = sceneIndex,
                scenePathHash = scenePathHash,
                settings = settings,
                idToAssign = idToAssign,
                operation = op
            };

            _pendingOperations.Add(operation);
            RegisterBuildSceneCompletionCallback(operation);

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingOperations.Add(operation);
                clientModule.RegisterBuildSceneCompletionCallback(operation);
            }

            return op;
        }

        /// <summary>
        /// Unloads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">Name of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with name '{sceneName}' not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="buildIndex">Build index of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(int buildIndex, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByBuildIndex(buildIndex);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with build index {buildIndex} not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its Scene object - Must be in build settings
        /// </summary>
        /// <param name="scene">The Scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return null;
            }

            if (_networkManager.gameObject.scene == scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return null;
            }

            if (!_idToScene.TryGetValue(scene, out var sceneIndex))
            {
                PurrLogger.LogError($"Scene {scene.name} not found in scenes list");
                return null;
            }

            ThrowIfExactPromotedListenSceneMutationIsFenced(
                $"unload SceneID {sceneIndex}");

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneIndex, options = options });
#if ADDRESSABLES_PURRNET_SUPPORT
            if (TryUnloadAddressableScene(sceneIndex, options))
                return null;
#endif
            var op = TrackSceneUnload(scene, SceneManager.UnloadSceneAsync(scene, options),
                $"server unload of scene '{scene.name}'");
            RemoveScene(scene);

            return op;
        }

        /// <summary>
        /// Unloads a scene asynchronously by its SceneID.
        /// Use this when you have the SceneID from onSceneLoaded or sceneStates.
        /// </summary>
        /// <param name="sceneId">The SceneID of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public void UnloadSceneAsync(SceneID sceneId, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return;
            }

            if (!_scenes.TryGetValue(sceneId, out var state))
            {
                PurrLogger.LogError($"Scene with ID {sceneId} not found in scenes list");
                return;
            }

            if (_networkManager.gameObject.scene == state.scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return;
            }

            UnloadSceneAsync(state.scene, options);
        }

        static readonly List<SceneAction> _playerFilteredActions = new List<SceneAction>();

        private void FilterActionsForPlayer(PlayerID player, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        private void FilterActionsForPlayerBySceneID(PlayerID player, SceneID id, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (target != id)
                    continue;

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        partial void ProcessCompletedAddressableLoads();
        partial void PollCompletedAddressableUnloads();
        partial void RebuildAddressableHistoryFromLoadedScenes();
        partial void CollectAddressablePromotionSceneCandidates(
            List<PromotionSceneCandidate> candidates);
        partial void AddLoadedAddressableSceneToHistory(SceneID id, bool isPromotionBase);
        partial void CopyPromotedListenAddressableMetadata(ScenesModule serverModule, SceneID id);
        partial void ValidatePromotedListenAddressableMetadata(
            ScenesModule serverModule,
            SceneID id,
            ref string failure);
        partial void ValidateExactAddressableAuthoritySwitchState(ref string failure);
        partial void ValidateExactAddressableAuthoritySwitchScene(
            SceneID id,
            ref bool isAddressable,
            ref string failure);
        partial void UnregisterStagedExactAddressableMetadata(
            HashSet<SceneID> addressableScenes);
        partial void RetireStagedExactAddressableScenes();

        public void FixedUpdate()
        {
            if (_transferReconciliationFailure == null)
                ProcessCompletedAddressableLoads();
            if (_pendingSceneUnloads.Count > 0
#if ADDRESSABLES_PURRNET_SUPPORT
                || _pendingAddressableUnloads.Count > 0
#endif
               )
                PollTransferReconciliationOperations();
            if (_transferReconciliationFailure == null)
            {
                HandleNextSceneAction();
                if (_deferredExactTransferManifest != null)
                    TryCommitDeferredExactTransferManifest();
            }

            if (_history.hasUnflushedActions)
                FlushActions();

            if (_transferReconciliationFailure == null &&
                _scenesToTriggerUnloadEvent.Count > 0)
            {
                for (var i = 0; i < _scenesToTriggerUnloadEvent.Count; i++)
                {
                    var scene = _scenesToTriggerUnloadEvent[i];
                    PlayUnloadEventsForScene(scene);
                }
                _scenesToTriggerUnloadEvent.Clear();
            }
        }

        private void FlushActions()
        {
            var delta = _history.GetDelta();

            for (var i = 0; i < _players.players.Count; i++)
            {
                var player = _players.players[i];

                _playerFilteredActions.Clear();

                FilterActionsForPlayer(player, delta.actions, _playerFilteredActions);

                if (_playerFilteredActions.Count > 0)
                {
                    _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
                }
            }

            _history.Flush();
        }

        private readonly List<AsyncOperation> _pendingUnloads = new List<AsyncOperation>();
        private CleanupStage _cleanupStage;

        enum CleanupStage
        {
            None,
            Skip,
            LoadEmptyScene,
            WaitOneFrame,
            UnloadScenes,
            UnloadScenesOnly,
            LoadOGScene,
            LoadOGSceneOnly,
            UnloadEmptyScene,
            ResetScene,
            Done
        }

        private Scene? _emptyScene;
        private AsyncOperation _ogSceneLoad;

        public bool Cleanup()
        {
            if (!_wasSetup)
                return true;

            var rules = _networkManager.networkRules;

            if (rules && !rules.ShouldCleanupScenesOnDisconnect())
                return true;

            if (ApplicationContext.isQuitting)
                return true;

            if (!_networkManager.isOffline)
                return true;

            if (_pendingOperations.Count > 0)
                return false;

            switch (_cleanupStage)
            {
                case CleanupStage.None:
                    {
                        if (rules.SceneCleanupModeOnDisconnect() == SceneCleanupMode.All)
                        {
                            if (_networkManager.originalSceneBuildIndex == -1)
                            {
                                PurrLogger.LogError("Unable to load original scene on cleanup because its index is invalid");
                                _cleanupStage = CleanupStage.Skip;
                            }
                            else
                                _cleanupStage = CleanupStage.LoadOGSceneOnly;
                        }
                        else
                        {
                            _cleanupStage = _networkManager.IsDontDestroyOnLoad()
                                ? CleanupStage.LoadEmptyScene
                                : CleanupStage.UnloadScenesOnly;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module) && module._wasSetup)
                            module._cleanupStage = CleanupStage.Skip;

                        return false;
                    }
                case CleanupStage.Skip: return false;
                case CleanupStage.Done: return true;
                case CleanupStage.LoadEmptyScene:
                    {
                        _cleanupStage = CleanupStage.WaitOneFrame;
                        _emptyScene = SceneManager.CreateScene("EmptyScene");
                        return false;
                    }
                case CleanupStage.WaitOneFrame:
                    {
                        _cleanupStage = CleanupStage.UnloadScenes;
                        return false;
                    }
                case CleanupStage.UnloadScenes:
                    {
                        if (UnloadAllScenesCleanup(false))
                            _cleanupStage = CleanupStage.LoadOGScene;
                        return false;
                    }
                case CleanupStage.UnloadScenesOnly:
                    {
                        if (UnloadAllScenesCleanup(true))
                        {
                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                                module._cleanupStage = CleanupStage.Done;
                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGScene:
                    {
                        if (_ogSceneLoad == null)
                        {
                            if (_networkManager.originalSceneBuildIndex != -1)
                            {
                                _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex,
                                    LoadSceneMode.Additive);

                                if (_ogSceneLoad != null)
                                    _ogSceneLoad.allowSceneActivation = true;
                            }
                            else
                            {
                                _cleanupStage = CleanupStage.UnloadEmptyScene;
                            }
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _cleanupStage = CleanupStage.ResetScene;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGSceneOnly:
                    {
                        if (_ogSceneLoad == null)
                        {
                            _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex);

                            if (_ogSceneLoad != null)
                                _ogSceneLoad.allowSceneActivation = true;
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _scenes.Clear();

                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                                module._cleanupStage = CleanupStage.Done;

                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.ResetScene:
                    {
                        var activeScene = SceneManager.GetSceneByBuildIndex(_networkManager.originalSceneBuildIndex);
                        _networkManager.ResetOriginalScene(activeScene);
                        _cleanupStage = CleanupStage.UnloadEmptyScene;
                        return false;
                    }
                case CleanupStage.UnloadEmptyScene:
                    {
                        if (_emptyScene != null)
                        {
                            if (_emptyScene.Value.IsValid())
                                SceneManager.UnloadSceneAsync(_emptyScene.Value);
                            _emptyScene = null;
                            return false;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                            module._cleanupStage = CleanupStage.Done;
                        _cleanupStage = CleanupStage.Done;
                        return false;
                    }
                default: return true;
            }
        }

        private bool UnloadAllScenesCleanup(bool keepNetworkManager)
        {
            // unload all scenes that aren't the network manager scene
            if (_scenes.Count > 0)
            {
                _pendingUnloads.Clear();

                foreach (var (_, scene) in _scenes)
                {
                    var unityScene = scene.scene;

                    if (keepNetworkManager && _networkManager.gameObject.scene.handle == unityScene.handle)
                        continue;

                    if (!unityScene.IsValid())
                        continue;

                    if (!unityScene.isLoaded)
                        continue;

                    if (IsDontDestroyOnLoadScene(unityScene))
                        continue;

                    _pendingUnloads.Add(SceneManager.UnloadSceneAsync(unityScene));
                }

                _scenes.Clear();
            }

            if (_pendingUnloads.Count > 0)
            {
                for (int i = 0; i < _pendingUnloads.Count; i++)
                {
                    if (_pendingUnloads[i] != null && !_pendingUnloads[i].isDone)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="scene">Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneId)
        {
            if (!_idToScene.TryGetValue(scene, out sceneId) ||
                (_stagedExactScenes.Count > 0 && _stagedExactScenes.ContainsKey(sceneId)))
            {
                sceneId = default;
                return false;
            }

            return true;
        }


        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="buildIndex">BuildIndex of Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetScene(int buildIndex, out SceneID sceneId)
        {
            var hasStagedExactScenes = _stagedExactScenes.Count > 0;
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (hasStagedExactScenes && _stagedExactScenes.ContainsKey(_rawScenes[i]))
                    continue;

                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                    {
                        sceneId = _rawScenes[i];
                        return true;
                    }
                }
            }

            sceneId = default;
            return false;
        }

        /// <summary>
        /// Checks whether a scene is loaded on the network
        /// </summary>
        /// <param name="buildIndex">Build index of scene to check</param>
        /// <returns>Whether the scene is loaded on the network or not</returns>
        public bool IsSceneLoaded(int buildIndex)
        {
            var hasStagedExactScenes = _stagedExactScenes.Count > 0;
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (hasStagedExactScenes && _stagedExactScenes.ContainsKey(_rawScenes[i]))
                    continue;

                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                        return true;
                }
            }

            return false;
        }
    }
}
