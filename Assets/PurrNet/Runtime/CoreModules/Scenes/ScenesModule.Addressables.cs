#if ADDRESSABLES_PURRNET_SUPPORT
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
        private struct PendingAddressableSceneOperation
        {
            public string guid;
            public AsyncOperationHandle<SceneInstance> handle;
            public SceneID idToAssign;
            public PurrSceneSettings settings;
        }

        private readonly List<PendingAddressableSceneOperation> _pendingAddressableOperations =
            new List<PendingAddressableSceneOperation>();

        private readonly Dictionary<SceneID, AsyncOperationHandle<SceneInstance>> _addressableSceneHandles =
            new Dictionary<SceneID, AsyncOperationHandle<SceneInstance>>();

        private readonly Dictionary<SceneID, string> _addressableSceneIdToGuid =
            new Dictionary<SceneID, string>();

        private readonly Dictionary<string, List<SceneID>> _addressableSceneGuidToIds =
            new Dictionary<string, List<SceneID>>();

        partial void ProcessCompletedAddressableLoads()
        {
            for (var i = _pendingAddressableOperations.Count - 1; i >= 0; i--)
            {
                var op = _pendingAddressableOperations[i];

                if (!op.handle.IsDone)
                    continue;

                if (op.handle.Status == AsyncOperationStatus.Succeeded)
                {
                    var scene = op.handle.Result.Scene;
                    AddScene(scene, op.settings, op.idToAssign);
                    _addressableSceneHandles[op.idToAssign] = op.handle;
                    if (!string.IsNullOrEmpty(op.guid))
                    {
                        _addressableSceneIdToGuid[op.idToAssign] = op.guid;
                        if (!_addressableSceneGuidToIds.TryGetValue(op.guid, out var list))
                        {
                            list = new List<SceneID>();
                            _addressableSceneGuidToIds[op.guid] = list;
                        }
                        list.Add(op.idToAssign);
                    }
                }
                else
                {
                    PurrLogger.LogError($"Addressable scene load failed: {op.handle.OperationException}");
                }

                _pendingAddressableOperations.RemoveAt(i);
            }
        }

        private void ProcessLoadAddressableAction(LoadAddressableSceneAction action)
        {
            var guid = action.guid.value;
            if (string.IsNullOrEmpty(guid))
            {
                PurrLogger.LogError("LoadAddressableSceneAction has empty GUID");
                return;
            }

            var parameters = action.GetLoadSceneParameters();

            AsyncOperationHandle<SceneInstance> handle;

            try
            {
                handle = Addressables.LoadSceneAsync(guid, parameters, true, 100);
            }
            catch (System.Exception e)
            {
                PurrLogger.LogError($"Error loading addressable scene: {e}");
                return;
            }

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = action.sceneID,
                settings = action.parameters
            });

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = action.sceneID,
                    settings = action.parameters
                });
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

        private bool TryUnloadAddressableScene(SceneID sceneId, UnloadSceneOptions options)
        {
            if (!_addressableSceneHandles.TryGetValue(sceneId, out var handle))
                return false;

            if (!_scenes.TryGetValue(sceneId, out var state))
                return false;

            Addressables.UnloadSceneAsync(handle, options);
            _addressableSceneHandles.Remove(sceneId);
            if (_addressableSceneIdToGuid.TryGetValue(sceneId, out var guid))
            {
                _addressableSceneIdToGuid.Remove(sceneId);
                if (_addressableSceneGuidToIds.TryGetValue(guid, out var list))
                {
                    list.Remove(sceneId);
                    if (list.Count == 0)
                        _addressableSceneGuidToIds.Remove(guid);
                }
            }
            RemoveScene(state.scene);

            return true;
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

            HostMigrationCompatibility(ref settings);

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

            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);
            var handle = Addressables.LoadSceneAsync(sceneRef, parameters, true, 100);

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = idToAssign,
                settings = settings
            });

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings
                });
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

            HostMigrationCompatibility(ref settings);

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

            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);
            var handle = Addressables.LoadSceneAsync(guid, parameters, true, 100);

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = idToAssign,
                settings = settings
            });

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings
                });
            }

            return handle;
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

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list) && list.Count > 0)
                return true;

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
                if (_pendingAddressableOperations[i].guid == guid)
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

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list) && list.Count > 0)
            {
                sceneId = list[0];
                return true;
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
                return list;

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
