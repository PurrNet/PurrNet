using Object = UnityEngine.Object;

namespace PurrNet
{
    /// <summary>
    /// Single entry point for turning a <see cref="NetworkAssetID"/> (or an asset reference) into a registered asset.
    /// Every runtime lookup goes through here so the resolution strategy can be changed in one place.
    /// </summary>
    public sealed class NetworkAssetResolver
    {
        private readonly NetworkManager _manager;

        internal NetworkAssetResolver(NetworkManager manager)
        {
            _manager = manager;
        }

        private NetworkAssets registry => _manager.networkAssets;

        public bool TryGetAsset(NetworkAssetID id, out Object asset)
        {
            var current = registry;
            if (current && id.isValid)
                return current.TryGetAsset((int)id, out asset);

            asset = null;
            return false;
        }

        /// <summary>
        /// Looks up the id of a registered asset. Silent by default; pass warnIfUnregistered
        /// to log once per asset when it isn't registered.
        /// </summary>
        public bool TryGetId(Object asset, out NetworkAssetID id, bool warnIfUnregistered = false)
        {
            var current = registry;
            if (current && asset)
            {
                int index = warnIfUnregistered ? current.GetIndex(asset) : (current.TryGetIndex(asset, out var i) ? i : -1);
                id = index;
                return id.isValid;
            }

            id = NetworkAssetID.invalid;
            return false;
        }

        public bool TryGetPersistentId(Object asset, out string persistentId)
        {
            var current = registry;
            if (current)
                return current.TryGetPersistentId(asset, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetAssetByPersistentId(string persistentId, out Object asset)
        {
            var current = registry;
            if (current)
                return current.TryGetAssetByPersistentId(persistentId, out asset);

            asset = null;
            return false;
        }
    }
}
