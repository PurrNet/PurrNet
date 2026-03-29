using System;
using System.Collections.Generic;
using UnityEngine;
using PurrNet.Logging;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using PurrNet.Utils;
using UnityEditor;
#endif

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkPrefabs", menuName = "PurrNet/Network Prefabs/Prefabs", order = -201)]
    public class NetworkPrefabs : PrefabProviderScriptable
    {
        public bool autoGenerate = true;
        public bool networkOnly = true;
        public bool poolByDefault;
        public Object folder;
        [Tooltip("When no folder is set, search all of Assets/ instead of doing nothing.")]
        public bool searchAllIfNoFolder = true;
        [Tooltip("Will also get prefabs from these nested prefabs. This is to allow further organization")]
        public List<NetworkPrefabs> linkedNetworkPrefabs = new();
        public List<UserPrefabData> prefabs = new List<UserPrefabData>();

        [Serializable]
        public struct UserPrefabData
        {
            public string guid;
            public GameObject prefab;
            public bool pooled;
            public int warmupCount;
        }

        public override IEnumerable<PrefabData> allPrefabs => prefabLookup.Values;

        private readonly Dictionary<int, PrefabData> prefabLookup = new();
        
        public override bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return this.prefabLookup.TryGetValue(prefabId, out prefabData);
        }

        public override bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            foreach (var data in this.allPrefabs)
            {
                if (data.prefab == prefab)
                {
                    prefabData = data;
                    return true;
                }
            }

            // Fallback: match by name for addressable bundle-loaded prefabs
            // where the reference is a different instance than the registered one
            var prefabName = prefab ? prefab.name : null;
            if (prefabName != null)
            {
                PrefabData? candidate = null;
                bool ambiguous = false;

                foreach (var data in this.allPrefabs)
                {
                    if (data.prefab && data.prefab.name == prefabName)
                    {
                        if (candidate.HasValue)
                        {
                            ambiguous = true;
                            break;
                        }
                        candidate = data;
                    }
                }

                if (candidate.HasValue && !ambiguous)
                {
                    prefabData = candidate.Value;
                    return true;
                }
            }

            prefabData = default;
            return false;
        }

        public override void Refresh()
        {
            RegeneratePrefabLookup();
        }

#if UNITY_EDITOR
        private bool _generating;

        private void OnValidate()
        {
            if (autoGenerate)
            {
                // schedule for next editor update
                EditorApplication.delayCall += Generate;
            }
        }
#endif

        private void OnEnable()
        {
            RegeneratePrefabLookup();
        }

        private void RegeneratePrefabLookup()
        {
            prefabLookup.Clear();

            var visited = new HashSet<NetworkPrefabs>();
            var seenGuid = new HashSet<string>();
            var seenGO = new HashSet<GameObject>();
            var buffer = new List<UserPrefabData>();

            void Collect(NetworkPrefabs np)
            {
                if (!np || !visited.Add(np)) return;

                var list = np.prefabs;
                for (int i = 0; i < list.Count; i++)
                {
                    var ud = list[i];
                    if (!ud.prefab) continue;

                    var hasGuid = !string.IsNullOrEmpty(ud.guid);
                    if (hasGuid)
                    {
                        if (!seenGuid.Add(ud.guid)) continue;
                    }
                    else
                    {
                        if (!seenGO.Add(ud.prefab)) continue;
                    }

                    buffer.Add(ud);
                }

                var links = np.linkedNetworkPrefabs;
                if (links == null) return;
                for (int i = 0; i < links.Count; i++)
                {
                    var link = links[i];
                    if (link) Collect(link);
                }
            }

            Collect(this);

            for (int i = 0; i < buffer.Count; i++)
            {
                var ud = buffer[i];
                prefabLookup.Add(i, new PrefabData
                {
                    prefabId = i,
                    prefab = ud.prefab,
                    pooled = ud.pooled,
                    warmupCount = ud.warmupCount
                });
            }
        }

        /// <summary>
        /// Editor only method to generate network prefabs from a specified folder.
        /// </summary>
        public void Generate()
        {
        #if UNITY_EDITOR
            if (ApplicationContext.isClone) return;
            if (!this) return;
            if (_generating) return;

            _generating = true;
            try
            {
                EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Scanning...", 0.1f);

                string resolvedPath = AssetScannerUtility.ResolveFolderPath(folder, searchAllIfNoFolder);

                if (string.IsNullOrEmpty(resolvedPath))
                {
                    if (autoGenerate && prefabs.Count > 0)
                    {
                        prefabs.Clear();
                        EditorUtility.SetDirty(this);
                        AssetDatabase.SaveAssets();
                    }
                    return;
                }

                var found = AssetScannerUtility.ScanPrefabs(folder, networkOnly, searchAllIfNoFolder);

                EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Syncing...", 0.8f);

                // Update GUIDs on existing entries
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (!prefabs[i].prefab) continue;
                    var path = AssetDatabase.GetAssetPath(prefabs[i].prefab);
                    var g = AssetDatabase.AssetPathToGUID(path);
                    if (prefabs[i].guid != g)
                    {
                        var p = prefabs[i];
                        p.guid = g;
                        prefabs[i] = p;
                        EditorUtility.SetDirty(this);
                    }
                }

                var (added, removed) = AssetScannerUtility.SyncEntries(
                    prefabs,
                    found,
                    e => e.guid,
                    e => e.prefab,
                    scan => new UserPrefabData
                    {
                        prefab = (GameObject)scan.asset,
                        pooled = poolByDefault,
                        warmupCount = 5,
                        guid = scan.guid
                    },
                    e => e.prefab);

                if (removed > 0 || added > 0)
                {
                    EditorUtility.SetDirty(this);
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"An error occurred during prefab generation: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _generating = false;
            }
        #endif
        }

    }
}
