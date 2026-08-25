using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_5_OR_NEWER
using PurrSceneHandle = UnityEngine.SceneManagement.SceneHandle;
#else
using PurrSceneHandle = System.Int32;
#endif

namespace PurrNet.Modules
{
    [AddComponentMenu("")]
    internal sealed class PurrNetPoolRoot : MonoBehaviour
    {
    }

    public static class NetworkPoolManager
    {
        internal readonly struct ScenePoolKey : IEquatable<ScenePoolKey>
        {
            internal readonly NetworkManager manager;
            internal readonly PurrSceneHandle unitySceneHandle;
            internal readonly SceneID sceneId;

            internal ScenePoolKey(NetworkManager manager, Scene unityScene, SceneID sceneId)
            {
                this.manager = manager;
                unitySceneHandle = unityScene.handle;
                this.sceneId = sceneId;
            }

            public bool Equals(ScenePoolKey other) =>
                ReferenceEquals(manager, other.manager) &&
                unitySceneHandle == other.unitySceneHandle &&
                sceneId == other.sceneId;

            public override bool Equals(object obj) => obj is ScenePoolKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = RuntimeHelpers.GetHashCode(manager);
                    hash = (hash * 397) ^ unitySceneHandle.GetHashCode();
                    return (hash * 397) ^ sceneId.GetHashCode();
                }
            }
        }

        internal sealed class ScenePoolEntry
        {
            internal readonly HierarchyPool pool;
            internal int leaseCount;

            internal ScenePoolEntry(HierarchyPool pool)
            {
                this.pool = pool;
            }
        }

        /// <summary>
        /// Owns one reference to a scene pool. Dispose the lease when the hierarchy or extension
        /// no longer uses the pool; the pool is retired after the final matching role releases it.
        /// </summary>
        public sealed class ScenePoolLease : IDisposable
        {
            private readonly ScenePoolKey _key;
            private ScenePoolEntry _entry;

            public HierarchyPool pool => _entry?.pool;

            internal ScenePoolLease(ScenePoolKey key, ScenePoolEntry entry)
            {
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                var entry = _entry;
                if (entry == null)
                    return;

                _entry = null;
                ReleaseScenePool(_key, entry);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearPools()
        {
            foreach (var pool in _pools.Values)
                pool.Dispose();

            foreach (var entry in _scenePools.Values)
                entry.pool.Dispose();

            _pools.Clear();
            _scenePools.Clear();
        }

        private static readonly Dictionary<IPrefabProvider, HierarchyPool> _pools = new();
        private static readonly Dictionary<ScenePoolKey, ScenePoolEntry> _scenePools = new();

        /// <summary>
        /// Acquires the pool owned by this manager, physical Unity scene, and logical SceneID.
        /// Listen roles share only when all three ownership values match.
        /// </summary>
        public static ScenePoolLease AcquireScenePool(NetworkManager manager,
            Scene unityScene, SceneID scene)
        {
            var key = ValidateAndCreateScenePoolKey(manager, unityScene, scene);
            var entry = GetOrCreateScenePool(key, unityScene);
            checked
            {
                entry.leaseCount++;
            }

            return new ScenePoolLease(key, entry);
        }

        internal static HierarchyPool GetScenePool(NetworkManager manager,
            Scene unityScene, SceneID scene)
        {
            var key = ValidateAndCreateScenePoolKey(manager, unityScene, scene);
            return GetOrCreateScenePool(key, unityScene).pool;
        }

        private static ScenePoolKey ValidateAndCreateScenePoolKey(NetworkManager manager,
            Scene unityScene, SceneID scene)
        {
            if (!manager)
                throw new ArgumentNullException(nameof(manager));
            if (!unityScene.IsValid())
                throw new ArgumentException("A scene pool requires a valid physical Unity scene.",
                    nameof(unityScene));

            return new ScenePoolKey(manager, unityScene, scene);
        }

        private static ScenePoolEntry GetOrCreateScenePool(ScenePoolKey key, Scene unityScene)
        {
            if (_scenePools.TryGetValue(key, out var entry))
                return entry;

            var poolParent = new GameObject(
                $"PurrNetPool-{key.sceneId}-{key.unitySceneHandle}-" +
                RuntimeHelpers.GetHashCode(key.manager))
            {
#if PURRNET_DEBUG_POOLING
                hideFlags = HideFlags.DontSave
#else
                hideFlags = HideFlags.HideAndDontSave
#endif
            };
            poolParent.AddComponent<PurrNetPoolRoot>();

            SceneManager.MoveGameObjectToScene(poolParent, unityScene);

            entry = new ScenePoolEntry(new HierarchyPool(poolParent.transform));
            _scenePools.Add(key, entry);
            return entry;
        }

        private static void ReleaseScenePool(ScenePoolKey key, ScenePoolEntry expectedEntry)
        {
            if (!_scenePools.TryGetValue(key, out var entry) ||
                !ReferenceEquals(entry, expectedEntry))
                return;

            if (entry.leaseCount <= 0)
                throw new InvalidOperationException("A scene-pool lease was released too many times.");

            entry.leaseCount--;
            if (entry.leaseCount != 0)
                return;

            _scenePools.Remove(key);
            entry.pool.Dispose();
        }

        public static HierarchyPool GetPool(NetworkManager manager)
        {
            var prefabs = manager.prefabProvider;

            if (prefabs == null)
                return null;

            if (_pools.TryGetValue(prefabs, out var pool))
                return pool;

            var poolParent = new GameObject($"PurrNetPool-{_pools.Count}")
            {
#if PURRNET_DEBUG_POOLING
                hideFlags = HideFlags.DontSave
#else
                hideFlags = HideFlags.HideAndDontSave
#endif
            };
            poolParent.AddComponent<PurrNetPoolRoot>();

            UnityEngine.Object.DontDestroyOnLoad(poolParent);
            pool = new HierarchyPool(poolParent.transform, prefabs);
            _pools.Add(prefabs, pool);
            pool.Warmup();
            return pool;
        }

        public static void RemovePool(IPrefabProvider prefabs)
        {
            if (_pools.Remove(prefabs, out var pool))
                pool.Dispose();
        }

        internal static bool RemovePool(NetworkManager manager, Scene unityScene, SceneID scene)
        {
            if (!manager || !unityScene.IsValid())
                return false;

            var key = new ScenePoolKey(manager, unityScene, scene);
            if (!_scenePools.TryGetValue(key, out var entry) || entry.leaseCount != 0)
                return false;

            _scenePools.Remove(key);
            entry.pool.Dispose();
            return true;
        }

        /// <summary>
        /// Removes only unleased pools with the requested logical ID. An active hierarchy's
        /// ownership-scoped pool can never be destroyed through this compatibility cleanup.
        /// </summary>
        public static void RemovePool(SceneID scene)
        {
            List<ScenePoolKey> removable = null;
            foreach (var pair in _scenePools)
            {
                if (pair.Key.sceneId != scene || pair.Value.leaseCount != 0)
                    continue;

                removable ??= new List<ScenePoolKey>();
                removable.Add(pair.Key);
            }

            if (removable == null)
                return;

            for (var i = 0; i < removable.Count; i++)
            {
                var key = removable[i];
                if (_scenePools.Remove(key, out var entry))
                    entry.pool.Dispose();
            }
        }
    }
}
