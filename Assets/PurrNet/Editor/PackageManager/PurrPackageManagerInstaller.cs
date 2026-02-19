using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerInstaller
    {
        public static bool IsInstalled(PackageInfo package)
        {
            var path = Path.Combine("Packages", package.GetUpmPackageName());
            return Directory.Exists(path);
        }

        public static string GetInstalledVersion(PackageInfo package)
        {
            var packageJsonPath = Path.Combine("Packages", package.GetUpmPackageName(), "package.json");
            if (!File.Exists(packageJsonPath))
                return null;

            try
            {
                var json = JObject.Parse(File.ReadAllText(packageJsonPath));
                return json["version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static async Task<Result<bool>> Install(string apiKey, PackageInfo package, VersionInfo version)
        {
            try
            {
                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Getting download URL...", 0.1f);

                var downloadResult = await PurrPackageManagerAPI.GetDownloadUrl(apiKey, package.Id, version.Id);
                if (!downloadResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return new Result<bool>(downloadResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Downloading {package.DisplayName}...", 0.3f);

                var filename = downloadResult.Value.Filename ?? (package.GetUpmPackageName() + ".unitypackage");
                var tempPath = Path.Combine(Path.GetTempPath(), filename);

                var fileResult = await PurrPackageManagerAPI.DownloadFile(downloadResult.Value.Url, tempPath);
                if (!fileResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return new Result<bool>(fileResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Installing package...", 0.7f);

                var upmFolder = Path.Combine("Packages", package.GetUpmPackageName());
                if (Directory.Exists(upmFolder))
                    Directory.Delete(upmFolder, true);

                AssetDatabase.ImportPackage(tempPath, false);

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Cleaning up...", 0.9f);

                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                PurrPackageManagerCache.Invalidate();
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();

                return new Result<bool>(true);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                return new Result<bool>(e.Message);
            }
        }

        public static bool Remove(PackageInfo package)
        {
            var upmFolder = Path.Combine("Packages", package.GetUpmPackageName());
            if (!Directory.Exists(upmFolder))
                return false;

            if (!EditorUtility.DisplayDialog("Remove Package",
                $"Are you sure you want to remove {package.DisplayName}?\n\nThis will delete the folder:\n{upmFolder}",
                "Remove", "Cancel"))
                return false;

            try
            {
                Directory.Delete(upmFolder, true);
                PurrPackageManagerCache.Invalidate();
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to remove package: {e.Message}");
                return false;
            }
        }
    }
}
