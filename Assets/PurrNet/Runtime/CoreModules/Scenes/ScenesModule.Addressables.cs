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
            public AsyncOperationHandle<SceneInstance> handle;
            public SceneID idToAssign;
            public PurrSceneSettings settings;
        }

        private readonly List<PendingAddressableSceneOperation> _pendingAddressableOperations =
            new List<PendingAddressableSceneOperation>();

        private readonly Dictionary<SceneID, AsyncOperationHandle<SceneInstance>> _addressableSceneHandles =
            new Dictionary<SceneID, AsyncOperationHandle<SceneInstance>>();

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
                handle = handle,
                idToAssign = action.sceneID,
                settings = action.parameters
            });

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
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
                handle = handle,
                idToAssign = idToAssign,
                settings = settings
            });

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
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
                handle = handle,
                idToAssign = idToAssign,
                settings = settings
            });

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings
                });
            }

            return handle;
        }
    }
}
#endif
