using System.Collections.Generic;
using UnityEngine;

namespace PurrNet
{
    public class CompositePrefabProvider : IPrefabProvider
    {
        private readonly List<IPrefabProvider> _providers = new();
        private readonly List<int> _offsets = new();
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

                offset += localMax + 1;
            }
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

            prefabData = default;
            return false;
        }
    }
}
