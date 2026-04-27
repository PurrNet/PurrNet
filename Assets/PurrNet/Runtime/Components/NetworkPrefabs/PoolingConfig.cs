using System;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Shared pooling configuration used across all prefab provider entry types.
    /// Compose this into any entry struct that needs pooling support.
    /// </summary>
    [Serializable]
    public struct PoolingConfig
    {
        [Tooltip("Whether instances of this prefab should be pooled for reuse.")]
        public bool pooled;

        [Tooltip("Number of instances to pre-create when warming up the pool.")]
        public int warmupCount;

        public static PoolingConfig Default(bool poolByDefault = false)
        {
            return new PoolingConfig
            {
                pooled = poolByDefault,
                warmupCount = 5
            };
        }
    }
}
