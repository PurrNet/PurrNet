using System;
using System.IO;
using UnityEngine;

namespace PurrNet.Utils
{
    public static class ClonesContext
    {
#if UNITY_EDITOR
        private static bool? _cachedIsClone;
#endif

        public static bool isClone
        {
            get
            {
#if !UNITY_EDITOR
                return false;
#else
                if (_cachedIsClone.HasValue)
                    return _cachedIsClone.Value;

                var assetsFolder = new DirectoryInfo("Assets");
                var isClone = assetsFolder.Attributes.HasFlag(FileAttributes.ReparsePoint);

                _cachedIsClone = isClone;
                return isClone;
#endif
            }
        }

#if UNITY_EDITOR
        public static string GetOriginalProjectPath()
        {
            if (!isClone)
                return null;
            try
            {
#if NET6_0_OR_GREATER
                var target = new DirectoryInfo("Assets").ResolveLinkTarget(true);
                if (target != null)
                    return Path.GetDirectoryName(target.FullName);
#else
                var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
                var folderName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));
                var cloneIdx = folderName.IndexOf("_clone", StringComparison.OrdinalIgnoreCase);
                if (cloneIdx > 0)
                {
                    var parentName = folderName.Substring(0, cloneIdx);
                    return Path.Combine(Path.GetDirectoryName(projectRoot) ?? "", parentName);
                }
#endif
            }
            catch { }
            return null;
        }
#endif
    }
}