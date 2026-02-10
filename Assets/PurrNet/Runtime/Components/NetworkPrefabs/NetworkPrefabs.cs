using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
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

            buffer.Sort((a, b) =>
            {
                var ga = string.IsNullOrEmpty(a.guid) ? null : a.guid;
                var gb = string.IsNullOrEmpty(b.guid) ? null : b.guid;
                if (ga != null && gb != null) return string.CompareOrdinal(ga, gb);
                if (ga != null) return -1;
                if (gb != null) return 1;
                var na = a.prefab ? a.prefab.name : string.Empty;
                var nb = b.prefab ? b.prefab.name : string.Empty;
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
                return a.prefab.GetInstanceID().CompareTo(b.prefab.GetInstanceID());
            });

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
                EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Checking existing...", 0f);

                if (folder == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(folder)))
                {
                    EditorUtility.DisplayProgressBar("Getting Network Prefabs", "No folder found...", 0f);
                    if (autoGenerate && prefabs.Count > 0)
                    {
                        prefabs.Clear();
                        EditorUtility.SetDirty(this);
                        AssetDatabase.SaveAssets();
                    }
                    EditorUtility.ClearProgressBar();
                    _generating = false;
                    return;
                }

                string folderPath = AssetDatabase.GetAssetPath(folder);
                if (string.IsNullOrEmpty(folderPath))
                {
                    if (autoGenerate && prefabs.Count > 0)
                    {
                        prefabs.Clear();
                        EditorUtility.SetDirty(this);
                        AssetDatabase.SaveAssets();
                    }
                    EditorUtility.ClearProgressBar();
                    _generating = false;
                    PurrLogger.LogError("Exiting Generate method early due to empty folder path.");
                    return;
                }

                EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Finding paths...", 0.1f);

                var foundPrefabs = new List<GameObject>();
                var identities = new List<NetworkIdentity>();
                string[] guids = AssetDatabase.FindAssets("t:prefab", new[] { folderPath });
                for (int i = 0; i < guids.Length; i++)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (!prefab) continue;

                    EditorUtility.DisplayProgressBar("Getting Network Prefabs", $"Looking at {prefab.name}", 0.1f + 0.7f * ((i + 1f) / guids.Length));

                    if (!networkOnly)
                    {
                        foundPrefabs.Add(prefab);
                        continue;
                    }

                    identities.Clear();
                    prefab.GetComponentsInChildren(true, identities);
                    if (identities.Count > 0) foundPrefabs.Add(prefab);
                }

                EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Sorting...", 0.9f);

                foundPrefabs.Sort((a, b) =>
                {
                    string pathA = AssetDatabase.GetAssetPath(a);
                    string pathB = AssetDatabase.GetAssetPath(b);
                    var guidA = AssetDatabase.GUIDFromAssetPath(pathA);
                    var guidB = AssetDatabase.GUIDFromAssetPath(pathB);
                    return string.Compare(guidA.ToString(), guidB.ToString(), StringComparison.Ordinal);
                });

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

                EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Removing invalid prefabs...", 0.95f);

                int removed = prefabs.RemoveAll(pd => !pd.prefab || !File.Exists(AssetDatabase.GetAssetPath(pd.prefab)));

                var foundGuids = new HashSet<string>();
                for (int i = 0; i < foundPrefabs.Count; i++)
                {
                    var p = AssetDatabase.GetAssetPath(foundPrefabs[i]);
                    if (!string.IsNullOrEmpty(p)) foundGuids.Add(AssetDatabase.AssetPathToGUID(p));
                }

                var existingGuids = new HashSet<string>();
                for (int i = 0; i < prefabs.Count; i++)
                {
                    var p = prefabs[i].prefab ? AssetDatabase.GetAssetPath(prefabs[i].prefab) : null;
                    if (!string.IsNullOrEmpty(p)) existingGuids.Add(AssetDatabase.AssetPathToGUID(p));
                }

                for (int i = 0; i < prefabs.Count; i++)
                {
                    var path = prefabs[i].prefab ? AssetDatabase.GetAssetPath(prefabs[i].prefab) : null;
                    var g = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(g) || !foundGuids.Contains(g))
                    {
                        prefabs.RemoveAt(i);
                        removed++;
                        i--;
                    }
                }

                int added = 0;
                for (int i = 0; i < foundPrefabs.Count; i++)
                {
                    var path = AssetDatabase.GetAssetPath(foundPrefabs[i]);
                    var g = AssetDatabase.AssetPathToGUID(path);
                    if (!existingGuids.Contains(g))
                    {
                        prefabs.Add(new UserPrefabData { prefab = foundPrefabs[i], pooled = poolByDefault, warmupCount = 5, guid = g });
                        added++;
                    }
                }

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
