using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;
#if UNITY_6000_3_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkAssets", menuName = "PurrNet/Network Assets", order = -200)]
    public class NetworkAssets : ScriptableObject, ISerializationCallbackReceiver
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

        /// <summary>
        /// Asset and its persistent-id guid serialized together so they can never desync.
        /// Guids are populated in editor from the AssetDatabase; sub-assets use the guid_localId format.
        /// </summary>
        [Serializable]
        public struct AssetEntry
        {
            public Object asset;
            public string guid;
        }

        public List<AssetEntry> entries = new();

        [SerializeField, HideInInspector, FormerlySerializedAs("assets")]
        private List<Object> _legacyAssets = new();

        [SerializeField, HideInInspector, FormerlySerializedAs("_assetGuids")]
        private List<string> _legacyAssetGuids = new();

        [SerializeField, HideInInspector]
        private List<string> _availableTypeNames = new();
        public IReadOnlyList<string> AvailableTypeNames => _availableTypeNames;

        private readonly Dictionary<int, Object> idToAsset = new();
        private readonly Dictionary<ObjectId, int> objectIdToId = new();
        private readonly Dictionary<string, Object> persistentIdToAsset = new();
        private readonly Dictionary<string, int> persistentIdToId = new();
        private readonly Dictionary<int, string> idToPersistentId = new();
        private readonly Dictionary<ObjectId, string> objectIdToPersistentId = new();

        private const int AmbiguousMarker = -2;
        private readonly Dictionary<(Type, string), int> typeNameToId = new();
        private HashSet<(Type, string)> _warnedAmbiguous;
        private HashSet<ObjectId> _warnedUnresolved;

        [Serializable]
        public struct BakedEntry
        {
            public int id;
            public Object asset;
            public string persistentId;
        }

        [SerializeField, HideInInspector] private List<BakedEntry> _baked = new();

        [SerializeField, HideInInspector, FormerlySerializedAs("_bakedIds")]
        private List<int> _legacyBakedIds = new();

        [SerializeField, HideInInspector, FormerlySerializedAs("_bakedAssets")]
        private List<Object> _legacyBakedAssets = new();

        [SerializeField, HideInInspector, FormerlySerializedAs("_bakedPersistentIds")]
        private List<string> _legacyBakedPersistentIds = new();

        private List<Object> _assetsView;
        private bool _legacyMigrated;

        public Object GetAsset(int index) => idToAsset.GetValueOrDefault(index);
        public Object GetAssetByPersistentId(string persistentId)
        {
            return !string.IsNullOrEmpty(persistentId) &&
                   persistentIdToAsset.TryGetValue(persistentId, out var obj)
                ? obj
                : null;
        }

        public int GetIndex(Object obj)
        {
            if (!obj) return -1;

            if (TryGetIndex(obj, out int id))
                return id;

            WarnUnresolvedOnce(obj, GetObjectId(obj));
            return -1;
        }

        /// <summary>
        /// Same lookup as GetIndex but silent — no warning when the asset isn't registered.
        /// Use this to probe registration of assets that are allowed to be unregistered.
        /// </summary>
        public bool TryGetIndex(Object obj, out int id)
        {
            id = -1;
            if (!obj) return false;

            ObjectId objectId = GetObjectId(obj);
            if (objectIdToId.TryGetValue(objectId, out id))
                return true;

            if (TryResolveDuplicate(obj, out id))
            {
                objectIdToId[objectId] = id;
                return true;
            }

            id = -1;
            return false;
        }

        public bool TryGetPersistentId(Object obj, out string persistentId)
        {
            persistentId = null;
            if (!obj) return false;

            ObjectId objectId = GetObjectId(obj);
            if (objectIdToPersistentId.TryGetValue(objectId, out persistentId))
                return true;

            if (!TryResolveDuplicate(obj, out int id))
                return false;

            return TryGetPersistentId(id, out persistentId);
        }

        public bool TryGetPersistentId(int id, out string persistentId)
        {
            return idToPersistentId.TryGetValue(id, out persistentId);
        }

        public bool TryGetAssetByPersistentId(string persistentId, out Object obj)
        {
            if (!string.IsNullOrEmpty(persistentId) &&
                persistentIdToAsset.TryGetValue(persistentId, out obj))
                return true;

            obj = null;
            return false;
        }

        public bool TryGetIdByPersistentId(string persistentId, out int id)
        {
            if (!string.IsNullOrEmpty(persistentId) &&
                persistentIdToId.TryGetValue(persistentId, out id))
                return true;

            id = -1;
            return false;
        }

        private bool TryResolveDuplicate(Object obj, out int id)
        {
            if (typeNameToId.TryGetValue((obj.GetType(), obj.name), out id))
            {
                if (id == AmbiguousMarker)
                {
                    WarnAmbiguousOnce(obj);
                    id = -1;
                    return false;
                }
                return true;
            }
            id = -1;
            return false;
        }

        public bool TryGetCanonical(Object maybeDuplicate, out Object canonical)
        {
            canonical = null;
            int id = GetIndex(maybeDuplicate);
            if (id < 0) return false;
            return idToAsset.TryGetValue(id, out canonical);
        }

        private void WarnAmbiguousOnce(Object obj)
        {
            _warnedAmbiguous ??= new HashSet<(Type, string)>();
            var key = (obj.GetType(), obj.name);
            if (_warnedAmbiguous.Add(key))
            {
                Debug.LogWarning(
                    $"NetworkAssets: cannot resolve duplicate managed instance of '{obj.name}' " +
                    $"({obj.GetType().Name}) — multiple registered assets share the same (Type, name). " +
                    $"Returning -1.", this);
            }
        }

        private void WarnUnresolvedOnce(Object obj, ObjectId objectId)
        {
            _warnedUnresolved ??= new HashSet<ObjectId>();
            if (_warnedUnresolved.Add(objectId))
            {
                Debug.LogWarning(
                    $"NetworkAssets: could not resolve '{obj.name}' ({obj.GetType().Name}, " +
                    $"id={objectId}) - not registered and no (Type, name) fallback matched.",
                    this);
            }
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            _assetsView = null;
            MigrateLegacyEntries();
            MigrateLegacyBake();
        }

        private void MigrateLegacyEntries()
        {
            if (_legacyAssets.Count == 0 && _legacyAssetGuids.Count == 0)
                return;

            if (entries.Count > 0)
            {
                Debug.LogWarning(
                    $"NetworkAssets: found both migrated entries ({entries.Count}) and legacy asset/guid lists " +
                    $"({_legacyAssets.Count}/{_legacyAssetGuids.Count}). Keeping the migrated entries and " +
                    "discarding the legacy lists; verify the asset list after this load.");
            }
            else
            {
                if (_legacyAssetGuids.Count == 0 || _legacyAssetGuids.Count == _legacyAssets.Count)
                {
                    for (int i = 0; i < _legacyAssets.Count; i++)
                    {
                        entries.Add(new AssetEntry
                        {
                            asset = _legacyAssets[i],
                            guid = i < _legacyAssetGuids.Count ? _legacyAssetGuids[i] : null
                        });
                    }
                }
                else
                {
                    for (int i = 0; i < _legacyAssets.Count; i++)
                        entries.Add(new AssetEntry { asset = _legacyAssets[i] });

                    Debug.LogError(
                        $"NetworkAssets: legacy asset list ({_legacyAssets.Count}) and guid list " +
                        $"({_legacyAssetGuids.Count}) have mismatched counts. Persistent ids were dropped " +
                        "for these entries instead of guessing pairings" +
#if UNITY_EDITOR
                        "; they will be regenerated from the AssetDatabase on the next refresh.");
#else
                        "; persistent id lookups will fail for this container.");
#endif
                }
            }

            _legacyAssets.Clear();
            _legacyAssetGuids.Clear();
            _legacyMigrated = true;
        }

        private void MigrateLegacyBake()
        {
            if (_legacyBakedIds.Count == 0 && _legacyBakedAssets.Count == 0 && _legacyBakedPersistentIds.Count == 0)
                return;

            if (_baked.Count == 0 &&
                _legacyBakedIds.Count == _legacyBakedAssets.Count &&
                _legacyBakedPersistentIds.Count == _legacyBakedAssets.Count)
            {
                for (int i = 0; i < _legacyBakedAssets.Count; i++)
                {
                    _baked.Add(new BakedEntry
                    {
                        id = _legacyBakedIds[i],
                        asset = _legacyBakedAssets[i],
                        persistentId = _legacyBakedPersistentIds[i]
                    });
                }
            }

            _legacyBakedIds.Clear();
            _legacyBakedAssets.Clear();
            _legacyBakedPersistentIds.Clear();
            _legacyMigrated = true;
        }

        private void OnEnable()
        {
            ClearLookups();

            if (_legacyMigrated)
            {
                _legacyMigrated = false;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            if (_baked.Count == 0 &&
                (entries.Count > 0 || linkedNetworkAssets is { Count: > 0 }))
            {
                Refresh();
                return;
            }

            for (int i = 0; i < _baked.Count; i++)
            {
                var baked = _baked[i];
                var obj = baked.asset;
                if (!obj) continue;

                try
                {
                    idToAsset[baked.id] = obj;
                    objectIdToId[GetObjectId(obj)] = baked.id;
                    RegisterTypeNameFallback(obj, baked.id);
                    RegisterPersistentId(obj, baked.id, baked.persistentId);
                }
                catch
                {
                    idToAsset.Remove(baked.id);
                }
            }

            if (IsBakeStale())
                Refresh();
        }

        private void ClearLookups()
        {
            idToAsset.Clear();
            objectIdToId.Clear();
            persistentIdToAsset.Clear();
            persistentIdToId.Clear();
            idToPersistentId.Clear();
            objectIdToPersistentId.Clear();
            typeNameToId.Clear();
            _warnedAmbiguous?.Clear();
            _warnedUnresolved?.Clear();
        }

        private void RegisterPersistentId(Object obj, int id, string persistentId)
        {
            if (!obj || string.IsNullOrEmpty(persistentId))
                return;

            if (!persistentIdToAsset.ContainsKey(persistentId))
            {
                persistentIdToAsset.Add(persistentId, obj);
                persistentIdToId.Add(persistentId, id);
            }

            idToPersistentId[id] = persistentId;
            objectIdToPersistentId[GetObjectId(obj)] = persistentId;
        }

        private void RegisterTypeNameFallback(Object obj, int id)
        {
            var key = (obj.GetType(), obj.name);
            if (typeNameToId.TryGetValue(key, out int existing))
            {
                if (existing != id)
                    typeNameToId[key] = AmbiguousMarker;
            }
            else
            {
                typeNameToId[key] = id;
            }
        }

        private bool IsBakeStale()
        {
            var visited = new HashSet<NetworkAssets>();
            return Check(this);

            bool Check(NetworkAssets na)
            {
                if (!na || !visited.Add(na)) return false;

                for (int i = 0; i < na.entries.Count; i++)
                {
                    var obj = na.entries[i].asset;
                    if (obj && !objectIdToId.ContainsKey(GetObjectId(obj)))
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

        public IReadOnlyList<Object> assets
        {
            get
            {
                if (_assetsView == null)
                {
                    _assetsView = new List<Object>(entries.Count);
                    for (int i = 0; i < entries.Count; i++)
                        _assetsView.Add(entries[i].asset);
                }

                return _assetsView;
            }
        }

        public IReadOnlyList<Object> AllAssets => assets;
        public IReadOnlyDictionary<int, Object> IndexToAsset => idToAsset;
        public IReadOnlyDictionary<string, Object> PersistentIdToAsset => persistentIdToAsset;

        public void Refresh()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (RebuildGuids())
                    UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            ClearLookups();
            _assetsView = null;
            _baked.Clear();

            var visited = new HashSet<NetworkAssets>();
            var seenObjectIds = new HashSet<ObjectId>();
            var buffer = new List<AssetEntry>();

            Collect(this);

            for (int i = 0; i < buffer.Count; i++)
            {
                var obj = buffer[i].asset;
                ObjectId objectId = GetObjectId(obj);
                if (objectIdToId.ContainsKey(objectId)) continue;

                idToAsset[i] = obj;
                objectIdToId[objectId] = i;

                RegisterTypeNameFallback(obj, i);
                RegisterPersistentId(obj, i, buffer[i].guid);

                _baked.Add(new BakedEntry { id = i, asset = obj, persistentId = buffer[i].guid });
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            return;

            void Collect(NetworkAssets na)
            {
                if (!na || !visited.Add(na)) return;

#if UNITY_EDITOR
                if (!Application.isPlaying && na.RebuildGuids())
                    UnityEditor.EditorUtility.SetDirty(na);
#endif

                for (int i = 0; i < na.entries.Count; i++)
                {
                    var entry = na.entries[i];
                    if (!entry.asset || !seenObjectIds.Add(GetObjectId(entry.asset))) continue;
                    buffer.Add(entry);
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

            if (objectIdToId.ContainsKey(GetObjectId(obj)))
            {
                if (logIfDuplicate)
                    Debug.LogWarning($"Asset already exists in NetworkAssets: {obj.name}");
                return;
            }

            entries.Add(new AssetEntry { asset = obj });
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
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

            var enabledTypes = enabledTypeNames.Select(Type.GetType).Where(t => t != null).ToArray();
            var found = AssetScannerUtility.ScanAssets(folder, enabledTypes, searchAllIfNoFolder);
            var linkedAssets = AssetScannerUtility.CollectLinkedNetworkAssets(this);
            if (linkedAssets.Count > 0)
                found.RemoveAll(scan => linkedAssets.Contains(scan.asset));

            if (found.Count == 0 && folder == null && !searchAllIfNoFolder)
                return;

            var existingSet = new HashSet<Object>(entries.Select(e => e.asset));
            bool changed = false;

            if (linkedAssets.Count > 0)
            {
                int removed = entries.RemoveAll(e => e.asset && linkedAssets.Contains(e.asset));
                if (removed > 0)
                {
                    existingSet = new HashSet<Object>(entries.Select(e => e.asset));
                    changed = true;
                }
            }

            foreach (var scan in found)
            {
                if (existingSet.Add(scan.asset))
                {
                    entries.Add(new AssetEntry { asset = scan.asset, guid = scan.guid });
                    changed = true;
                }
            }

            bool guidsChanged = RebuildGuids();

            if (changed || guidsChanged)
            {
                Refresh();
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
            }

            CleanupNullEntries();
        }

        private bool RebuildGuids()
        {
            bool changed = false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                string guid = null;

                if (entry.asset)
                {
                    string path = UnityEditor.AssetDatabase.GetAssetPath(entry.asset);
                    guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);

                    // For sub-assets, append local file ID to make GUID unique
                    if (UnityEditor.AssetDatabase.IsSubAsset(entry.asset))
                    {
                        if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(entry.asset, out string _, out long localId))
                            guid = $"{guid}_{localId}";
                    }
                }

                if (entry.guid == guid) continue;

                entry.guid = guid;
                entries[i] = entry;
                changed = true;
            }

            if (changed)
                _assetsView = null;

            return changed;
        }

        private void CleanupNullEntries()
        {
            int count = entries.Count;
            entries.RemoveAll(e => e.asset == null);
            if (entries.Count != count)
                Refresh();
        }
#endif

        public bool TryGetAsset(int id, out Object obj) => idToAsset.TryGetValue(id, out obj);

        public bool TryGetId(Object obj, out int id)
        {
            id = GetIndex(obj);
            return id >= 0;
        }

        private static ObjectId GetObjectId(Object obj) => PurrObjectId.Of(obj);
    }

}
