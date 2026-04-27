#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    /// <summary>
    /// Shared editor utility for scanning folders for assets.
    /// Used by NetworkPrefabs, NetworkAssets, and AddressableNetworkPrefabs
    /// to provide consistent scanning, sorting, and sync behavior.
    /// Lives in the Runtime assembly so all types can reference it,
    /// but only compiles in the editor.
    /// </summary>
    public static class AssetScannerUtility
    {
        public struct ScanResult
        {
            public Object asset;
            public string guid;
            public string assetPath;
        }

        /// <summary>
        /// Resolves a folder Object to its AssetDatabase path.
        /// When folder is null and fallbackToAssetsRoot is true, returns "Assets".
        /// Returns null if the folder cannot be resolved and fallback is disabled.
        /// </summary>
        public static string ResolveFolderPath(Object folder, bool fallbackToAssetsRoot = true)
        {
            if (folder != null)
            {
                string path = AssetDatabase.GetAssetPath(folder);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }

            return fallbackToAssetsRoot ? "Assets" : null;
        }

        /// <summary>
        /// Scans a folder for prefabs, optionally filtering to those with NetworkIdentity.
        /// Results are sorted deterministically by GUID.
        /// </summary>
        public static List<ScanResult> ScanPrefabs(Object folder, bool networkOnly, bool fallbackToAssetsRoot)
        {
            var results = new List<ScanResult>();
            string folderPath = ResolveFolderPath(folder, fallbackToAssetsRoot);

            if (string.IsNullOrEmpty(folderPath))
                return results;

            string[] guids = AssetDatabase.FindAssets("t:prefab", new[] { folderPath });
            var identities = new List<NetworkIdentity>();

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (!prefab) continue;

                if (networkOnly)
                {
                    identities.Clear();
                    prefab.GetComponentsInChildren(true, identities);
                    if (identities.Count == 0) continue;
                }

                results.Add(new ScanResult
                {
                    asset = prefab,
                    guid = guids[i],
                    assetPath = assetPath
                });
            }

            results.Sort(CompareByGuid);
            return results;
        }

        /// <summary>
        /// Scans a folder for general assets, filtering by enabled types.
        /// Includes sub-assets (e.g. sprites inside a texture).
        /// Results are sorted deterministically by GUID.
        /// </summary>
        public static List<ScanResult> ScanAssets(Object folder, Type[] enabledTypes, bool fallbackToAssetsRoot)
        {
            var results = new List<ScanResult>();
            string folderPath = ResolveFolderPath(folder, fallbackToAssetsRoot);

            if (string.IsNullOrEmpty(folderPath))
                return results;

            string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
            var seen = new HashSet<Object>();

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (assetPath.EndsWith(".unity")) continue;

                var allAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var obj in allAtPath)
                {
                    if (!obj) continue;

                    var ns = obj.GetType().Namespace;
                    if (ns != null && ns.Contains("UnityEditor")) continue;

                    if (!seen.Add(obj)) continue;

                    bool matchesType = false;
                    for (int t = 0; t < enabledTypes.Length; t++)
                    {
                        if (enabledTypes[t].IsAssignableFrom(obj.GetType()))
                        {
                            matchesType = true;
                            break;
                        }
                    }

                    if (!matchesType) continue;

                    string objGuid = guids[i];
                    // For sub-assets, append the local file ID to make the GUID unique
                    if (AssetDatabase.IsSubAsset(obj))
                    {
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string _, out long localId))
                            objGuid = $"{guids[i]}_{localId}";
                    }

                    results.Add(new ScanResult
                    {
                        asset = obj,
                        guid = objGuid,
                        assetPath = assetPath
                    });
                }
            }

            results.Sort(CompareByGuid);
            return results;
        }

        /// <summary>
        /// Deterministic comparator: by GUID (ordinal), then by name, then by instance ID.
        /// </summary>
        public static int CompareByGuid(ScanResult a, ScanResult b)
        {
            int c = string.CompareOrdinal(a.guid, b.guid);
            if (c != 0) return c;

            string na = a.asset ? a.asset.name : string.Empty;
            string nb = b.asset ? b.asset.name : string.Empty;
            c = string.CompareOrdinal(na, nb);
            if (c != 0) return c;

            return string.CompareOrdinal(a.assetPath, b.assetPath);
        }

        /// <summary>
        /// Synchronizes an existing entry list with scan results.
        /// Preserves user settings (e.g. pooling) on existing entries.
        /// Returns the number of entries added and removed.
        /// </summary>
        public static (int added, int removed) SyncEntries<T>(
            List<T> existing,
            List<ScanResult> found,
            Func<T, string> getGuid,
            Func<T, Object> getAsset,
            Func<ScanResult, T> createNew,
            Func<T, bool> isValid)
        {
            int removed = existing.RemoveAll(e => !isValid(e));

            var existingGuids = new HashSet<string>();
            for (int i = 0; i < existing.Count; i++)
            {
                string g = getGuid(existing[i]);
                if (!string.IsNullOrEmpty(g))
                    existingGuids.Add(g);
            }

            var foundGuids = new HashSet<string>();
            for (int i = 0; i < found.Count; i++)
                foundGuids.Add(found[i].guid);

            // Remove entries not in found set
            for (int i = existing.Count - 1; i >= 0; i--)
            {
                string g = getGuid(existing[i]);
                if (string.IsNullOrEmpty(g) || !foundGuids.Contains(g))
                {
                    existing.RemoveAt(i);
                    removed++;
                }
            }

            // Add new entries
            int added = 0;
            for (int i = 0; i < found.Count; i++)
            {
                if (!existingGuids.Contains(found[i].guid))
                {
                    existing.Add(createNew(found[i]));
                    added++;
                }
            }

            return (added, removed);
        }
    }
}
#endif
