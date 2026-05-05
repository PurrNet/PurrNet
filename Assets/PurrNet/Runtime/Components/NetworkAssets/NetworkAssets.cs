using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkAssets", menuName = "PurrNet/Network Assets", order = -200)]
    public class NetworkAssets : ScriptableObject
    {
        public bool autoGenerate;
        public Object folder;

        [Tooltip("When no folder is set, search all of Assets/ instead of doing nothing.")]
        public bool searchAllIfNoFolder = true;

        [Tooltip("Will also get assets from these linked NetworkAssets. This is to allow further organization.")]
        public List<NetworkAssets> linkedNetworkAssets = new();

        [Serializable]
        public class TypeToggle
        {
            public string typeName;
            public bool enabled;
        }

        [SerializeField] private List<string> _enabledTypeNames = new();
        private HashSet<string> _enabledTypeLookup;

        public HashSet<string> enabledTypeNames
        {
            get
            {
                return _enabledTypeLookup ??= new HashSet<string>(_enabledTypeNames);
            }
        }

        public List<Object> assets = new();

        [SerializeField, HideInInspector]
        private List<string> _availableTypeNames = new();
        public IReadOnlyList<string> AvailableTypeNames => _availableTypeNames;

        /// <summary>
        /// GUIDs for each asset in the assets list, used for deterministic ID assignment.
        /// Populated during GenerateAssets() in editor.
        /// </summary>
        [SerializeField, HideInInspector] private List<string> _assetGuids = new();

        private readonly Dictionary<int, Object> idToAsset = new();
        private readonly Dictionary<Object, int> assetToId = new(ReferenceComparer.Instance);

        private sealed class ReferenceComparer : IEqualityComparer<Object>
        {
            public static readonly ReferenceComparer Instance = new();
            public bool Equals(Object x, Object y) => ReferenceEquals(x, y);
            public int GetHashCode(Object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        [SerializeField, HideInInspector] private List<int> _bakedIds = new();
        [SerializeField, HideInInspector] private List<Object> _bakedAssets = new();

        public Object GetAsset(int index) => idToAsset.GetValueOrDefault(index);

        public int GetIndex(Object obj)
        {
            return assetToId.GetValueOrDefault(obj, -1);
        }

        private void OnEnable()
        {
            idToAsset.Clear();
            assetToId.Clear();

            if (_bakedAssets.Count == 0 &&
                (assets.Count > 0 || linkedNetworkAssets is { Count: > 0 }))
            {
                Refresh();
                return;
            }

            for (int i = 0; i < _bakedAssets.Count; i++)
            {
                var obj = _bakedAssets[i];
                int id = _bakedIds[i];
                if (!obj) continue;

                try
                {
                    idToAsset[id] = obj;
                    assetToId[obj] = id;
                }
                catch
                {
                    idToAsset.Remove(id);
                }
            }

            if (IsBakeStale())
                Refresh();
        }

        private bool IsBakeStale()
        {
            var visited = new HashSet<NetworkAssets>();
            return Check(this);

            bool Check(NetworkAssets na)
            {
                if (!na || !visited.Add(na)) return false;

                for (int i = 0; i < na.assets.Count; i++)
                {
                    var obj = na.assets[i];
                    if (obj && !assetToId.ContainsKey(obj))
                        return true;
                }

                if (na.linkedNetworkAssets == null) return false;
                for (int i = 0; i < na.linkedNetworkAssets.Count; i++)
                {
                    if (Check(na.linkedNetworkAssets[i]))
                        return true;
                }

                return false;
            }
        }

        public IReadOnlyList<Object> AllAssets => assets;
        public IReadOnlyDictionary<int, Object> IndexToAsset => idToAsset;
        public IReadOnlyDictionary<Object, int> AssetToIndex => assetToId;

        public void Refresh()
        {
            idToAsset.Clear();
            assetToId.Clear();
            _bakedIds.Clear();
            _bakedAssets.Clear();

            // Collect all assets including from linked instances
            var visited = new HashSet<NetworkAssets>();
            var seenObjects = new HashSet<Object>();
            var buffer = new List<(Object asset, string guid)>();

            Collect(this);

            for (int i = 0; i < buffer.Count; i++)
            {
                var obj = buffer[i].asset;
                if (assetToId.ContainsKey(obj)) continue;

                idToAsset[i] = obj;
                assetToId[obj] = i;

                _bakedIds.Add(i);
                _bakedAssets.Add(obj);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            return;

            void Collect(NetworkAssets na)
            {
                if (!na || !visited.Add(na)) return;

                for (int i = 0; i < na.assets.Count; i++)
                {
                    var obj = na.assets[i];
                    if (!obj || !seenObjects.Add(obj)) continue;

                    string guid = (i < na._assetGuids.Count) ? na._assetGuids[i] : null;
                    buffer.Add((obj, guid));
                }

                if (na.linkedNetworkAssets == null) return;
                for (int i = 0; i < na.linkedNetworkAssets.Count; i++)
                {
                    var link = na.linkedNetworkAssets[i];
                    if (link) Collect(link);
                }
            }
        }

        public void AddAsset(Object obj, bool logIfDuplicate = true)
        {
            if (!obj) return;

            if (assetToId.TryGetValue(obj, out _))
            {
                if (logIfDuplicate)
                    Debug.LogWarning($"Asset already exists in NetworkAssets: {obj.name}");
                return;
            }

            assets.Add(obj);
            Refresh();
        }

        public void CacheAvailableTypes(IEnumerable<Type> types)
        {
            _availableTypeNames = types.Select(t => t.AssemblyQualifiedName).Distinct().ToList();
        }

        public void SetEnabledType(string typeName, bool enable)
        {
            if (enable)
            {
                if (!_enabledTypeNames.Contains(typeName))
                    _enabledTypeNames.Add(typeName);
            }
            else
            {
                _enabledTypeNames.Remove(typeName);
            }

            _enabledTypeLookup = null;
        }

#if UNITY_EDITOR
        public void GenerateAssets()
        {
            var enabledTypes = enabledTypeNames.Select(Type.GetType).Where(t => t != null).ToArray();
            var found = AssetScannerUtility.ScanAssets(folder, enabledTypes, searchAllIfNoFolder);

            if (found.Count == 0 && folder == null && !searchAllIfNoFolder)
                return;

            // Add newly found assets not already in the list
            var existingSet = new HashSet<Object>(assets);
            bool changed = false;

            foreach (var scan in found)
            {
                if (existingSet.Add(scan.asset))
                {
                    assets.Add(scan.asset);
                    changed = true;
                }
            }

            // Update GUIDs list to match assets list
            RebuildGuids();

            if (changed)
            {
                Refresh();
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssets();
            }

            CleanupNullEntries();
        }

        /// <summary>
        /// Rebuilds the _assetGuids list from the current assets list using AssetDatabase.
        /// </summary>
        private void RebuildGuids()
        {
            _assetGuids.Clear();
            for (int i = 0; i < assets.Count; i++)
            {
                var obj = assets[i];
                if (!obj)
                {
                    _assetGuids.Add(null);
                    continue;
                }

                string path = UnityEditor.AssetDatabase.GetAssetPath(obj);
                string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);

                // For sub-assets, append local file ID to make GUID unique
                if (UnityEditor.AssetDatabase.IsSubAsset(obj))
                {
                    if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string _, out long localId))
                        guid = $"{guid}_{localId}";
                }

                _assetGuids.Add(guid);
            }
        }

        private void CleanupNullEntries()
        {
            int count = assets.Count;
            assets.RemoveAll(a => a == null);
            if (assets.Count != count)
            {
                RebuildGuids();
                Refresh();
            }
        }
#endif

        public bool TryGetAsset(int id, out Object obj) => idToAsset.TryGetValue(id, out obj);
        public bool TryGetId(Object obj, out int id) => assetToId.TryGetValue(obj, out id);
    }

}
