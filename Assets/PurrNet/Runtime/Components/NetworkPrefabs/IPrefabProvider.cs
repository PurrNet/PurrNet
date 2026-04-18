using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PurrNet
{
    public interface IAsyncPrefabProvider : IPrefabProvider
    {
        bool NeedsLoad(int prefabId);
        Task<PrefabData> LoadPrefabAsync(int prefabId);
    }

    public struct PrefabData
    {
        public int prefabId;
        public GameObject prefab;
        public PoolingConfig pooling;

        /// <summary>Shim for backwards compatibility. Use pooling.pooled instead.</summary>
        public bool pooled
        {
            get => pooling.pooled;
            set => pooling.pooled = value;
        }

        /// <summary>Shim for backwards compatibility. Use pooling.warmupCount instead.</summary>
        public int warmupCount
        {
            get => pooling.warmupCount;
            set => pooling.warmupCount = value;
        }
    }

    public interface IPrefabProvider
    {
        IEnumerable<PrefabData> allPrefabs { get; }

        bool TryGetPrefabData(int prefabId, out PrefabData prefabData);

        bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData);

        void Refresh();
    }

    public abstract class PrefabProviderScriptable : ScriptableObject, IPrefabProvider
    {
        public abstract IEnumerable<PrefabData> allPrefabs { get; }

        public abstract bool TryGetPrefabData(int prefabId, out PrefabData prefabData);

        public abstract bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData);

        public abstract void Refresh();
    }
}
