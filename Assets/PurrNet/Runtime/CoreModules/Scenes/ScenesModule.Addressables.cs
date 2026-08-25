#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public partial class ScenesModule
    {
        public struct PendingAddressableSceneOperation
        {
            public string guid;
            public AsyncOperationHandle<SceneInstance> handle;
            public SceneID idToAssign;
            public PurrSceneSettings settings;
            public bool ownsHandle;
        }

        private struct PendingAddressableSceneUnload
        {
            public AsyncOperationHandle<SceneInstance> handle;
            public bool releaseWhenDone;
            public string context;
        }

        private struct StagedExactAddressableScene
        {
            public string guid;
            public AsyncOperationHandle<SceneInstance> handle;
            public bool ownsHandle;
        }

        private readonly List<PendingAddressableSceneOperation> _pendingAddressableOperations =
            new List<PendingAddressableSceneOperation>();

        private readonly List<PendingAddressableSceneUnload> _pendingAddressableUnloads =
            new List<PendingAddressableSceneUnload>();

        private readonly Dictionary<SceneID, StagedExactAddressableScene>
            _stagedExactAddressableScenes =
                new Dictionary<SceneID, StagedExactAddressableScene>();

        private readonly Dictionary<SceneID, AsyncOperationHandle<SceneInstance>> _addressableSceneHandles =
            new Dictionary<SceneID, AsyncOperationHandle<SceneInstance>>();

        private readonly Dictionary<SceneID, string> _addressableSceneIdToGuid =
            new Dictionary<SceneID, string>();

        private readonly Dictionary<string, List<SceneID>> _addressableSceneGuidToIds =
            new Dictionary<string, List<SceneID>>(StringComparer.OrdinalIgnoreCase);

        public delegate void OnAddressableSceneEvent(SceneID sceneId, string guid, bool asServer);

        /// <summary>
        /// Fired when an Addressable scene begins loading.
        /// </summary>
        public event OnAddressableSceneEvent onAddressableSceneStartLoading;

        /// <summary>
        /// Fired when an Addressable scene has finished loading and is registered.
        /// </summary>
        public event OnAddressableSceneEvent onAddressableSceneLoaded;

        /// <summary>
        /// Registers a completion callback on the addressable scene handle so that the
        /// scene is processed as soon as it loads, rather than waiting for the next
        /// FixedUpdate. This prevents a race condition where scene objects' Start()
        /// fires before PurrNet has processed the loaded scene.
        /// </summary>
        private void RegisterAddressableCompletionCallback(AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid())
            {
                FailTransferReconciliation("Addressables returned an invalid scene-load handle.");
                return;
            }

            handle.Completed += _ => ProcessCompletedAddressableLoads();
        }

        partial void ProcessCompletedAddressableLoads()
        {
            for (var i = _pendingAddressableOperations.Count - 1; i >= 0; i--)
            {
                var op = _pendingAddressableOperations[i];

                if (!op.handle.IsValid())
                {
                    FailTransferReconciliation(
                        $"Addressable scene {op.idToAssign} ('{op.guid}') lost its load handle before completion.");
                    _pendingAddressableOperations.RemoveAt(i);
                    continue;
                }

                if (!op.handle.IsDone)
                    continue;

                if (op.handle.Status == AsyncOperationStatus.Succeeded)
                {
                    var scene = op.handle.Result.Scene;
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        FailTransferReconciliation(
                            $"Addressables reported success for SceneID {op.idToAssign} ('{op.guid}'), " +
                            "but did not produce a loaded Unity scene.");
                        if (op.ownsHandle && op.handle.IsValid())
                            Addressables.Release(op.handle);
                        _pendingAddressableOperations.RemoveAt(i);
                        continue;
                    }

                    if (_requiresTransferReconciliation && _transferReconciliationFailure != null)
                    {
                        if (op.ownsHandle)
                            RetireOwnedExactAddressableSceneHandle(
                                op.handle, op.idToAssign,
                                $"retire Addressable SceneID {op.idToAssign} after reconciliation failure");

                        _pendingAddressableOperations.RemoveAt(i);
                        continue;
                    }

                    if (_requiresTransferReconciliation &&
                        _scenes.TryGetValue(op.idToAssign, out var existing) &&
                        existing.scene.handle != scene.handle)
                    {
                        FailTransferReconciliation(
                            $"Addressable scene '{op.guid}' cannot claim SceneID {op.idToAssign}; " +
                            $"that ID is already registered to '{existing.scene.name}'.");
                        if (op.ownsHandle)
                            RetireOwnedExactAddressableSceneHandle(
                                op.handle, op.idToAssign,
                                $"retire unclaimed Addressable SceneID {op.idToAssign}");
                        _pendingAddressableOperations.RemoveAt(i);
                        continue;
                    }

                    if (_requiresTransferReconciliation)
                    {
                        if (_stagedExactAddressableScenes.ContainsKey(op.idToAssign))
                        {
                            _pendingAddressableOperations.RemoveAt(i);
                            FailTransferReconciliation(
                                $"Addressable SceneID {op.idToAssign} completed more than once during exact reconciliation.");
                            if (op.ownsHandle)
                                RetireOwnedExactAddressableSceneHandle(
                                    op.handle, op.idToAssign,
                                    $"retire duplicate staged Addressable SceneID {op.idToAssign}");
                            continue;
                        }

                        _stagedExactAddressableScenes.Add(op.idToAssign,
                            new StagedExactAddressableScene
                            {
                                guid = op.guid,
                                handle = op.handle,
                                ownsHandle = op.ownsHandle
                            });
                        RegisterAddressableSceneHandle(op.idToAssign, op.guid, op.handle);

                        if (!TryStageExactScene(
                                scene, op.settings, op.idToAssign, out var stagingFailure))
                        {
                            _pendingAddressableOperations.RemoveAt(i);
                            FailTransferReconciliation(stagingFailure);
                            continue;
                        }

                        _pendingAddressableOperations.RemoveAt(i);
                        continue;
                    }

                    _sceneActionScenes.Add(op.idToAssign);
                    RegisterAddressableSceneHandle(op.idToAssign, op.guid, op.handle);
                    AddScene(scene, op.settings, op.idToAssign);
                    
                    onAddressableSceneLoaded?.Invoke(op.idToAssign, op.guid, _asServer);
                }
                else
                {
                    PurrLogger.LogError($"Addressable scene load failed: {op.handle.OperationException}");
                    FailTransferReconciliation(
                        $"Addressable scene {op.idToAssign} ('{op.guid}') failed to load.",
                        op.handle.OperationException);
                    if (op.ownsHandle && op.handle.IsValid())
                        Addressables.Release(op.handle);
                }

                _pendingAddressableOperations.RemoveAt(i);
            }
        }

        private void ProcessLoadAddressableAction(LoadAddressableSceneAction action)
        {
            // A reconnect delivers the same load action twice: once in the
            // first-join batch and once when the player is re-added to the
            // public scene. Loading again would duplicate the addressable scene
            // and clash with the already assigned SceneID.
            if (_scenes.ContainsKey(action.sceneID) || IsScenePending(action.sceneID))
            {
                _sceneActionScenes.Add(action.sceneID);
                return;
            }

            var guid = action.guid.value;
            if (string.IsNullOrEmpty(guid))
            {
                PurrLogger.LogError("LoadAddressableSceneAction has empty GUID");
                FailTransferReconciliation(
                    $"Authoritative Addressable scene {action.sceneID} has an empty GUID.");
                return;
            }

            var parameters = action.GetLoadSceneParameters();

            AsyncOperationHandle<SceneInstance> handle;

            try
            {
                handle = Addressables.LoadSceneAsync(guid, parameters, true, 100);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Error loading addressable scene: {e}");
                FailTransferReconciliation(
                    $"Addressables threw while loading authoritative scene {action.sceneID} ('{guid}').", e);
                return;
            }

            if (!handle.IsValid())
            {
                PurrLogger.LogError(
                    $"Addressables returned an invalid handle for scene {action.sceneID} ('{guid}').");
                FailTransferReconciliation(
                    $"Addressables did not start loading authoritative scene {action.sceneID} ('{guid}').");
                return;
            }

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = action.sceneID,
                settings = action.parameters,
                ownsHandle = true
            });
            _sceneActionScenes.Add(action.sceneID);

            RegisterAddressableCompletionCallback(handle);

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = action.sceneID,
                    settings = action.parameters,
                    ownsHandle = false
                });
                clientModule._sceneActionScenes.Add(action.sceneID);
                clientModule.RegisterAddressableCompletionCallback(handle);
            }

            if (!_requiresTransferReconciliation)
                onAddressableSceneStartLoading?.Invoke(action.sceneID, guid, _asServer);
        }

        partial void UnregisterStagedExactAddressableMetadata(
            HashSet<SceneID> addressableScenes)
        {
            foreach (var pair in _stagedExactAddressableScenes)
            {
                addressableScenes.Add(pair.Key);
                UnregisterAddressableScene(pair.Key);
            }
        }

        partial void RetireStagedExactAddressableScenes()
        {
            foreach (var pair in _stagedExactAddressableScenes)
            {
                var staged = pair.Value;
                if (!staged.ownsHandle)
                    continue;

                RetireOwnedExactAddressableSceneHandle(
                    staged.handle, pair.Key,
                    $"roll back unpublished Addressable SceneID {pair.Key}");
            }

            _stagedExactAddressableScenes.Clear();
        }

        private void RetireOwnedExactAddressableSceneHandle(
            AsyncOperationHandle<SceneInstance> handle,
            SceneID id,
            string context)
        {
            if (!handle.IsValid())
                return;

            if (handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded)
            {
                var loadedScene = handle.Result.Scene;
                if (loadedScene.IsValid() &&
                    (HasScene(loadedScene) || IsSceneUnloading(loadedScene) ||
                     ShouldPreserveExactAddressableRetirementScene(loadedScene)))
                {
                    PurrLogger.LogWarning(
                        $"Preserving protected or registered Unity scene '{loadedScene.name}' " +
                        $"while retiring exact Addressable SceneID {id} ({context}).");
                    return;
                }
            }

            var unload = Addressables.UnloadSceneAsync(
                handle, UnloadSceneOptions.None, false);
            TrackAddressableSceneUnload(unload, true, context);
        }

        internal bool ShouldPreserveExactAddressableRetirementScene(Scene scene) =>
            scene.IsValid() && ShouldKeepLocalSceneDuringTransfer(scene);

        private void CommitStagedExactAddressableScene(SceneID id)
        {
            _stagedExactAddressableScenes.Remove(id);
        }

        private void PlayStagedExactAddressableStartEvent(SceneID id)
        {
            if (!_addressableSceneIdToGuid.TryGetValue(id, out var guid))
                return;

            InvokeAddressableSceneEventSafely(
                onAddressableSceneStartLoading, id, guid, "start-loading");
        }

        private void PlayStagedExactAddressableLoadedEvent(SceneID id)
        {
            if (!_addressableSceneIdToGuid.TryGetValue(id, out var guid))
                return;

            InvokeAddressableSceneEventSafely(
                onAddressableSceneLoaded, id, guid, "loaded");
        }

        private void InvokeAddressableSceneEventSafely(
            OnAddressableSceneEvent callbacks,
            SceneID id,
            string guid,
            string phase)
        {
            if (callbacks == null)
                return;

            var invocationList = callbacks.GetInvocationList();
            for (var i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((OnAddressableSceneEvent)invocationList[i]).Invoke(id, guid, _asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogError(
                        $"Exact Addressable SceneID {id} {phase} observer failed after commit: {e.Message}");
                    PurrLogger.LogException(e);
                }
            }
        }

        private bool IsScenePendingAddressable(SceneID sceneId)
        {
            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                if (_pendingAddressableOperations[i].idToAssign == sceneId)
                    return true;
            }

            return false;
        }

        private bool IsAddressableScenePending(LoadAddressableSceneAction loadAction)
        {
            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                var operation = _pendingAddressableOperations[i];
                if (operation.idToAssign != loadAction.sceneID)
                    continue;

                var guid = loadAction.guid.value;
                if (!operation.handle.IsValid() ||
                    !string.Equals(operation.guid, guid, StringComparison.OrdinalIgnoreCase) ||
                    !ArePendingSceneSettingsCompatible(operation.settings, loadAction.parameters))
                {
                    FailTransferReconciliation(
                        $"SceneID {loadAction.sceneID} is already loading an Addressable scene that " +
                        "does not match the authoritative GUID or load settings; the load cannot be cancelled safely.");
                }

                return true;
            }

            return false;
        }

        partial void PollCompletedAddressableUnloads()
        {
            for (var i = _pendingAddressableUnloads.Count - 1; i >= 0; i--)
            {
                var pending = _pendingAddressableUnloads[i];
                if (!pending.handle.IsValid())
                {
                    FailTransferReconciliation(
                        $"Addressable scene unload lost its operation handle ({pending.context}).");
                    _pendingAddressableUnloads.RemoveAt(i);
                    continue;
                }

                if (!pending.handle.IsDone)
                    continue;

                if (pending.handle.Status != AsyncOperationStatus.Succeeded)
                {
                    FailTransferReconciliation(
                        $"Addressable scene unload failed ({pending.context}).",
                        pending.handle.OperationException);
                }

                if (pending.releaseWhenDone && pending.handle.IsValid())
                    Addressables.Release(pending.handle);

                _pendingAddressableUnloads.RemoveAt(i);
            }
        }

        private void TrackAddressableSceneUnload(
            AsyncOperationHandle<SceneInstance> handle,
            bool releaseWhenDone,
            string context)
        {
            if (!handle.IsValid())
            {
                FailTransferReconciliation(
                    $"Addressables did not start the required scene unload ({context}).");
                return;
            }

            _pendingAddressableUnloads.Add(new PendingAddressableSceneUnload
            {
                handle = handle,
                releaseWhenDone = releaseWhenDone,
                context = context
            });

            PollCompletedAddressableUnloads();
        }

        private bool TryUnloadAddressableScene(SceneID sceneId, UnloadSceneOptions options)
        {
            return TryRemoveAddressableScene(sceneId, options, false, false, out _);
        }

        /// <summary>
        /// Unloads an Addressable scene asynchronously by its SceneID.
        /// Use this instead of UnloadSceneAsync when you need to await an Addressable scene unload,
        /// since Addressables doesn't expose a Unity AsyncOperation for unloading.
        /// The returned handle is not auto-released, so it stays valid while you await it;
        /// call Addressables.Release on it once you are done.
        /// </summary>
        /// <param name="sceneId">The SceneID of the Addressable scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        /// <returns>The AsyncOperationHandle for the unload, or a default handle if invalid</returns>
        public AsyncOperationHandle<SceneInstance> UnloadAddressableSceneAsync(
            SceneID sceneId,
            UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return default;
            }

            if (!IsAddressableScene(sceneId))
            {
                PurrLogger.LogError($"Scene with ID {sceneId} is not a loaded Addressable scene");
                return default;
            }

            if (_scenes.TryGetValue(sceneId, out var state) && _networkManager.gameObject.scene == state.scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return default;
            }

            ThrowIfExactPromotedListenSceneMutationIsFenced(
                $"unload Addressable SceneID {sceneId}");

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneId, options = options });
            TryRemoveAddressableScene(sceneId, options, false, true, out var handle);

            return handle;
        }

        /// <summary>
        /// Returns true if the given SceneID was loaded through Addressables.
        /// </summary>
        public bool IsAddressableScene(SceneID sceneId)
        {
            return !_stagedExactScenes.ContainsKey(sceneId) &&
                   HasAddressableSceneRegistration(sceneId);
        }

        private bool HasAddressableSceneRegistration(SceneID sceneId) =>
            _addressableSceneHandles.ContainsKey(sceneId) ||
            _addressableSceneIdToGuid.ContainsKey(sceneId);

        private bool TryReconcileLoadedAddressableTransferScene(
            LoadAddressableSceneAction loadAction,
            ICollection<SceneID> retainedSceneEvents)
        {
            var guid = loadAction.guid.value;
            if (string.IsNullOrEmpty(guid))
                return false;

            var loadedMatches = GetLoadedAddressableSceneIds(guid);
            var hasStableAuthoritativeBinding = false;

            if (_scenes.TryGetValue(loadAction.sceneID, out var existing))
            {
                var existingIsLoaded = existing.scene.IsValid() && existing.scene.isLoaded;
                var identityMatches = IsLoadedAddressableScene(loadAction.sceneID, guid, existing);
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
                            $"Repaired stale LocalPhysicsMode metadata for retained Addressable " +
                            $"SceneID {loadAction.sceneID} from its loaded Unity scene.");
                    }
                    if (usedLocalPhysicsFallback)
                    {
                        WarnBestEffortPhysicsFallback(
                            loadAction.sceneID, existing.scene,
                            loadAction.parameters.physicsMode, reconciledSettings.physicsMode);
                    }
                    _sceneActionScenes.Add(loadAction.sceneID);
                    RegisterAddressableSceneGuid(loadAction.sceneID, guid);
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
                        $"Loaded Addressable scene registered for authoritative SceneID " +
                        $"{loadAction.sceneID} does not match its GUID, immutable LocalPhysicsMode, or " +
                        "stable topology. Exact reconciliation cannot unload or replace retained target state.");
                    return true;
                }

                RemoveExistingTransferScene(loadAction.sceneID, existing);
            }

            if (_requiresTransferReconciliation && IsLoadedTargetSelectionAmbiguous(
                    loadedMatches.Count, hasStableAuthoritativeBinding))
            {
                FailTransferReconciliation(
                    $"Authoritative Addressable scene {loadAction.sceneID} matches " +
                    $"{loadedMatches.Count} loaded instances of GUID '{guid}', but no stable SceneID " +
                    "binding selects one retained instance deterministically.");
                return true;
            }

            for (var i = 0; i < loadedMatches.Count; i++)
            {
                var oldId = loadedMatches[i];
                if (!_scenes.TryGetValue(oldId, out var state))
                    continue;

                if (_requiresTransferReconciliation &&
                    !IsExactSceneDescriptorIdentityMatch(
                        oldId, loadAction.sceneID, true, true))
                {
                    FailTransferReconciliation(
                        $"Loaded Addressable scene '{guid}' is retained under SceneID {oldId}, " +
                        $"but the new authority describes SceneID {loadAction.sceneID}. Exact " +
                        "reconciliation cannot re-key a live scene without replaying lifecycle state.");
                    return true;
                }

                if (_requiresTransferReconciliation && IsSceneUnloading(state.scene))
                {
                    FailTransferReconciliation(
                        $"Loaded Addressable scene '{guid}' for authoritative SceneID " +
                        $"{loadAction.sceneID} is already unloading and cannot be rebound safely.");
                    return true;
                }

                var reconciledSettings = loadAction.parameters;
                var repairedRetainedMetadata = false;
                if (!TryReconcileLoadedSceneSettings(
                        state.scene, state.settings, loadAction.parameters,
                        out reconciledSettings, out repairedRetainedMetadata,
                        out var usedLocalPhysicsFallback, out var physicsFailure))
                {
                    if (_requiresTransferReconciliation)
                    {
                        FailTransferReconciliation(
                            $"Authoritative Addressable scene {loadAction.sceneID} has incompatible " +
                            "physical topology: " + physicsFailure);
                        return true;
                    }

                    RemoveExistingTransferScene(oldId, state);
                    continue;
                }

                if (_requiresTransferReconciliation && repairedRetainedMetadata)
                {
                    PurrLogger.LogWarning(
                        $"Repaired stale LocalPhysicsMode metadata for retained Addressable " +
                        $"SceneID {loadAction.sceneID} from its loaded Unity scene.");
                }
                if (_requiresTransferReconciliation && usedLocalPhysicsFallback)
                {
                    WarnBestEffortPhysicsFallback(
                        loadAction.sceneID, state.scene,
                        loadAction.parameters.physicsMode, reconciledSettings.physicsMode);
                }

                MoveAddressableSceneRegistration(oldId, loadAction.sceneID, guid);
                BindLoadedTransferScene(state.scene, reconciledSettings, loadAction.sceneID);
                retainedSceneEvents.Add(loadAction.sceneID);
                return true;
            }

            return false;
        }

        private bool TryPreflightLoadedAddressableTarget(
            LoadAddressableSceneAction loadAction,
            IDictionary<Scene, SceneID> claimedPhysicalScenes,
            out bool isRetained)
        {
            isRetained = false;
            var guid = loadAction.guid.value;
            if (string.IsNullOrEmpty(guid))
            {
                FailTransferReconciliation(
                    $"Authoritative Addressable SceneID {loadAction.sceneID} has an empty GUID.");
                return false;
            }

            var loadedMatches = GetLoadedAddressableSceneIds(guid);
            var hasStableAuthoritativeBinding = false;

            if (_scenes.TryGetValue(loadAction.sceneID, out var existing))
            {
                var existingIsLoaded = existing.scene.IsValid() && existing.scene.isLoaded;
                var identityMatches = IsLoadedAddressableScene(loadAction.sceneID, guid, existing);
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
                        $"Loaded Addressable scene registered for authoritative SceneID " +
                        $"{loadAction.sceneID} does not match its GUID or stable topology. Its " +
                        "SceneID cannot move without destroying scene-authored roots owned by the old hierarchy pool.");
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

            var oldId = loadedMatches[0];
            if (!_scenes.TryGetValue(oldId, out var state) ||
                !state.scene.IsValid() || !state.scene.isLoaded)
            {
                FailTransferReconciliation(
                    $"Loaded Addressable GUID '{guid}' has no consistent retained descriptor.");
                return false;
            }

            if (!IsExactSceneDescriptorIdentityMatch(
                    oldId, loadAction.sceneID, true, true))
            {
                return true;
            }

            if (IsSceneUnloading(state.scene))
            {
                FailTransferReconciliation(
                    $"Loaded Addressable scene '{guid}' for authoritative SceneID " +
                    $"{loadAction.sceneID} is already unloading and cannot be rebound safely.");
                return false;
            }

            if (!TryReconcileLoadedSceneSettings(
                    state.scene, state.settings, loadAction.parameters,
                    out _, out _, out _, out var physicsFailure))
            {
                FailTransferReconciliation(
                    $"Authoritative Addressable scene {loadAction.sceneID} has unproven physical topology: " +
                    physicsFailure);
                return false;
            }

            isRetained = TryClaimExactPhysicalScene(
                state.scene, loadAction.sceneID, claimedPhysicalScenes);
            return isRetained;
        }

        private bool ValidateNoStaleAddressableSceneLoads(
            IReadOnlyDictionary<SceneID, LoadAddressableSceneAction> targetAddressableScenes)
        {
            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                var operation = _pendingAddressableOperations[i];
                if (targetAddressableScenes.TryGetValue(operation.idToAssign, out var target) &&
                    operation.handle.IsValid() &&
                    string.Equals(operation.guid, target.guid.value, StringComparison.OrdinalIgnoreCase) &&
                    ArePendingSceneSettingsCompatible(operation.settings, target.parameters))
                    continue;

                FailTransferReconciliation(
                    $"A stale Addressable load for SceneID {operation.idToAssign} is still in flight; " +
                    "the load cannot be cancelled safely.");
                return false;
            }

            return true;
        }

        private List<SceneID> GetLoadedAddressableSceneIds(string guid)
        {
            var loaded = new List<SceneID>();
            if (!_addressableSceneGuidToIds.TryGetValue(guid, out var sceneIds))
                return loaded;

            for (var i = 0; i < sceneIds.Count; i++)
            {
                var id = sceneIds[i];
                if (_scenes.TryGetValue(id, out var state) &&
                    state.scene.IsValid() && state.scene.isLoaded)
                {
                    loaded.Add(id);
                }
            }

            return loaded;
        }

        private bool IsLoadedAddressableScene(SceneID sceneId, string guid, SceneState state)
        {
            if (!state.scene.IsValid() || !state.scene.isLoaded)
                return false;

            return _addressableSceneIdToGuid.TryGetValue(sceneId, out var existingGuid) &&
                   string.Equals(existingGuid, guid, StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveExistingTransferScene(SceneID sceneId, SceneState state)
        {
            if (_requiresTransferReconciliation && ShouldKeepLocalSceneDuringTransfer(state.scene))
            {
                FailTransferReconciliation(
                    $"Bootstrap Addressable scene '{state.scene.name}' registered for SceneID {sceneId} " +
                    "does not match the authoritative GUID or immutable LocalPhysicsMode.");
                return;
            }

            if (TryRemoveAddressableScene(sceneId, UnloadSceneOptions.None, true, false, out _))
                return;

            RemoveScene(state.scene, true);

            if (!ShouldKeepLocalSceneDuringTransfer(state.scene) && state.scene.IsValid() && state.scene.isLoaded)
            {
                TrackSceneUnload(state.scene, SceneManager.UnloadSceneAsync(state.scene),
                    $"replace Addressable SceneID {sceneId}");
            }
        }

        private void RemoveStaleAddressableTransferScenes(
            IReadOnlyDictionary<SceneID, LoadAddressableSceneAction> targetAddressableScenes,
            ICollection<Scene> retainedPhysicalScenes = null,
            ICollection<SceneID> unloadableSceneIds = null)
        {
            for (var i = _pendingAddressableOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingAddressableOperations[i];
                if (targetAddressableScenes.TryGetValue(operation.idToAssign, out var target) &&
                    string.Equals(operation.guid, target.guid.value, StringComparison.OrdinalIgnoreCase) &&
                    ArePendingSceneSettingsCompatible(operation.settings, target.parameters))
                {
                    continue;
                }

                if (_requiresTransferReconciliation)
                {
                    FailTransferReconciliation(
                        $"A stale Addressable load for SceneID {operation.idToAssign} is still in flight; " +
                        "the load cannot be cancelled safely.");
                    return;
                }

                if (operation.ownsHandle && operation.handle.IsValid())
                {
                    var unload = Addressables.UnloadSceneAsync(
                        operation.handle, UnloadSceneOptions.None, false);
                    TrackAddressableSceneUnload(unload, true,
                        $"remove stale pending Addressable SceneID {operation.idToAssign}");
                }

                _pendingAddressableOperations.RemoveAt(i);
            }

            var ids = new List<SceneID>(_addressableSceneIdToGuid.Keys);
            if (_requiresTransferReconciliation)
            {
                if (retainedPhysicalScenes == null || unloadableSceneIds == null)
                {
                    FailTransferReconciliation(
                        "Exact Addressable cleanup requires a combined retained-scene plan.");
                    return;
                }

                var retainedIds = new List<SceneID>();
                var unloadableIds = new List<SceneID>();
                var retainedScenes = new HashSet<Scene>();

                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (!_addressableSceneIdToGuid.TryGetValue(id, out var existingGuid))
                    {
                        FailTransferReconciliation(
                            $"Addressable SceneID {id} changed while exact stale-scene cleanup was planned.");
                        return;
                    }

                    if (targetAddressableScenes.TryGetValue(id, out var target) &&
                        string.Equals(existingGuid, target.guid.value, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!_scenes.TryGetValue(id, out var state) ||
                        !state.scene.IsValid() || !state.scene.isLoaded ||
                        !_idToScene.TryGetValue(state.scene, out var reverseId) || reverseId != id ||
                        !_rawScenes.Contains(id))
                    {
                        FailTransferReconciliation(
                            $"Stale Addressable SceneID {id} has no stable loaded scene binding.");
                        return;
                    }

                    if (!ShouldKeepLocalSceneDuringTransfer(state.scene))
                    {
                        unloadableIds.Add(id);
                        continue;
                    }

                    if (string.IsNullOrEmpty(existingGuid) ||
                        !_addressableSceneGuidToIds.TryGetValue(existingGuid, out var reverseIds))
                    {
                        FailTransferReconciliation(
                            $"Stale retained Addressable SceneID {id} has no stable reverse GUID binding.");
                        return;
                    }

                    var reverseMatches = 0;
                    for (var j = 0; j < reverseIds.Count; j++)
                    {
                        if (reverseIds[j] == id)
                            reverseMatches++;
                    }

                    if (reverseMatches != 1 || !retainedScenes.Add(state.scene))
                    {
                        FailTransferReconciliation(
                            $"Stale retained Addressable SceneID {id} is not one-to-one with its physical scene and GUID.");
                        return;
                    }

                    retainedIds.Add(id);
                    retainedPhysicalScenes.Add(state.scene);
                }

                for (var i = 0; i < retainedIds.Count; i++)
                    UnregisterAddressableScene(retainedIds[i]);

                for (var i = 0; i < unloadableIds.Count; i++)
                    unloadableSceneIds.Add(unloadableIds[i]);

                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var existingGuid = _addressableSceneIdToGuid[id];
                if (targetAddressableScenes.TryGetValue(id, out var target) &&
                    string.Equals(existingGuid, target.guid.value, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryRemoveAddressableScene(id, UnloadSceneOptions.None, true, false, out _);
            }
        }

        private void RemovePlannedStaleAddressableScenes(
            IReadOnlyList<SceneID> unloadableSceneIds)
        {
            for (var i = 0; i < unloadableSceneIds.Count; i++)
            {
                if (TryRemoveAddressableScene(
                        unloadableSceneIds[i], UnloadSceneOptions.None, true, false, out _))
                    continue;

                FailTransferReconciliation(
                    $"Stale Addressable SceneID {unloadableSceneIds[i]} changed before it could be unloaded.");
                return;
            }
        }

        partial void RebuildAddressableHistoryFromLoadedScenes()
        {
            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var id = _rawScenes[i];
                if (_addressableSceneIdToGuid.ContainsKey(id))
                    AddLoadedAddressableSceneToHistory(id, false);
            }
        }

        partial void CollectAddressablePromotionSceneCandidates(
            List<PromotionSceneCandidate> candidates)
        {
            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var id = _rawScenes[i];
                if (!_scenes.TryGetValue(id, out var state) ||
                    !state.scene.IsValid() || !state.scene.isLoaded ||
                    !_addressableSceneIdToGuid.TryGetValue(id, out var guid) ||
                    string.IsNullOrEmpty(guid))
                    continue;

                if (!TryGetPhysicalLocalPhysicsMode(
                        state.scene, out var physicalMode, out var physicsFailure))
                {
                    throw new InvalidOperationException(
                        $"Cannot promote Addressable SceneID {id} because {physicsFailure}");
                }

                candidates.Add(new PromotionSceneCandidate(
                    id,
                    state.settings.mode,
                    physicalMode,
                    false,
                    true));
            }
        }

        partial void CopyPromotedListenAddressableMetadata(ScenesModule serverModule, SceneID id)
        {
            if (serverModule == null ||
                !serverModule._addressableSceneIdToGuid.TryGetValue(id, out var guid) ||
                string.IsNullOrEmpty(guid))
                return;

            if (serverModule._addressableSceneHandles.TryGetValue(id, out var handle) &&
                handle.IsValid())
            {
                RegisterAddressableSceneHandle(id, guid, handle);
            }
            else
            {
                RegisterAddressableSceneGuid(id, guid);
            }
        }

        partial void ValidatePromotedListenAddressableMetadata(
            ScenesModule serverModule,
            SceneID id,
            ref string failure)
        {
            if (failure != null)
                return;

            if (_addressableSceneHandles.Count != 0 || _addressableSceneIdToGuid.Count != 0 ||
                _addressableSceneGuidToIds.Count != 0)
            {
                failure = "The fresh promoted listen-client role already contains Addressables scene metadata.";
                return;
            }

            var hasGuid = serverModule._addressableSceneIdToGuid.TryGetValue(id, out var guid);
            var hasHandle = serverModule._addressableSceneHandles.ContainsKey(id);
            if (!hasGuid && !hasHandle)
                return;

            if (!hasGuid || string.IsNullOrEmpty(guid))
            {
                failure = $"Promoted Addressable SceneID {id} has no stable GUID.";
                return;
            }

            if (!serverModule._addressableSceneGuidToIds.TryGetValue(guid, out var reverseIds))
            {
                failure = $"Promoted Addressable SceneID {id} has no reverse GUID registration.";
                return;
            }

            var matches = 0;
            for (var i = 0; i < reverseIds.Count; i++)
            {
                if (reverseIds[i] == id)
                    matches++;
            }

            if (matches != 1)
            {
                failure = $"Promoted Addressable GUID '{guid}' does not contain exactly one " +
                          $"reverse registration for SceneID {id}.";
                return;
            }

            if (hasHandle &&
                !TryValidateAddressableSceneHandleBinding(
                    serverModule, id, serverModule._addressableSceneHandles[id], out failure))
            {
                return;
            }
        }

        partial void ValidateExactAddressableAuthoritySwitchState(ref string failure)
        {
            if (failure != null)
                return;

            if (_pendingAddressableOperations.Count > 0 || _pendingAddressableUnloads.Count > 0)
            {
                failure = "An Addressables scene load or unload is still pending.";
                return;
            }

            foreach (var pair in _addressableSceneIdToGuid)
            {
                if (!_scenes.ContainsKey(pair.Key) || string.IsNullOrEmpty(pair.Value))
                {
                    failure = $"Addressable SceneID {pair.Key} has orphaned or empty GUID metadata.";
                    return;
                }

                if (!_addressableSceneGuidToIds.TryGetValue(pair.Value, out var reverseIds))
                {
                    failure = $"Addressable SceneID {pair.Key} has no reverse GUID registration.";
                    return;
                }

                var matches = 0;
                for (var i = 0; i < reverseIds.Count; i++)
                {
                    if (reverseIds[i] == pair.Key)
                        matches++;
                }

                if (matches != 1)
                {
                    failure = $"Addressable SceneID {pair.Key} has {matches} reverse GUID registrations.";
                    return;
                }
            }

            foreach (var pair in _addressableSceneHandles)
            {
                if (!_scenes.ContainsKey(pair.Key) ||
                    !_addressableSceneIdToGuid.ContainsKey(pair.Key))
                {
                    failure = $"Addressable SceneID {pair.Key} has an orphaned scene handle.";
                    return;
                }

                if (!TryValidateAddressableSceneHandleBinding(
                        this, pair.Key, pair.Value, out failure))
                    return;
            }

            foreach (var pair in _addressableSceneGuidToIds)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null || pair.Value.Count == 0)
                {
                    failure = "Addressables reverse GUID metadata is malformed.";
                    return;
                }

                var uniqueIds = new HashSet<SceneID>();
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var id = pair.Value[i];
                    if (!uniqueIds.Add(id) ||
                        !_addressableSceneIdToGuid.TryGetValue(id, out var guid) ||
                        !string.Equals(guid, pair.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        failure = $"Addressable GUID '{pair.Key}' has an inconsistent reverse SceneID {id}.";
                        return;
                    }
                }
            }
        }

        private static bool TryValidateAddressableSceneHandleBinding(
            ScenesModule owner,
            SceneID id,
            AsyncOperationHandle<SceneInstance> handle,
            out string failure)
        {
            failure = null;
            if (!handle.IsValid() || !handle.IsDone ||
                handle.Status != AsyncOperationStatus.Succeeded)
            {
                failure = $"Addressable SceneID {id} has no completed successful scene handle.";
                return false;
            }

            if (!owner._scenes.TryGetValue(id, out var registeredState) ||
                !registeredState.scene.IsValid() || !registeredState.scene.isLoaded)
            {
                failure = $"Addressable SceneID {id} has no stable registered Unity scene.";
                return false;
            }

            var handledScene = handle.Result.Scene;
            if (!handledScene.IsValid() || !handledScene.isLoaded ||
                handledScene.handle != registeredState.scene.handle)
            {
                failure = $"Addressable SceneID {id} has a handle for a different Unity scene.";
                return false;
            }

            return true;
        }

        partial void ValidateExactAddressableAuthoritySwitchScene(
            SceneID id,
            ref bool isAddressable,
            ref string failure)
        {
            if (failure != null)
                return;

            var hasGuid = _addressableSceneIdToGuid.TryGetValue(id, out var guid);
            var hasHandle = _addressableSceneHandles.ContainsKey(id);
            if (!hasGuid && !hasHandle)
                return;

            isAddressable = true;
            if (!hasGuid || string.IsNullOrEmpty(guid) ||
                !_addressableSceneGuidToIds.TryGetValue(guid, out var reverseIds))
            {
                failure = $"Addressable SceneID {id} has no stable one-to-one GUID metadata.";
                return;
            }

            var matches = 0;
            for (var i = 0; i < reverseIds.Count; i++)
            {
                if (reverseIds[i] == id)
                    matches++;
            }

            if (matches != 1)
            {
                failure = $"Addressable GUID '{guid}' does not contain exactly one reverse " +
                          $"registration for SceneID {id}.";
            }
        }

        partial void AddLoadedAddressableSceneToHistory(SceneID id, bool isPromotionBase)
        {
            if (!_scenes.TryGetValue(id, out var state) ||
                !state.scene.IsValid() || !state.scene.isLoaded ||
                !_addressableSceneIdToGuid.TryGetValue(id, out var guid) ||
                string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException(
                    $"Cannot describe promotion SceneID {id} as a loaded Addressable scene.");
            }

            _sceneActionScenes.Add(id);
            var effectiveSettings = state.settings;
            if (_networkManager.hostMigrationSession.canReconcile)
            {
                if (!TryGetPhysicalLocalPhysicsMode(
                        state.scene, out var physicalMode, out var physicsFailure))
                {
                    throw new InvalidOperationException(
                        $"Cannot describe promotion Addressable SceneID {id}: {physicsFailure}");
                }

                effectiveSettings.physicsMode = physicalMode;
                _scenes[id] = new SceneState(state.scene, effectiveSettings);
            }

            var manifestSettings = GetPromotionManifestSettings(
                effectiveSettings,
                _networkManager.hostMigrationSession.canReconcile,
                isPromotionBase);

            _history.AddLoadAddressableAction(new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = id,
                parameters = manifestSettings
            });
        }

        private bool TryRemoveAddressableScene(
            SceneID sceneId,
            UnloadSceneOptions options,
            bool playUnloadEventsImmediately,
            bool keepUnloadHandleAlive,
            out AsyncOperationHandle<SceneInstance> unloadHandle)
        {
            unloadHandle = default;

            var hasHandle = _addressableSceneHandles.TryGetValue(sceneId, out var handle);

            if (!hasHandle && !_addressableSceneIdToGuid.ContainsKey(sceneId))
                return false;

            var hasState = _scenes.TryGetValue(sceneId, out var state);

            var keepPhysicalScene = hasState && ShouldKeepLocalSceneDuringTransfer(state.scene);

            if (hasHandle && handle.IsValid() && !keepPhysicalScene)
            {
                unloadHandle = Addressables.UnloadSceneAsync(handle, options, false);
                TrackAddressableSceneUnload(unloadHandle, !keepUnloadHandleAlive,
                    $"unload Addressable SceneID {sceneId}");
            }
            else if (hasState && !ShouldKeepLocalSceneDuringTransfer(state.scene) &&
                     state.scene.IsValid() && state.scene.isLoaded)
            {
                TrackSceneUnload(state.scene, SceneManager.UnloadSceneAsync(state.scene, options),
                    $"unload Addressable SceneID {sceneId} through SceneManager");
            }

            UnregisterAddressableScene(sceneId);
            if (hasState)
            {
                if (keepPhysicalScene)
                    DetachRetainedPhysicalSceneRegistration(state.scene);
                else RemoveScene(state.scene, playUnloadEventsImmediately);
            }

            return true;
        }

        private void RegisterAddressableSceneHandle(
            SceneID sceneId,
            string guid,
            AsyncOperationHandle<SceneInstance> handle)
        {
            if (handle.IsValid())
                _addressableSceneHandles[sceneId] = handle;

            RegisterAddressableSceneGuid(sceneId, guid);
        }

        private void RegisterAddressableSceneGuid(SceneID sceneId, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            _addressableSceneIdToGuid[sceneId] = guid;
            if (!_addressableSceneGuidToIds.TryGetValue(guid, out var list))
            {
                list = new List<SceneID>();
                _addressableSceneGuidToIds[guid] = list;
            }

            if (!list.Contains(sceneId))
                list.Add(sceneId);
        }

        private void MoveAddressableSceneRegistration(SceneID oldId, SceneID newId, string guid)
        {
            if (_addressableSceneHandles.TryGetValue(oldId, out var handle))
            {
                _addressableSceneHandles.Remove(oldId);
                if (handle.IsValid())
                    _addressableSceneHandles[newId] = handle;
            }

            UnregisterAddressableSceneGuid(oldId);
            RegisterAddressableSceneGuid(newId, guid);
        }

        private void UnregisterAddressableScene(SceneID sceneId)
        {
            _addressableSceneHandles.Remove(sceneId);
            UnregisterAddressableSceneGuid(sceneId);
        }

        private void UnregisterAddressableSceneGuid(SceneID sceneId)
        {
            if (!_addressableSceneIdToGuid.TryGetValue(sceneId, out var guid))
                return;

            _addressableSceneIdToGuid.Remove(sceneId);
            if (!_addressableSceneGuidToIds.TryGetValue(guid, out var list))
                return;

            list.Remove(sceneId);
            if (list.Count == 0)
                _addressableSceneGuidToIds.Remove(guid);
        }

        /// <summary>
        /// Loads an Addressable scene asynchronously by AssetReference (or AssetReferenceScene).
        /// Only the server can load scenes.
        /// </summary>
        /// <param name="sceneRef">The AssetReference pointing to the Addressable scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns>The AsyncOperationHandle for the load, or a default handle if invalid</returns>
        public AsyncOperationHandle<SceneInstance> LoadAddressableSceneAsync(
            AssetReference sceneRef,
            PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes");
                return default;
            }

            if (sceneRef == null || !sceneRef.RuntimeKeyIsValid())
            {
                PurrLogger.LogError("LoadAddressableSceneAsync failed: AssetReference is null or invalid");
                return default;
            }

            ThrowIfExactPromotedListenSceneMutationIsFenced(
                $"load Addressable scene '{sceneRef.AssetGUID}'");

            var idToAssign = GetNextID();
            var guid = sceneRef.AssetGUID;

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

                for (var i = 0; i < _rawScenes.Count; i++)
                {
                    var isDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoadScene)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            _history.AddLoadAddressableAction(new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = idToAssign,
                parameters = settings
            });
            _sceneActionScenes.Add(idToAssign);

            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);
            var handle = Addressables.LoadSceneAsync(sceneRef, parameters, true, 100);

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = idToAssign,
                settings = settings,
                ownsHandle = true
            });

            RegisterAddressableCompletionCallback(handle);

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings,
                    ownsHandle = false
                });
                clientModule._sceneActionScenes.Add(idToAssign);
                clientModule.RegisterAddressableCompletionCallback(handle);
            }

            return handle;
        }

        /// <summary>
        /// Loads an Addressable scene asynchronously by GUID.
        /// Only the server can load scenes.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns>The AsyncOperationHandle for the load, or a default handle if invalid</returns>
        public AsyncOperationHandle<SceneInstance> LoadAddressableSceneAsync(string guid, PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes");
                return default;
            }

            if (string.IsNullOrEmpty(guid))
            {
                PurrLogger.LogError("LoadAddressableSceneAsync failed: GUID is null or empty");
                return default;
            }

            ThrowIfExactPromotedListenSceneMutationIsFenced(
                $"load Addressable scene '{guid}'");

            var idToAssign = GetNextID();

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

                for (var i = 0; i < _rawScenes.Count; i++)
                {
                    var isDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoadScene)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            _history.AddLoadAddressableAction(new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = idToAssign,
                parameters = settings
            });
            _sceneActionScenes.Add(idToAssign);

            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);
            var handle = Addressables.LoadSceneAsync(guid, parameters, true, 100);

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = idToAssign,
                settings = settings,
                ownsHandle = true
            });

            RegisterAddressableCompletionCallback(handle);

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings,
                    ownsHandle = false
                });
                clientModule._sceneActionScenes.Add(idToAssign);
                clientModule.RegisterAddressableCompletionCallback(handle);
            }

            return handle;
        }

        /// <summary>
        /// Returns the pending addressable operations for this module.
        /// This allows you to check if a scene is still loading or unloading and the progress of the operation.
        /// </summary>
        /// <returns>List of pending operations</returns>
        public IReadOnlyList<PendingAddressableSceneOperation> GetPendingAddressableOperations()
        {
            return _pendingAddressableOperations;
        }

        /// <summary>
        /// Returns true if an Addressable scene with the given GUID is currently loaded (or loading).
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <returns>True if at least one instance of the scene is loaded or currently loading</returns>
        public bool IsAddressableSceneLoaded(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (!_stagedExactScenes.ContainsKey(list[i]))
                        return true;
                }
            }

            return IsAddressableSceneLoading(guid);
        }

        /// <summary>
        /// Returns true if an Addressable scene with the given GUID is currently loading.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <returns>True if the scene is currently loading</returns>
        public bool IsAddressableSceneLoading(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;

            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                if (string.Equals(_pendingAddressableOperations[i].guid, guid,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to get the first SceneID for an Addressable scene loaded by the given GUID.
        /// Use GetSceneIdsByAddressableGuid when multiple instances may exist.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <param name="sceneId">The SceneID if found</param>
        /// <returns>True if the scene is loaded and a SceneID was found</returns>
        public bool TryGetSceneIdByAddressableGuid(string guid, out SceneID sceneId)
        {
            if (string.IsNullOrEmpty(guid))
            {
                sceneId = default;
                return false;
            }

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (_stagedExactScenes.ContainsKey(list[i]))
                        continue;

                    sceneId = list[i];
                    return true;
                }
            }

            sceneId = default;
            return false;
        }

        /// <summary>
        /// Gets all SceneIDs for Addressable scenes loaded by the given GUID.
        /// Returns an empty list if none are loaded.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <returns>A list of SceneIDs (may be empty, never null). Do not modify the returned list.</returns>
        public IReadOnlyList<SceneID> GetSceneIdsByAddressableGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return System.Array.Empty<SceneID>();

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list))
            {
                var published = new List<SceneID>(list.Count);
                for (var i = 0; i < list.Count; i++)
                {
                    if (!_stagedExactScenes.ContainsKey(list[i]))
                        published.Add(list[i]);
                }
                return published;
            }

            return System.Array.Empty<SceneID>();
        }

        /// <summary>
        /// Unloads all instances of an Addressable scene by its asset GUID.
        /// Only the server can unload scenes. Returns the number of instances unloaded.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene to unload</param>
        /// <param name="options">The UnloadSceneOptions to use</param>
        /// <returns>The number of scene instances that were unloaded</returns>
        public int UnloadAddressableSceneByGuid(
            string guid,
            UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return 0;
            }

            var ids = GetSceneIdsByAddressableGuid(guid);
            var count = ids.Count;

            for (var i = ids.Count - 1; i >= 0; i--)
                UnloadSceneAsync(ids[i], options);

            return count;
        }
    }
}
#endif
