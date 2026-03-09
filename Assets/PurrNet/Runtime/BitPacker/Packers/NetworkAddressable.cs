#if ADDRESSABLES_PURRNET_SUPPORT
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PurrNet
{
    public struct NetworkAddressable : IPackedAuto, IAsyncPackable
    {
        [DontPack] public AssetReference addressablesReference;
        private string GUID;

        public NetworkAddressable(AssetReference addressablesReference)
        {
            this.addressablesReference = addressablesReference;
            GUID = null;
        }

        /// <summary>The loaded asset, or null if not yet resolved.</summary>
        public Object Asset => addressablesReference != null ? addressablesReference.Asset : null;

        /// <summary>Whether the addressable has been resolved and the asset is loaded.</summary>
        public bool IsValid => addressablesReference != null && addressablesReference.Asset != null;

        public static implicit operator NetworkAddressable(AssetReference reference) => new(reference);

        public static implicit operator AssetReference(NetworkAddressable networkAddressable) =>
            networkAddressable.addressablesReference;

        public ValueTask<IAsyncPackable> PrepareForPackAsync()
        {
            if (addressablesReference != null)
                GUID = addressablesReference.AssetGUID;
            return new ValueTask<IAsyncPackable>(this);
        }

        public async ValueTask<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            if (string.IsNullOrEmpty(GUID))
            {
                PurrLogger.LogError("Failed to unpack network addressable as the GUID is empty");
                return this;
            }

            addressablesReference = new AssetReference(GUID);
            await addressablesReference.LoadAssetAsync<Object>().Task;
            return this;
        }

        public override string ToString()
        {
            if (Asset)
                return Asset.name;
            if (!string.IsNullOrEmpty(GUID))
                return $"NetworkAddressable({GUID}, not loaded)";
            return "NetworkAddressable(empty)";
        }
    }
}
#endif
