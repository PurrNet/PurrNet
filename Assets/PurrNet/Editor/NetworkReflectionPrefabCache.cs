using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace PurrNet.Editor
{
    static class NetworkReflectionPrefabCache
    {
        internal const string PREFAB_CACHE_FILE = "Library/PurrNet/PrefabsWithNetworkReflection.txt";

        internal static HashSet<string> Read()
        {
            var paths = new HashSet<string>();
            if (!File.Exists(PREFAB_CACHE_FILE))
                return paths;

            foreach (var line in File.ReadAllLines(PREFAB_CACHE_FILE))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    paths.Add(trimmed);
            }

            return paths;
        }

        internal static void Write(HashSet<string> paths)
        {
            var dir = Path.GetDirectoryName(PREFAB_CACHE_FILE);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sorted = new List<string>(paths);
            sorted.Sort();
            File.WriteAllLines(PREFAB_CACHE_FILE, sorted);
        }

        internal static void BatchUpdate(Action<HashSet<string>> update)
        {
            var paths = Read();
            update(paths);
            Write(paths);
        }

        internal static bool PrefabContainsNetworkReflection(string prefabPath)
        {
            try
            {
                var text = File.ReadAllText(prefabPath);
                return text.Contains("NetworkReflection");
            }
            catch
            {
                return false;
            }
        }

        internal static void UpdateForImport(string path, HashSet<string> paths)
        {
            if (PrefabContainsNetworkReflection(path))
                paths.Add(path);
            else
                paths.Remove(path);
        }

        internal static void UpdateForDelete(string path, HashSet<string> paths)
        {
            paths.Remove(path);
        }

        internal static void UpdateForMove(string fromPath, string toPath, HashSet<string> paths)
        {
            paths.Remove(fromPath);
            if (toPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) &&
                PrefabContainsNetworkReflection(toPath))
                paths.Add(toPath);
        }
    }
}
