#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "AddressableNetworkPrefabs", menuName = "PurrNet/Network Prefabs/Addressable Network Prefabs", order = -200)]
    public class AddressableNetworkPrefabs : PrefabProviderScriptable, IAddressablePrefabProvider
    {
        [Serializable]
        public struct Entry
        {
            public AssetReferenceGameObject asset;
        }

        [SerializeField] private bool _preloadAtStartup = true;
        [SerializeField] private List<Entry> _entries = new();

        [Tooltip("Will also get entries from these linked AddressableNetworkPrefabs.")]
        public List<AddressableNetworkPrefabs> linkedAddressablePrefabs = new();

        public bool autoGenerate;
        public UnityEngine.Object folder;

        [Tooltip("When no folder is set, search all of Assets/ instead of doing nothing.")]
        public bool searchAllIfNoFolder;

        /// <summary>
        /// Whether all registered Addressable prefabs have been loaded and are ready for use.
        /// </summary>
        public bool isLoaded { get; private set; }

        private readonly Dictionary<int, PrefabData> _prefabLookup = new();
        private readonly Dictionary<string, int> _guidToId = new();
        private readonly Dictionary<int, string> _idToGuid = new();
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _handles = new();
        private readonly Dictionary<string, Task<PrefabData>> _pendingLoads = new();
        private int _generation;
        private readonly List<RuntimePrefabEntry> _runtimePrefabs = new();
        private readonly HashSet<string> _warnedNameFallbacks = new();
        private int _runtimePrefabStartId;

        private struct RuntimePrefabEntry
        {
            public string uniqueName;
            public GameObject prefab;
            public bool pooled;
            public int warmupCount;
        }

        /// <summary>
        /// The entries registered in this asset. Read-only access for external inspection.
        /// </summary>
        public IReadOnlyList<Entry> entries => _entries;

        /// <summary>
        /// Number of registered entries, including those from linked AddressableNetworkPrefabs.
        /// </summary>
        public int count => CountEntries(this, new HashSet<AddressableNetworkPrefabs>());

        private static int CountEntries(AddressableNetworkPrefabs provider, HashSet<AddressableNetworkPrefabs> visited)
        {
            if (!provider || !visited.Add(provider)) return 0;

            int total = provider._entries.Count;
            if (provider.linkedAddressablePrefabs == null) return total;

            for (int i = 0; i < provider.linkedAddressablePrefabs.Count; i++)
                total += CountEntries(provider.linkedAddressablePrefabs[i], visited);
            return total;
        }

        public bool preloadAtStartup
        {
            get => _preloadAtStartup;
            set => _preloadAtStartup = value;
        }

        public override IEnumerable<PrefabData> allPrefabs => _prefabLookup.Values;
        public IEnumerable<string> persistentIds => _guidToId.Keys;

        /// <summary>
        /// Tries to get the loaded prefab data for a given asset GUID.
        /// Returns false if the GUID is not registered or the prefab isn't loaded yet.
        /// </summary>
        public bool TryGetPrefabDataByGuid(string assetGuid, out PrefabData prefabData)
        {
            if (_guidToId.TryGetValue(assetGuid, out int id))
                return _prefabLookup.TryGetValue(id, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            return TryGetPrefabDataByGuid(persistentId, out prefabData);
        }

        public override bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return _prefabLookup.TryGetValue(prefabId, out prefabData);
        }

        public override bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            foreach (var data in _prefabLookup.Values)
            {
                if (data.prefab == prefab)
                {
                    prefabData = data;
                    return true;
                }
            }

            if (!TryMatchByName(_prefabLookup.Values, prefab, out prefabData))
                return false;

            if (_warnedNameFallbacks.Add(prefab.name))
            {
                PurrLogger.LogWarning(
                    $"Resolved addressable network prefab `{prefab.name}` by name because the given reference isn't the registered one.\n" +
                    "This happens when the same asset is duplicated across bundles; fix the Addressables group setup to avoid spawning the wrong prefab.");
            }

            return true;
        }

        public override void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        {
            _runtimePrefabs.Add(new RuntimePrefabEntry
            {
                uniqueName = uniqueName,
                prefab = prefab,
                pooled = pooled,
                warmupCount = warmup
            });

            Refresh();
        }

        public bool TryGetGuid(int localPrefabId, out string assetGuid)
        {
            return _idToGuid.TryGetValue(localPrefabId, out assetGuid);
        }

        /// <summary>
        /// Like <see cref="TryGetGuid"/>, but false for prefabs added at runtime: those are never loaded or released.
        /// </summary>
        public bool TryGetAddressableGuid(int prefabId, out string assetGuid)
        {
            if (prefabId < _runtimePrefabStartId)
                return TryGetGuid(prefabId, out assetGuid);

            assetGuid = null;
            return false;
        }

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            return TryGetGuid(prefabId, out persistentId);
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (TryGetPrefabData(prefab, out var prefabData))
                return TryGetPersistentId((int)prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabByPersistentId(string persistentId, out GameObject prefab)
        {
            if (TryGetPrefabDataByPersistentId(persistentId, out var prefabData))
            {
                prefab = prefabData.prefab;
                return prefab;
            }

            prefab = null;
            return false;
        }

        /// <summary>Raised for every AddressableNetworkPrefabs asset. Prefer <see cref="loadStateChanged"/>.</summary>
        public static event Action<string, bool> onLoadStateChanged;

        public event Action<string, bool> loadStateChanged;

        private void NotifyLoadStateChanged(string guid, bool loaded)
        {
            loadStateChanged?.Invoke(guid, loaded);
            onLoadStateChanged?.Invoke(guid, loaded);
        }

        public IEnumerable<string> GetLoadedGuids()
        {
            foreach (var kvp in _prefabLookup)
            {
                if (kvp.Key < _runtimePrefabStartId && kvp.Value.prefab != null &&
                    _idToGuid.TryGetValue(kvp.Key, out var g))
                    yield return g;
            }
        }

        public override void Refresh()
        {
            RebuildLookup();
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        /// <summary>
        /// Rebuilds the internal lookup tables from the entry list.
        /// Assigns IDs in entry order, so every peer must use the same entry list.
        /// Includes entries from linked AddressableNetworkPrefabs (deduped by GUID).
        /// Note: PrefabData.prefab is null for every prefab that isn't loaded.
        /// The IDs assigned here are local to this provider and will be
        /// offset by the CompositePrefabProvider when combined with other providers.
        /// </summary>
        private void RebuildLookup()
        {
            _prefabLookup.Clear();
            _guidToId.Clear();
            _idToGuid.Clear();

            var seenGuids = new HashSet<string>();
            var sorted = new List<(string guid, Entry entry)>();
            var visited = new HashSet<AddressableNetworkPrefabs>();

            void CollectEntries(AddressableNetworkPrefabs provider)
            {
                if (!provider || !visited.Add(provider)) return;

                for (int i = 0; i < provider._entries.Count; i++)
                {
                    var entry = provider._entries[i];
                    if (entry.asset == null || !entry.asset.RuntimeKeyIsValid())
                        continue;

                    var guid = entry.asset.AssetGUID;
                    if (string.IsNullOrEmpty(guid) || !seenGuids.Add(guid))
                        continue;

                    sorted.Add((guid, entry));
                }

                if (provider.linkedAddressablePrefabs == null) return;
                for (int i = 0; i < provider.linkedAddressablePrefabs.Count; i++)
                {
                    var link = provider.linkedAddressablePrefabs[i];
                    if (link) CollectEntries(link);
                }
            }

            CollectEntries(this);

            for (int i = 0; i < sorted.Count; i++)
            {
                var (guid, _) = sorted[i];

                _guidToId[guid] = i;
                _idToGuid[i] = guid;
                _prefabLookup[i] = new PrefabData
                {
                    prefabId = i,
                    prefab = TryGetLoaded(guid, out var loaded) ? loaded : null,
                    pooled = false,
                    warmupCount = 0
                };
            }

            int runtimeStart = sorted.Count;
            _runtimePrefabStartId = runtimeStart;
            for (int i = 0; i < _runtimePrefabs.Count; i++)
            {
                var rt = _runtimePrefabs[i];
                if (!rt.prefab) continue;

                int id = runtimeStart + i;
                if (!string.IsNullOrEmpty(rt.uniqueName) && seenGuids.Add(rt.uniqueName))
                {
                    _guidToId[rt.uniqueName] = id;
                    _idToGuid[id] = rt.uniqueName;
                }

                _prefabLookup[id] = new PrefabData
                {
                    prefabId = id,
                    prefab = rt.prefab,
                    pooled = rt.pooled,
                    warmupCount = rt.warmupCount
                };
            }
        }

        public bool NeedsLoad(int prefabId)
        {
            if (!_prefabLookup.TryGetValue(prefabId, out var data))
                return false;
            return data.prefab == null;
        }

        private bool TryGetLoaded(string guid, out GameObject prefab)
        {
            if (_handles.TryGetValue(guid, out var handle) && handle.IsValid() &&
                handle.Status == AsyncOperationStatus.Succeeded && handle.Result)
            {
                prefab = handle.Result;
                return true;
            }

            prefab = null;
            return false;
        }

        public Task<PrefabData> LoadPrefabByGuidAsync(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !_guidToId.TryGetValue(assetGuid, out int localId))
            {
                PurrLogger.LogError($"LoadPrefabByGuidAsync: GUID '{assetGuid}' not registered.");
                return Task.FromResult(default(PrefabData));
            }

            return LoadPrefabAsync(localId);
        }

        /// <summary>
        /// Loads one prefab. Calls made while the same prefab is still loading share that load.
        /// </summary>
        public Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!_prefabLookup.TryGetValue(prefabId, out var data))
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not found in AddressableNetworkPrefabs.");
                return Task.FromResult(default(PrefabData));
            }

            if (data.prefab != null)
                return Task.FromResult(data);

            if (!_idToGuid.TryGetValue(prefabId, out var guid))
            {
                PurrLogger.LogError($"LoadPrefabAsync: no GUID mapping for prefabId {prefabId}.");
                return Task.FromResult(default(PrefabData));
            }

            if (_pendingLoads.TryGetValue(guid, out var pending))
                return pending;

            var task = LoadPrefabCoreAsync(guid);
            if (!task.IsCompleted)
                _pendingLoads[guid] = task;
            return task;
        }

        private async Task<PrefabData> LoadPrefabCoreAsync(string guid)
        {
            int generation = _generation;
            AsyncOperationHandle<GameObject> handle = default;

            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(guid);
                await handle.Task;

                // ReleaseAll ran while this was loading: nobody is waiting for the prefab anymore.
                if (generation != _generation)
                {
                    if (handle.IsValid())
                        Addressables.Release(handle);
                    return default;
                }

                _pendingLoads.Remove(guid);

                // A blocking load finished the same prefab while this one was waiting its turn.
                if (_handles.ContainsKey(guid))
                {
                    if (handle.IsValid())
                        Addressables.Release(handle);

                    return _guidToId.TryGetValue(guid, out int loadedId) &&
                           _prefabLookup.TryGetValue(loadedId, out var loadedData)
                        ? loadedData
                        : default;
                }

                if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || !handle.Result)
                {
                    PurrLogger.LogError($"Failed to load Addressable prefab with GUID '{guid}'.");
                    if (handle.IsValid())
                        Addressables.Release(handle);
                    return default;
                }

                // The entry list may have been refreshed while loading, so resolve the id again.
                if (!_guidToId.TryGetValue(guid, out int prefabId) ||
                    !_prefabLookup.TryGetValue(prefabId, out var data))
                {
                    Addressables.Release(handle);
                    return default;
                }

                _handles[guid] = handle;
                data.prefab = handle.Result;
                _prefabLookup[prefabId] = data;
                NotifyLoadStateChanged(guid, true);
                return data;
            }
            catch (Exception e)
            {
                if (generation == _generation)
                    _pendingLoads.Remove(guid);
                if (handle.IsValid())
                    Addressables.Release(handle);
                PurrLogger.LogError($"Exception loading Addressable prefab GUID '{guid}': {e.Message}\n{e.StackTrace}");
                return default;
            }
        }

        /// <summary>
        /// Loads one prefab and blocks until it is there. A load of the same prefab that is still
        /// running is finished by this call and completes on its own afterwards.
        /// </summary>
        public PrefabData LoadPrefabByGuid(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !_guidToId.TryGetValue(assetGuid, out int prefabId) ||
                !_prefabLookup.TryGetValue(prefabId, out var data))
            {
                PurrLogger.LogError($"LoadPrefabByGuid: GUID '{assetGuid}' not registered.");
                return default;
            }

            if (data.prefab != null)
                return data;

            AsyncOperationHandle<GameObject> handle = default;

            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(assetGuid);
                handle.WaitForCompletion();

                if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || !handle.Result)
                {
                    PurrLogger.LogError($"Failed to load Addressable prefab with GUID '{assetGuid}'.");
                    if (handle.IsValid())
                        Addressables.Release(handle);
                    return default;
                }
            }
            catch (Exception e)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                PurrLogger.LogError($"Exception loading Addressable prefab GUID '{assetGuid}': {e.Message}\n{e.StackTrace}");
                return default;
            }

            _handles[assetGuid] = handle;
            data.prefab = handle.Result;
            _prefabLookup[prefabId] = data;
            NotifyLoadStateChanged(assetGuid, true);
            return data;
        }

        /// <summary>
        /// Loads all registered Addressable prefabs into memory.
        /// Must be called (and awaited) before the network is started.
        /// After loading, PrefabData.prefab will contain valid references.
        /// </summary>
        public async Task LoadAllAsync()
        {
            ReleaseAll();
            RebuildLookup();

            var sortedGuids = new List<string>(_guidToId.Count);
            foreach (var kvp in _guidToId)
            {
                if (kvp.Value < _runtimePrefabStartId)
                    sortedGuids.Add(kvp.Key);
            }

            sortedGuids.Sort(StringComparer.Ordinal);

            int generation = _generation;

            for (int i = 0; i < sortedGuids.Count; i++)
            {
                await LoadPrefabByGuidAsync(sortedGuids[i]);

                if (generation != _generation)
                    return;
            }

            isLoaded = true;
        }

        /// <summary>
        /// Releases one loaded prefab. Returns false when it wasn't loaded.
        /// Instances that still exist lose the assets they share with the prefab, so despawn them first.
        /// </summary>
        public bool ReleasePrefabByGuid(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !_handles.Remove(assetGuid, out var handle))
                return false;

            if (_guidToId.TryGetValue(assetGuid, out int prefabId) &&
                _prefabLookup.TryGetValue(prefabId, out var data))
            {
                data.prefab = null;
                _prefabLookup[prefabId] = data;
            }

            if (handle.IsValid())
                Addressables.Release(handle);

            isLoaded = false;
            NotifyLoadStateChanged(assetGuid, false);
            return true;
        }

        /// <summary>
        /// Releases all loaded Addressable handles.
        /// Call this when the network shuts down or the provider is no longer needed.
        /// </summary>
        public void ReleaseAll()
        {
            _generation++;
            _pendingLoads.Clear();
            isLoaded = false;

            if (_handles.Count == 0)
                return;

            var guids = new List<string>(_handles.Keys);
            for (int i = 0; i < guids.Count; i++)
                ReleasePrefabByGuid(guids[i]);
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (autoGenerate &&
                !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
                !UnityEditor.EditorApplication.isCompiling &&
                !UnityEditor.EditorApplication.isUpdating)
            {
                UnityEditor.EditorApplication.delayCall += OnAutoGenerate;
            }
        }

        private void OnAutoGenerate()
        {
            // Actual generation is handled by the editor (AddressableNetworkPrefabsEditor)
            // because it requires UnityEditor.AddressableAssets which is in a separate editor assembly.
            // This triggers via onAutoGenerateRequested event.
            if (!this) return;
            onAutoGenerateRequested?.Invoke(this);
        }

        /// <summary>
        /// Event fired when auto-generation is triggered via OnValidate.
        /// The editor subscribes to this to perform the actual generation.
        /// </summary>
        public static event Action<AddressableNetworkPrefabs> onAutoGenerateRequested;

        /// <summary>
        /// Gets the set of GUIDs already registered as entries.
        /// Used by the editor during generation to avoid duplicates.
        /// </summary>
        public HashSet<string> GetExistingGuids()
        {
            var guids = new HashSet<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.asset != null && entry.asset.RuntimeKeyIsValid())
                    guids.Add(entry.asset.AssetGUID);
            }
            return guids;
        }

        /// <summary>
        /// Adds an entry by asset reference. Used by the editor during generation.
        /// </summary>
        public void AddEntry(AssetReferenceGameObject assetRef)
        {
            _entries.Add(new Entry
            {
                asset = assetRef
            });
        }
#endif
    }
}
#endif
