using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
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
                    return Result<bool>.Fail(downloadResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Downloading {package.DisplayName}...", 0.3f);

                var filename = downloadResult.Value.Filename ?? (package.GetUpmPackageName() + ".unitypackage");
                var tempPath = Path.Combine(Path.GetTempPath(), filename);

                var fileResult = await PurrPackageManagerAPI.DownloadFile(downloadResult.Value.Url, tempPath);
                if (!fileResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail(fileResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Installing package...", 0.7f);

                var upmFolder = Path.Combine("Packages", package.GetUpmPackageName());
                string backupFolder = null;

                // Move existing package to temp instead of deleting (native DLLs can be locked)
                if (Directory.Exists(upmFolder))
                {
                    backupFolder = Path.Combine("Temp", package.GetUpmPackageName() + "_backup_" + DateTime.Now.Ticks);
                    try
                    {
                        Directory.Move(upmFolder, backupFolder);
                    }
                    catch (Exception moveEx)
                    {
                        EditorUtility.ClearProgressBar();
                        return Result<bool>.Fail($"Failed to move existing package out of the way: {moveEx.Message}");
                    }
                }

                try
                {
                    ExtractUnityPackage(tempPath, upmFolder);
                }
                catch (Exception extractEx)
                {
                    // Restore backup if extraction failed
                    if (backupFolder != null && Directory.Exists(backupFolder))
                    {
                        if (Directory.Exists(upmFolder))
                            Directory.Delete(upmFolder, true);
                        Directory.Move(backupFolder, upmFolder);
                    }
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail($"Failed to extract package: {extractEx.Message}");
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Cleaning up...", 0.9f);

                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                // Best-effort cleanup of the backup; locked native DLLs will be left for the OS
                if (backupFolder != null && Directory.Exists(backupFolder))
                {
                    try { Directory.Delete(backupFolder, true); }
                    catch { /* locked files will be cleaned up on next restart */ }
                }

                PurrPackageManagerCache.Invalidate();
                UnityEditor.PackageManager.Client.Resolve();
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();

                return Result<bool>.Ok(true);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                return Result<bool>.Fail(e.Message);
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

        private static void ExtractUnityPackage(string packagePath, string targetDir)
        {
            // .unitypackage = gzipped tar
            // Each asset is a folder named by GUID containing:
            //   pathname  - the original asset path
            //   asset     - the file content
            //   asset.meta - the .meta file content

            var entries = new Dictionary<string, PackageEntry>();
            string longName = null;

            using (var fileStream = File.OpenRead(packagePath))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (var memStream = new MemoryStream())
            {
                gzipStream.CopyTo(memStream);
                var tarBytes = memStream.ToArray();

                int pos = 0;
                while (pos + 512 <= tarBytes.Length)
                {
                    // Check for zero block (end of archive)
                    bool allZero = true;
                    for (int i = 0; i < 512; i++)
                    {
                        if (tarBytes[pos + i] != 0) { allZero = false; break; }
                    }
                    if (allZero) break;

                    // Parse tar header
                    string name = Encoding.ASCII.GetString(tarBytes, pos, 100).TrimEnd('\0');
                    string sizeStr = Encoding.ASCII.GetString(tarBytes, pos + 124, 12).Trim('\0', ' ');
                    long size = sizeStr.Length > 0 ? Convert.ToInt64(sizeStr, 8) : 0;
                    char typeFlag = (char)tarBytes[pos + 156];

                    // ustar prefix field (offset 345, 155 bytes)
                    string prefix = Encoding.ASCII.GetString(tarBytes, pos + 345, 155).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(prefix))
                        name = prefix + "/" + name;

                    pos += 512;

                    byte[] content = null;
                    if (size > 0)
                    {
                        content = new byte[size];
                        Array.Copy(tarBytes, pos, content, 0, (int)size);
                        pos += (int)((size + 511) / 512) * 512;
                    }

                    // Handle GNU long name extension
                    if (typeFlag == 'L')
                    {
                        longName = content != null ? Encoding.ASCII.GetString(content).TrimEnd('\0') : null;
                        continue;
                    }

                    // Use long name if set by previous ././@LongLink entry
                    if (longName != null)
                    {
                        name = longName;
                        longName = null;
                    }

                    // Skip pax extended headers
                    if (typeFlag == 'x' || typeFlag == 'g')
                        continue;

                    // Skip directories
                    if (typeFlag == '5')
                        continue;

                    // Strip leading "./"
                    if (name.StartsWith("./"))
                        name = name.Substring(2);

                    // Strip trailing "/"
                    name = name.TrimEnd('/');

                    // Entries are "{guid}/{type}" where type is pathname, asset, or asset.meta
                    var slashIdx = name.IndexOf('/');
                    if (slashIdx < 0)
                        continue;

                    string guid = name.Substring(0, slashIdx);
                    string entryName = name.Substring(slashIdx + 1);

                    if (!entries.TryGetValue(guid, out var entry))
                    {
                        entry = new PackageEntry();
                        entries[guid] = entry;
                    }

                    if (entryName == "pathname" && content != null)
                        entry.Pathname = Encoding.UTF8.GetString(content).Trim();
                    else if (entryName == "asset")
                        entry.AssetContent = content;
                    else if (entryName == "asset.meta")
                        entry.MetaContent = content;
                }
            }

            // Find the root prefix by locating package.json
            string rootPrefix = null;
            foreach (var entry in entries.Values)
            {
                if (entry.Pathname == null)
                    continue;

                var filename = entry.Pathname;
                // Normalize slashes
                filename = filename.Replace('\\', '/');
                entry.Pathname = filename;

                if (filename.EndsWith("/package.json") || filename == "package.json")
                {
                    rootPrefix = filename.Substring(0, filename.Length - "package.json".Length);
                    break;
                }
            }

            // Fallback: find the shortest common directory prefix
            if (rootPrefix == null)
            {
                foreach (var entry in entries.Values)
                {
                    if (entry.Pathname == null)
                        continue;
                    var lastSlash = entry.Pathname.LastIndexOf('/');
                    var dir = lastSlash >= 0 ? entry.Pathname.Substring(0, lastSlash + 1) : "";
                    if (rootPrefix == null || dir.Length < rootPrefix.Length)
                        rootPrefix = dir;
                }
            }

            rootPrefix ??= "";

            // Write files to target directory
            Directory.CreateDirectory(targetDir);
            int fileCount = 0;

            foreach (var entry in entries.Values)
            {
                if (entry.Pathname == null)
                    continue;

                // Strip root prefix
                string relativePath = entry.Pathname;
                if (rootPrefix.Length > 0 && relativePath.StartsWith(rootPrefix))
                    relativePath = relativePath.Substring(rootPrefix.Length);

                if (string.IsNullOrEmpty(relativePath))
                    continue;

                // Write asset content
                if (entry.AssetContent != null)
                {
                    var fullPath = Path.Combine(targetDir, relativePath);
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(fullPath, entry.AssetContent);
                    fileCount++;
                }

                // Write .meta file
                if (entry.MetaContent != null)
                {
                    var metaPath = Path.Combine(targetDir, relativePath + ".meta");
                    var dir = Path.GetDirectoryName(metaPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(metaPath, entry.MetaContent);
                }
            }

            if (fileCount == 0)
                Debug.LogWarning("[PurrNet] Package extraction produced no files.");
        }

        private class PackageEntry
        {
            public string Pathname;
            public byte[] AssetContent;
            public byte[] MetaContent;
        }
    }
}
