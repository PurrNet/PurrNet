using UnityEditor;

namespace PurrNet.Editor
{
    public class NetworkReflectionCachePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var hasPrefabChanges = false;
            foreach (var p in importedAssets)
                if (p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) { hasPrefabChanges = true; break; }
            if (!hasPrefabChanges)
                foreach (var p in deletedAssets)
                    if (p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) { hasPrefabChanges = true; break; }
            if (!hasPrefabChanges)
                for (var i = 0; i < movedFromAssetPaths.Length; i++)
                    if (movedFromAssetPaths[i].EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) { hasPrefabChanges = true; break; }

            if (!hasPrefabChanges)
                return;

            NetworkReflectionPrefabCache.BatchUpdate(paths =>
            {
                foreach (var path in importedAssets)
                {
                    if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    NetworkReflectionPrefabCache.UpdateForImport(path, paths);
                }
                foreach (var path in deletedAssets)
                {
                    if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    NetworkReflectionPrefabCache.UpdateForDelete(path, paths);
                }
                for (var i = 0; i < movedAssets.Length; i++)
                {
                    if (!movedFromAssetPaths[i].EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    NetworkReflectionPrefabCache.UpdateForMove(movedFromAssetPaths[i], movedAssets[i], paths);
                }
            });

            EditorApplication.delayCall += NetworkReflectionInspector.RefreshReflectionTargetsCacheFull;
        }
    }
}
