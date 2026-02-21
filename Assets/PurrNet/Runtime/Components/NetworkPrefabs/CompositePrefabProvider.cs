using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet
{
    public class CompositePrefabProvider : IPrefabProvider, IAsyncPrefabProvider
    {
        private readonly List<IPrefabProvider> _providers = new();
        private readonly List<int> _offsets = new();
        private readonly List<int> _counts = new();
        private readonly Dictionary<int, PrefabData> _unified = new();

        public IEnumerable<PrefabData> allPrefabs => _unified.Values;

        /// <summary>
        /// Adds a provider to the composite. Providers must be added in
        /// the same order on all network peers for deterministic ID assignment.
        /// </summary>
        public void AddProvider(IPrefabProvider provider)
        {
            _providers.Add(provider);
        }

        /// <summary>
        /// Rebuilds the lookup from all added providers.
        /// Must be called after all providers are added and individually refreshed
        /// </summary>
        public void Refresh()
        {
            _unified.Clear();
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
                    int unifiedId = data.prefabId + offset;
                    _unified[unifiedId] = new PrefabData
                    {
                        prefabId = unifiedId,
                        prefab = data.prefab,
                        pooled = data.pooled,
                        warmupCount = data.warmupCount
                    };

                    if (data.prefabId > localMax)
                        localMax = data.prefabId;
                }

                int count = localMax + 1;
                _counts.Add(count);
                offset += count;
            }
        }

        public bool NeedsLoad(int prefabId)
        {
            return _unified.TryGetValue(prefabId, out var data) && data.prefab == null;
        }

        public async Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!_unified.TryGetValue(prefabId, out var data))
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not found in CompositePrefabProvider.");
                return default;
            }

            if (data.prefab != null)
                return data;

            for (int i = 0; i < _providers.Count; i++)
            {
                int count = _counts[i];
                if (prefabId < _offsets[i] || prefabId >= _offsets[i] + count)
                    continue;

                int localId = prefabId - _offsets[i];
                var provider = _providers[i];

                if (provider is IAsyncPrefabProvider asyncProvider)
                {
                    try
                    {
                        var loaded = await asyncProvider.LoadPrefabAsync(localId);
                        if (loaded.prefab == null)
                            return default;

                        data.prefab = loaded.prefab;
                        _unified[prefabId] = data;
                        return data;
                    }
                    catch (System.Exception e)
                    {
                        PurrLogger.LogError($"LoadPrefabAsync: exception loading prefabId {prefabId} (provider {i} local {localId}): {e.Message}\n{e.StackTrace}");
                        return default;
                    }
                }

                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} needs load but provider {i} does not support async loading.");
                return default;
            }

            PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not in any provider range.");
            return default;
        }

        public bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return _unified.TryGetValue(prefabId, out prefabData);
        }

        public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            foreach (var data in _unified.Values)
            {
                if (data.prefab == prefab)
                {
                    prefabData = data;
                    return true;
                }
            }

            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].TryGetPrefabData(prefab, out var pd) && pd.prefab != null)
                {
                    int unifiedId = _offsets[i] + pd.prefabId;
                    prefabData = new PrefabData
                    {
                        prefabId = unifiedId,
                        prefab = pd.prefab,
                        pooled = pd.pooled,
                        warmupCount = pd.warmupCount
                    };
                    _unified[unifiedId] = prefabData;
                    return true;
                }
            }

            prefabData = default;
            return false;
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        public bool TryGetAddressableGuid(int prefabId, out string assetGuid)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                int count = _counts[i];
                if (prefabId < _offsets[i] || prefabId >= _offsets[i] + count)
                    continue;

                int localId = prefabId - _offsets[i];
                if (_providers[i] is AddressableNetworkPrefabs addr)
                    return addr.TryGetGuid(localId, out assetGuid);

                assetGuid = null;
                return false;
            }

            assetGuid = null;
            return false;
        }
#endif
    }
}
