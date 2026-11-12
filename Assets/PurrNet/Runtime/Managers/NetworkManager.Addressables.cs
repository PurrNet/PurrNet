#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PurrNet
{
    public sealed partial class NetworkManager
    {
        private AddressableUsageTracker _addressableUsage;

        /// <summary>
        /// Tracks which addressable network prefabs are still needed and releases the others.
        /// </summary>
        public AddressableUsageTracker addressableUsage => _addressableUsage ??= new AddressableUsageTracker(this);

        /// <summary>
        /// Loads an addressable network prefab so it can be spawned without waiting.
        /// The returned prefab id is the one used on the network.
        /// Call <see cref="RetainAddressablePrefab"/> first if the prefab has to stay loaded while nothing uses it.
        /// </summary>
        public Task<PrefabData> LoadAddressablePrefabAsync(string assetGuid)
        {
            if (prefabProvider is IAddressablePrefabProvider provider)
                return provider.LoadPrefabByGuidAsync(assetGuid);

            PurrLogger.LogError("LoadAddressablePrefabAsync failed: the prefab provider has no Addressable prefabs.");
            return Task.FromResult(default(PrefabData));
        }

        /// <summary>
        /// Keeps an addressable network prefab from being released while unused.
        /// Every call needs a matching <see cref="ReleaseAddressablePrefab"/>.
        /// </summary>
        public void RetainAddressablePrefab(string assetGuid) => addressableUsage.Retain(assetGuid);

        /// <summary>
        /// Gives up a hold taken with <see cref="RetainAddressablePrefab"/>. The prefab is released once
        /// nothing holds it and no spawned object uses it, if the network rules allow it.
        /// </summary>
        public void ReleaseAddressablePrefab(string assetGuid) => addressableUsage.Release(assetGuid);

        /// <summary>
        /// Spawns a networked object from a pre-loaded Addressable prefab.
        /// The AssetReference must match an entry in the assigned AddressableNetworkPrefabs asset.
        /// The prefabs must be loaded (via AddressableNetworkPrefabs.LoadAllAsync) before calling this.
        /// </summary>
        /// <param name="assetRef">The AssetReference pointing to the Addressable prefab to spawn.</param>
        /// <param name="position">World position for the spawned object.</param>
        /// <param name="rotation">Rotation for the spawned object.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <returns>The spawned GameObject, or null if spawning failed.</returns>
        public GameObject SpawnAddressable(
            AssetReferenceGameObject assetRef,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null)
        {
            if (assetRef == null || !assetRef.RuntimeKeyIsValid())
            {
                PurrLogger.LogError("SpawnAddressable failed: AssetReference is null or invalid.");
                return null;
            }

            return SpawnAddressableByGuid(assetRef.AssetGUID, position, rotation, parent);
        }

        /// <summary>
        /// Spawns a networked object from a pre-loaded Addressable prefab, identified by asset GUID.
        /// The GUID must match an entry in the assigned AddressableNetworkPrefabs asset.
        /// The prefabs must be loaded (via AddressableNetworkPrefabs.LoadAllAsync) before calling this.
        /// </summary>
        /// <param name="assetGuid">The asset GUID of the Addressable prefab.</param>
        /// <param name="position">World position for the spawned object.</param>
        /// <param name="rotation">Rotation for the spawned object.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <returns>The spawned GameObject, or null if spawning failed.</returns>
        public GameObject SpawnAddressableByGuid(
            string assetGuid,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null)
        {
            if (!_addressableNetworkPrefabs)
            {
                PurrLogger.LogError("SpawnAddressable failed: No AddressableNetworkPrefabs assigned on NetworkManager.");
                return null;
            }

            if (!_addressableNetworkPrefabs.TryGetPrefabDataByGuid(assetGuid, out var localData))
            {
                PurrLogger.LogError($"SpawnAddressable failed: No Addressable prefab registered with GUID '{assetGuid}'.");
                return null;
            }

            if (!localData.prefab)
            {
                PurrLogger.LogError($"SpawnAddressable failed: Addressable prefab with GUID '{assetGuid}' is registered but not loaded. Use SpawnAddressableAsync and await it.");
                return null;
            }

            return InstantiateAndSpawnAddressable(localData.prefab, position, rotation, parent);
        }

        public async Task<GameObject> SpawnAddressableAsync(
            AssetReferenceGameObject assetRef,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null)
        {
            if (assetRef == null || !assetRef.RuntimeKeyIsValid())
            {
                PurrLogger.LogError("SpawnAddressableAsync failed: AssetReference is null or invalid.");
                return null;
            }

            return await SpawnAddressableByGuidAsync(assetRef.AssetGUID, position, rotation, parent);
        }

        public async Task<GameObject> SpawnAddressableByGuidAsync(
            string assetGuid,
            Vector3 position = default,
            Quaternion rotation = default,
            Transform parent = null)
        {
            if (!_addressableNetworkPrefabs)
            {
                PurrLogger.LogError("SpawnAddressableAsync failed: No AddressableNetworkPrefabs assigned on NetworkManager.");
                return null;
            }

            try
            {
                var prefabData = await _addressableNetworkPrefabs.LoadPrefabByGuidAsync(assetGuid);
                if (prefabData.prefab == null)
                {
                    PurrLogger.LogError($"SpawnAddressableAsync failed: could not load Addressable prefab GUID '{assetGuid}'.");
                    return null;
                }

                return InstantiateAndSpawnAddressable(prefabData.prefab, position, rotation, parent);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"SpawnAddressableAsync failed for GUID '{assetGuid}': {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private GameObject InstantiateAndSpawnAddressable(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            var instance = parent
                ? UnityProxy.Instantiate(prefab, position, rotation, parent)
                : UnityProxy.Instantiate(prefab, position, rotation);

            if (!instance)
                return null;

            if (instance.TryGetComponent(out NetworkIdentity identity) && !identity.IsSpawned(isServer))
                Spawn(instance);

            return instance;
        }

        /// <summary>
        /// Despawns and releases a networked Addressable object.
        /// This handles both the network despawn and the Addressable reference cleanup.
        /// </summary>
        /// <param name="instance">The spawned instance to despawn and release.</param>
        public void DespawnAddressable(GameObject instance)
        {
            if (!instance)
                return;

            UnityProxy.Destroy(instance);
        }
    }
}
#endif
