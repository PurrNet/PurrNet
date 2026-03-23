#if UNITY_EDITOR
using System.Linq;
using UnityEditor;

namespace PurrNet
{
    /// <summary>
    /// Unified asset postprocessor that monitors folder changes and triggers
    /// regeneration for all auto-generating asset registries (NetworkPrefabs,
    /// NetworkAssets, and AddressableNetworkPrefabs).
    /// Replaces the individual NetworkAssetsPostprocessor.
    /// </summary>
    public class UnifiedAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            ProcessNetworkPrefabs(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
            ProcessNetworkAssets(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);

#if ADDRESSABLES_PURRNET_SUPPORT
            ProcessAddressableNetworkPrefabs(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
#endif
        }

        private static bool HasRelevantChange(string folderPath, string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            return importedAssets.Any(p => p.StartsWith(folderPath)) ||
                   deletedAssets.Any(p => p.StartsWith(folderPath)) ||
                   movedAssets.Any(p => p.StartsWith(folderPath)) ||
                   movedFromAssetPaths.Any(p => p.StartsWith(folderPath));
        }

        private static void ProcessNetworkPrefabs(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var all = AssetDatabase.FindAssets("t:NetworkPrefabs")
                .Select(guid => AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(n => n && n.autoGenerate && n.folder)
                .ToArray();

            foreach (var networkPrefabs in all)
            {
                string folderPath = AssetDatabase.GetAssetPath(networkPrefabs.folder);
                if (HasRelevantChange(folderPath, importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                    networkPrefabs.Generate();
            }
        }

        private static void ProcessNetworkAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var all = AssetDatabase.FindAssets("t:NetworkAssets")
                .Select(guid => AssetDatabase.LoadAssetAtPath<NetworkAssets>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(n => n && n.autoGenerate && n.folder)
                .ToArray();

            foreach (var netAsset in all)
            {
                string folderPath = AssetDatabase.GetAssetPath(netAsset.folder);
                if (HasRelevantChange(folderPath, importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                    netAsset.GenerateAssets();
            }
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        private static void ProcessAddressableNetworkPrefabs(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var all = AssetDatabase.FindAssets("t:AddressableNetworkPrefabs")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AddressableNetworkPrefabs>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(n => n && n.autoGenerate && n.folder)
                .ToArray();

            foreach (var addressable in all)
            {
                string folderPath = AssetDatabase.GetAssetPath(addressable.folder);
                if (HasRelevantChange(folderPath, importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                    AddressableNetworkPrefabsEditor.Generate(addressable);
            }
        }
#endif
    }
}
#endif
