using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace PurrNet
{
    public class CompositePrefabProvider : IPrefabProvider, IAsyncPrefabProvider, IPersistentPrefabProvider
#if ADDRESSABLES_PURRNET_SUPPORT
        , IAddressablePrefabProvider
#endif
    {
        private readonly List<IPrefabProvider> _providers = new();
        private readonly List<int> _offsets = new();
        private readonly List<int> _counts = new();

        // Only ids are kept here. The providers own the prefabs and can load or release them at any
        // time, so a copy of their PrefabData would go stale and keep a released prefab reachable.
        private readonly Dictionary<int, (int provider, int local)> _locations = new();
        private readonly Dictionary<ObjectId, int> _idByPrefabInstance = new();

        public IEnumerable<PrefabData> allPrefabs
        {
            get
            {
                foreach (int id in _locations.Keys)
                {
                    if (TryGetPrefabData(id, out var data))
                        yield return data;
                }
            }
        }

        public IEnumerable<string> persistentIds
        {
            get
            {
                for (int i = 0; i < _providers.Count; i++)
                {
                    if (_providers[i] is not IPersistentPrefabProvider persistentProvider)
                        continue;

                    foreach (var persistentId in persistentProvider.persistentIds)
                        yield return persistentId;
                }
            }
        }

        /// <summary>
        /// Adds a provider to the composite. Providers must be added in
        /// the same order on all network peers for deterministic ID assignment.
        /// </summary>
        public void AddProvider(IPrefabProvider provider)
        {
            _providers.Add(provider);
        }

        public void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        {
            if (_providers.Count == 0)
            {
                PurrLogger.LogError("CompositePrefabProvider: no providers registered, cannot add runtime prefab.");
                return;
            }

            _providers[0].AddRuntimePrefab(uniqueName, prefab, pooled, warmup);
            Refresh();
        }

        /// <summary>
        /// Rebuilds the lookup from all added providers.
        /// Must be called after all providers are added and individually refreshed
        /// </summary>
        public void Refresh()
        {
            _locations.Clear();
            _idByPrefabInstance.Clear();
            _offsets.Clear();
            _counts.Clear();

            int offset = 0;

            for (int i = 0; i < _providers.Count; i++)
            {
                var provider = _providers[i];
                _offsets.Add(offset);

                int localMax = -1;

                foreach (var data in provider.allPrefabs)
                {
                    int localId = (int)data.prefabId;
                    _locations[localId + offset] = (i, localId);

                    if (localId > localMax)
                        localMax = localId;
                }

                int count = localMax + 1;
                _counts.Add(count);
                offset += count;
            }
        }

        private bool TryGetProvider(int prefabId, out int providerIndex, out int localId)
        {
            if (_locations.TryGetValue(prefabId, out var location))
            {
                providerIndex = location.provider;
                localId = location.local;
                return true;
            }

            providerIndex = -1;
            localId = -1;
            return false;
        }

        private PrefabData ToUnified(int providerIndex, PrefabData local)
        {
            local.prefabId = _offsets[providerIndex] + (int)local.prefabId;
            return local;
        }

        public bool NeedsLoad(int prefabId)
        {
            return TryGetPrefabData(prefabId, out var data) && data.prefab == null;
        }

        public async Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!TryGetProvider(prefabId, out int providerIndex, out int localId))
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not found in CompositePrefabProvider.");
                return default;
            }

            var provider = _providers[providerIndex];

            if (provider.TryGetPrefabData(localId, out var data) && data.prefab != null)
                return ToUnified(providerIndex, data);

            if (provider is not IAsyncPrefabProvider asyncProvider)
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} needs load but provider {providerIndex} does not support async loading.");
                return default;
            }

            try
            {
                var loaded = await asyncProvider.LoadPrefabAsync(localId);
                return loaded.prefab == null ? default : ToUnified(providerIndex, loaded);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"LoadPrefabAsync: exception loading prefabId {prefabId} (provider {providerIndex} local {localId}): {e.Message}\n{e.StackTrace}");
                return default;
            }
        }

        public bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            if (TryGetProvider(prefabId, out int providerIndex, out int localId) &&
                _providers[providerIndex].TryGetPrefabData(localId, out prefabData))
            {
                prefabData.prefabId = prefabId;
                return true;
            }

            prefabData = default;
            return false;
        }

        public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            if (!prefab)
            {
                prefabData = default;
                return false;
            }

            var instanceId = PurrObjectId.Of(prefab);

            if (_idByPrefabInstance.TryGetValue(instanceId, out int cachedId))
            {
                if (TryGetPrefabData(cachedId, out prefabData) && prefabData.prefab == prefab)
                    return true;

                _idByPrefabInstance.Remove(instanceId);
            }

            for (int i = 0; i < _providers.Count; i++)
            {
                if (!_providers[i].TryGetPrefabData(prefab, out var local) || local.prefab == null)
                    continue;

                prefabData = ToUnified(i, local);

                // A provider may match by name when the same asset exists in two bundles; only
                // remember exact matches so that warning keeps showing up.
                if (local.prefab == prefab)
                    _idByPrefabInstance[instanceId] = (int)prefabData.prefabId;

                return true;
            }

            prefabData = default;
            return false;
        }

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            if (TryGetProvider(prefabId, out int providerIndex, out int localId) &&
                _providers[providerIndex] is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPersistentId(localId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (TryGetPrefabData(prefab, out var prefabData))
                return TryGetPersistentId((int)prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            if (string.IsNullOrEmpty(persistentId))
            {
                prefabData = default;
                return false;
            }

            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i] is not IPersistentPrefabProvider persistentProvider)
                    continue;

                if (!persistentProvider.TryGetPrefabDataByPersistentId(persistentId, out var localData))
                    continue;

                prefabData = ToUnified(i, localData);
                return true;
            }

            prefabData = default;
            return false;
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        public event Action<string, bool> loadStateChanged
        {
            add
            {
                for (int i = 0; i < _providers.Count; i++)
                {
                    if (_providers[i] is IAddressablePrefabProvider addressable)
                        addressable.loadStateChanged += value;
                }
            }
            remove
            {
                for (int i = 0; i < _providers.Count; i++)
                {
                    if (_providers[i] is IAddressablePrefabProvider addressable)
                        addressable.loadStateChanged -= value;
                }
            }
        }

        public bool TryGetAddressableGuid(int prefabId, out string assetGuid)
        {
            if (TryGetProvider(prefabId, out int providerIndex, out int localId) &&
                _providers[providerIndex] is IAddressablePrefabProvider addressable)
                return addressable.TryGetAddressableGuid(localId, out assetGuid);

            assetGuid = null;
            return false;
        }

        public IEnumerable<string> GetLoadedGuids()
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i] is not IAddressablePrefabProvider addressable)
                    continue;

                foreach (var guid in addressable.GetLoadedGuids())
                    yield return guid;
            }
        }

        private bool TryGetAddressableOwner(string assetGuid, out int providerIndex, out int localId)
        {
            providerIndex = -1;
            localId = -1;

            if (string.IsNullOrEmpty(assetGuid))
                return false;

            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i] is not IAddressablePrefabProvider addressable ||
                    !addressable.TryGetPrefabDataByPersistentId(assetGuid, out var local) ||
                    !addressable.TryGetAddressableGuid((int)local.prefabId, out _))
                    continue;

                providerIndex = i;
                localId = (int)local.prefabId;
                return true;
            }

            return false;
        }

        public Task<PrefabData> LoadPrefabByGuidAsync(string assetGuid)
        {
            // Go through this composite's own id so the result carries the offset: a provider's
            // local id must never reach a spawn packet.
            if (!TryGetAddressableOwner(assetGuid, out int providerIndex, out int localId))
            {
                PurrLogger.LogError($"LoadPrefabByGuidAsync: GUID '{assetGuid}' not registered.");
                return Task.FromResult(default(PrefabData));
            }

            return LoadPrefabAsync(_offsets[providerIndex] + localId);
        }

        public bool ReleasePrefabByGuid(string assetGuid)
        {
            return TryGetAddressableOwner(assetGuid, out int providerIndex, out _) &&
                   ((IAddressablePrefabProvider)_providers[providerIndex]).ReleasePrefabByGuid(assetGuid);
        }
#endif
    }
}
