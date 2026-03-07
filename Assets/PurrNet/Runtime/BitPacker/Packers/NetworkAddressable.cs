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
        
        public Task<IAsyncPackable> PrepareForPackAsync()
        {
            if (addressablesReference == null)
                return Task.FromResult<IAsyncPackable>(this);

            GUID = addressablesReference.AssetGUID;
            return Task.FromResult<IAsyncPackable>(this);
        }

        public async Task<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            if (string.IsNullOrEmpty(GUID))
            {
                PurrLogger.LogError($"Failed to unpack network addressable as the GUID is empty", addressablesReference?.Asset);
                return this;
            }

            var handle = Addressables.LoadAssetAsync<Object>(GUID);
            await handle.Task;
            addressablesReference = new AssetReference(GUID);
            return this;
        }
    }
}
#endif
