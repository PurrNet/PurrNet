using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Single entry point for turning a <see cref="PrefabID"/> (or a prefab reference) into <see cref="PrefabData"/>.
    /// Every runtime lookup goes through here so the resolution strategy can be changed in one place.
    /// </summary>
    public sealed class PrefabResolver
    {
        private readonly NetworkManager _manager;

        internal PrefabResolver(NetworkManager manager)
        {
            _manager = manager;
        }

        private IPrefabProvider provider => _manager.prefabProvider;

        public IEnumerable<PrefabData> allPrefabs => provider?.allPrefabs ?? Array.Empty<PrefabData>();

        public bool TryGetPrefabData(PrefabID id, out PrefabData prefabData)
        {
            var current = provider;
            if (current != null && id.isValid)
                return current.TryGetPrefabData((int)id, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            var current = provider;
            if (current != null && prefab)
                return current.TryGetPrefabData(prefab, out prefabData);

            prefabData = default;
            return false;
        }

        /// <summary>
        /// True when the id is known but its prefab isn't loaded yet and the provider can load it asynchronously.
        /// </summary>
        public bool NeedsLoad(PrefabID id)
        {
            return provider is IAsyncPrefabProvider &&
                   TryGetPrefabData(id, out var prefabData) && !prefabData.prefab;
        }

        public async Task<PrefabData> LoadPrefabAsync(PrefabID id)
        {
            if (provider is IAsyncPrefabProvider asyncProvider)
                return await asyncProvider.LoadPrefabAsync((int)id);

            return TryGetPrefabData(id, out var prefabData) ? prefabData : default;
        }

        public bool TryGetPersistentId(PrefabID id, out string persistentId)
        {
            if (provider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPersistentId((int)id, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (provider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPersistentId(prefab, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            if (provider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPrefabDataByPersistentId(persistentId, out prefabData);

            prefabData = default;
            return false;
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        public bool TryGetAddressableGuid(PrefabID id, out string assetGuid)
        {
            if (provider is CompositePrefabProvider composite)
                return composite.TryGetAddressableGuid((int)id, out assetGuid);

            assetGuid = null;
            return false;
        }
#endif
    }
}
