#if ADDRESSABLES_PURRNET_SUPPORT
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace PurrNet
{
    [UsedByIL]
    public static class AddressablesProxy
    {
        #region Addressables.InstantiateAsync - Intercepted

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            object key, Transform parent, bool instantiateInWorldSpace, bool trackHandle)
        {
            var handle = InstantiateAsyncDirectly(key, parent, instantiateInWorldSpace, trackHandle);
            RegisterAutoSpawn(handle, key);
            return handle;
        }

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            object key, Vector3 position, Quaternion rotation, Transform parent, bool trackHandle)
        {
            var handle = InstantiateAsyncDirectly(key, position, rotation, parent, trackHandle);
            RegisterAutoSpawn(handle, key);
            return handle;
        }

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            object key, InstantiationParameters instantiateParameters, bool trackHandle)
        {
            var handle = InstantiateAsyncDirectly(key, instantiateParameters, trackHandle);
            RegisterAutoSpawn(handle, key);
            return handle;
        }

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            IResourceLocation location, Transform parent, bool instantiateInWorldSpace, bool trackHandle)
        {
            var handle = InstantiateAsyncDirectly(location, parent, instantiateInWorldSpace, trackHandle);
            RegisterAutoSpawn(handle, location);
            return handle;
        }

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            IResourceLocation location, Vector3 position, Quaternion rotation, Transform parent, bool trackHandle)
        {
            var handle = InstantiateAsyncDirectly(location, position, rotation, parent, trackHandle);
            RegisterAutoSpawn(handle, location);
            return handle;
        }

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            IResourceLocation location, InstantiationParameters instantiateParameters, bool trackHandle)
        {
            var handle = InstantiateAsyncDirectly(location, instantiateParameters, trackHandle);
            RegisterAutoSpawn(handle, location);
            return handle;
        }

        #endregion

        #region AssetReference.InstantiateAsync - Intercepted

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            AssetReference self, Transform parent, bool instantiateInWorldSpace)
        {
            var handle = InstantiateAsyncDirectly(self, parent, instantiateInWorldSpace);
            RegisterAutoSpawn(handle, self);
            return handle;
        }

        [UsedByIL]
        public static AsyncOperationHandle<GameObject> InstantiateAsync(
            AssetReference self, Vector3 position, Quaternion rotation, Transform parent)
        {
            var handle = InstantiateAsyncDirectly(self, position, rotation, parent);
            RegisterAutoSpawn(handle, self);
            return handle;
        }

        #endregion

        #region Addressables.ReleaseInstance - Intercepted

        [UsedByIL]
        public static bool ReleaseInstance(GameObject instance)
        {
            DespawnIfNeeded(instance);
            return ReleaseInstanceDirectly(instance);
        }

        [UsedByIL]
        public static bool ReleaseInstance(AsyncOperationHandle handle)
            => ReleaseInstanceDirectly(handle);

        [UsedByIL]
        public static bool ReleaseInstance(AsyncOperationHandle<GameObject> handle)
        {
            if (handle.IsValid() && handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded)
                DespawnIfNeeded(handle.Result);
            return ReleaseInstanceDirectly(handle);
        }

        #endregion

        #region AssetReference.ReleaseInstance - Intercepted

        [UsedByIL]
        public static void ReleaseInstance(AssetReference self, GameObject obj)
        {
            DespawnIfNeeded(obj);
            ReleaseInstanceDirectly(self, obj);
        }

        #endregion

        #region Addressables.InstantiateAsync - Directly

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            object key, Transform parent = null, bool instantiateInWorldSpace = false, bool trackHandle = true)
            => Addressables.InstantiateAsync(key, parent, instantiateInWorldSpace, trackHandle);

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            object key, Vector3 position, Quaternion rotation, Transform parent = null, bool trackHandle = true)
            => Addressables.InstantiateAsync(key, position, rotation, parent, trackHandle);

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            object key, InstantiationParameters instantiateParameters, bool trackHandle = true)
            => Addressables.InstantiateAsync(key, instantiateParameters, trackHandle);

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            IResourceLocation location, Transform parent = null, bool instantiateInWorldSpace = false, bool trackHandle = true)
            => Addressables.InstantiateAsync(location, parent, instantiateInWorldSpace, trackHandle);

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            IResourceLocation location, Vector3 position, Quaternion rotation, Transform parent = null, bool trackHandle = true)
            => Addressables.InstantiateAsync(location, position, rotation, parent, trackHandle);

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            IResourceLocation location, InstantiationParameters instantiateParameters, bool trackHandle = true)
            => Addressables.InstantiateAsync(location, instantiateParameters, trackHandle);

        #endregion

        #region AssetReference.InstantiateAsync - Directly

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            AssetReference self, Transform parent = null, bool instantiateInWorldSpace = false)
            => self.InstantiateAsync(parent, instantiateInWorldSpace);

        public static AsyncOperationHandle<GameObject> InstantiateAsyncDirectly(
            AssetReference self, Vector3 position, Quaternion rotation, Transform parent = null)
            => self.InstantiateAsync(position, rotation, parent);

        #endregion

        #region Addressables.ReleaseInstance - Directly

        public static bool ReleaseInstanceDirectly(GameObject instance)
            => Addressables.ReleaseInstance(instance);

        public static bool ReleaseInstanceDirectly(AsyncOperationHandle handle)
            => Addressables.ReleaseInstance(handle);

        public static bool ReleaseInstanceDirectly(AsyncOperationHandle<GameObject> handle)
            => Addressables.ReleaseInstance(handle);

        #endregion

        #region AssetReference.ReleaseInstance - Directly

        public static void ReleaseInstanceDirectly(AssetReference self, GameObject obj)
            => self.ReleaseInstance(obj);

        #endregion

        #region Internal

        private static readonly Dictionary<AsyncOperationHandle<GameObject>, object> _pendingKeys = new();

        private static void RegisterAutoSpawn(AsyncOperationHandle<GameObject> handle, object key)
        {
            _pendingKeys[handle] = key;
            handle.Completed += OnInstantiateCompleted;
        }

        private static void OnInstantiateCompleted(AsyncOperationHandle<GameObject> op)
        {
            if (!_pendingKeys.Remove(op, out var key))
                return;

            if (op.Status != AsyncOperationStatus.Succeeded)
                return;

            var go = op.Result;

            if (!go)
                return;

            if (!go.GetComponentInChildren<NetworkIdentity>())
                return;

            var manager = NetworkManager.main;

            if (!manager)
                return;

            var prefab = ResolvePrefab(manager, key);

            if (!prefab)
            {
                PurrLogger.LogWarning(
                    $"Addressables.InstantiateAsync created '{go.name}' with a NetworkIdentity, " +
                    "but the prefab could not be resolved for network spawning. " +
                    "Make sure the prefab is registered in NetworkPrefabs or AddressableNetworkPrefabs.",
                    go);
                return;
            }

            PurrNetGameObjectUtils.NotifyGameObjectCreated(go, prefab);
        }

        private static GameObject ResolvePrefab(NetworkManager manager, object key)
        {
            string guid = null;

            if (key is AssetReference assetRef)
                guid = assetRef.AssetGUID;
            else if (key is string strKey)
                guid = strKey;
            else if (key is IResourceLocation location)
                guid = location.PrimaryKey;

            var addrPrefabs = manager.addressableNetworkPrefabs;

            if (guid != null && addrPrefabs &&
                addrPrefabs.TryGetPrefabDataByGuid(guid, out var data) &&
                data.prefab)
            {
                return data.prefab;
            }

            var provider = manager.prefabProvider;

            if (provider != null && key is AssetReference loadedRef &&
                loadedRef.Asset is GameObject loadedPrefab)
            {
                if (provider.TryGetPrefabData(loadedPrefab, out var providerData))
                    return providerData.prefab;
            }

            return null;
        }

        private static void DespawnIfNeeded(GameObject instance)
        {
            if (!instance || ApplicationContext.isQuitting)
                return;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            instance.GetComponentsInChildren(true, identities);

            if (identities.Count > 0)
            {
                for (int i = 0; i < identities.Count; i++)
                    identities[i].Despawn();
            }

            ListPool<NetworkIdentity>.Destroy(identities);
        }

        #endregion
    }
}
#endif
