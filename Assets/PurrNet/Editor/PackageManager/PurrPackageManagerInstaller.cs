using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerInstaller
    {
        private const string LegacyPackagesDir = "PurrPackages";
        private const string ManifestPath = "Packages/manifest.json";
        private const string LockFilePath = "Packages/packages-lock.json";

        public static bool IsInstalled(PackageInfo package)
        {
            return FindInstalledEntry(package) != null;
        }

        public static string GetInstalledVersion(PackageInfo package)
        {
            var match = FindInstalledEntry(package);
            if (match == null)
                return null;

            var value = match.Value.value;
            var key = match.Value.key;

            // Git URL entries — try to resolve the actual version
            if (IsGitUrl(value))
            {
                var resolved = GetResolvedPackageVersion(key);
                return resolved ?? "git";
            }

            // Parse version from the entry value
            // Format: "embedded:{name}-{version}" (current), or legacy "file:../PurrPackages/{name}-{version}.tgz" / "file:../PurrPackages/{name}-{version}"
            string nameAndVersion;
            if (value.StartsWith("embedded:"))
                nameAndVersion = value.Substring("embedded:".Length);
            else if (value.EndsWith(".tgz"))
                nameAndVersion = Path.GetFileNameWithoutExtension(value);
            else
                nameAndVersion = Path.GetFileName(value);

            if (nameAndVersion.StartsWith(key + "-"))
                return nameAndVersion[(key.Length + 1)..];
            return null;
        }

        /// <summary>
        /// Returns true if the package is currently installed via a git URL
        /// (either in manifest.json or resolved by Unity).
        /// </summary>
        public static bool IsInstalledViaGit(PackageInfo package)
        {
            var match = FindInstalledEntry(package);
            if (match == null)
                return false;
            return IsGitUrl(match.Value.value);
        }

        /// <summary>
        /// Reads the resolved commit hash from packages-lock.json for a git-installed package.
        /// Uses the actual manifest key (which may differ from GetUpmPackageName).
        /// </summary>
        public static string GetInstalledCommitHash(PackageInfo package)
        {
            if (!File.Exists(LockFilePath))
                return null;

            // Use the actual installed key, which may differ from apiName
            // when the package was matched by git URL scanning.
            var match = FindInstalledEntry(package);
            var lookupName = match?.key ?? package.GetUpmPackageName();

            try
            {
                var lockFile = JObject.Parse(File.ReadAllText(LockFilePath));
                var deps = lockFile["dependencies"] as JObject;
                var entry = deps?[lookupName] as JObject;
                if (entry == null)
                    return null;

                var source = entry["source"]?.ToString();
                if (source != "git")
                    return null;

                return entry["hash"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Finds the installed entry for a package. Checks embedded packages in Packages/{name}/,
        /// manifest entries (git URLs), and legacy PurrPackages/ file: references.
        /// </summary>
        private static (string key, string value)? FindInstalledEntry(PackageInfo package)
        {
            var apiName = package.GetUpmPackageName();

            // Check for embedded package first (Packages/{name}/ takes priority in Unity)
            if (HasEmbeddedPackage(apiName))
            {
                var pkgJsonPath = Path.Combine("Packages", apiName, "package.json");
                try
                {
                    var json = JObject.Parse(File.ReadAllText(pkgJsonPath));
                    var ver = json["version"]?.ToString() ?? "0.0.0";
                    return (apiName, $"embedded:{apiName}-{ver}");
                }
                catch
                {
                    return (apiName, $"embedded:{apiName}-0.0.0");
                }
            }

            if (!File.Exists(ManifestPath))
                return null;

            JObject deps;
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                deps = manifest["dependencies"] as JObject;
                if (deps == null)
                    return null;
            }
            catch
            {
                return null;
            }

            // Try direct lookup with API-provided name
            var directEntry = deps[apiName]?.ToString();
            if (directEntry != null && directEntry.Contains(LegacyPackagesDir))
                return (apiName, directEntry);

            // Check for git URL entries (external packages)
            if (directEntry != null && IsGitUrl(directEntry))
                return (apiName, directEntry);

            // API name may differ from the real name in package.json.
            // Scan legacy entries pointing to PurrPackages/ — this is safe because only our
            // installer puts tarballs there, unlike Packages/ which has unrelated packages.
            if (package.Versions != null && package.Versions.Length > 0)
            {
                foreach (var prop in deps.Properties())
                {
                    var val = prop.Value?.ToString();
                    if (!val.Contains(LegacyPackagesDir))
                        continue;

                    // Legacy: val is "file:../PurrPackages/{key}-{version}.tgz" or "file:../PurrPackages/{key}-{version}"
                    var filename = val.EndsWith(".tgz") ? Path.GetFileNameWithoutExtension(val) : Path.GetFileName(val);
                    if (!filename.StartsWith(prop.Name + "-"))
                        continue;

                    var fileVersion = filename[(prop.Name.Length + 1)..];
                    foreach (var v in package.Versions)
                    {
                        if (v.Version == fileVersion)
                            return (prop.Name, val);
                    }
                }
            }

            // Scan manifest for git URLs matching the package's known git URLs.
            // This handles cases where the UPM name differs or the user installed
            // via Unity's Package Manager using the same repo URL.
            if (!string.IsNullOrEmpty(package.GitInstallUrlRelease) || !string.IsNullOrEmpty(package.GitInstallUrlDev))
            {
                var knownBaseUrls = new HashSet<string>();
                if (!string.IsNullOrEmpty(package.GitInstallUrlRelease))
                    knownBaseUrls.Add(GetBaseGitUrl(package.GitInstallUrlRelease));
                if (!string.IsNullOrEmpty(package.GitInstallUrlDev))
                    knownBaseUrls.Add(GetBaseGitUrl(package.GitInstallUrlDev));

                foreach (var prop in deps.Properties())
                {
                    var val = prop.Value?.ToString();
                    if (!IsGitUrl(val))
                        continue;

                    if (knownBaseUrls.Contains(GetBaseGitUrl(val)))
                        return (prop.Name, val);
                }
            }

            return null;
        }

        private static bool HasEmbeddedPackage(string upmName)
        {
            var path = Path.Combine("Packages", upmName);
            if (!Directory.Exists(path))
                return false;

            // Unity also creates this folder for tgz/file: installs.
            // Only consider it embedded if there's no file: reference in the manifest.
            if (!File.Exists(ManifestPath))
                return true;

            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                var deps = manifest["dependencies"] as JObject;
                var entry = deps?[upmName]?.ToString();
                if (entry != null && (entry.StartsWith("file:") || IsGitUrl(entry)))
                    return false;
            }
            catch
            {
                // ignored
            }

            return true;
        }

        /// <summary>
        /// Removes an embedded package folder at Packages/{upmName}/ if it exists.
        /// </summary>
        private static void RemoveEmbeddedPackage(string upmName)
        {
            SafeRemoveDirectory(Path.Combine("Packages", upmName));
        }

        /// <summary>
        /// Safely removes a directory by moving it to Temp/ first (handles locked native DLLs).
        /// Also handles dangling junctions/symlinks that Unity may leave behind.
        /// </summary>
        private static void SafeRemoveDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    var tempDest = Path.Combine("Temp", "PurrNet_cleanup_" + DateTime.Now.Ticks);
                    Directory.Move(path, tempDest);
                    return;
                }
                catch
                {
                    try { Directory.Delete(path, true); }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[PurrNet] Could not remove directory at {path}: {e.Message}");
                    }
                }
            }

            // Handle dangling junctions/symlinks that Directory.Exists doesn't detect.
            // Unity creates junctions in Packages/ for file: manifest entries — if the
            // target in PurrPackages/ was already removed, the junction is dangling.
            try { Directory.Delete(path); }
            catch
            {
                // Path doesn't exist at all — nothing to do
            }
        }

        public static async Task<Result<bool>> Install(string apiKey, PackageInfo package, VersionInfo version, bool resolve = true)
        {
            try
            {
                // Git+tag fast path: no download needed, just set manifest to git URL with tag
                if (!package.IsExternal)
                {
                    var gitUrl = GetGitUrlForChannel(package, version.Channel);
                    if (gitUrl != null && !string.IsNullOrEmpty(version.TagName))
                    {
                        EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Installing {package.DisplayName}...", 0.5f);

                        var gitUpmName = package.GetUpmPackageName();

                        // Remove old entry (tgz, folder, or git)
                        var gitOldMatch = FindInstalledEntry(package);
                        if (gitOldMatch != null)
                        {
                            RemoveManifestEntry(gitOldMatch.Value.key);
                            CleanupLegacyPackageFiles(gitOldMatch.Value.key);
                        }

                        // Clean up old package files under the canonical name too
                        CleanupLegacyPackageFiles(gitUpmName);

                        // Remove embedded packages if they exist
                        if (HasEmbeddedPackage(gitUpmName))
                            RemoveEmbeddedPackage(gitUpmName);

                        // Set manifest to git URL with tag
                        SetManifestEntry(gitUpmName, StripGitRef(gitUrl) + "#" + version.TagName);

                        if (resolve)
                        {
                            PurrPackageManagerCache.Invalidate();
                            UnityEditor.PackageManager.Client.Resolve();
                            AssetDatabase.Refresh();
                        }

                        EditorUtility.ClearProgressBar();
                        return Result<bool>.Ok(true);
                    }
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Getting download URL...", 0.1f);

                var downloadResult = await PurrPackageManagerAPI.GetDownloadUrl(apiKey, package.Id, version.Id);
                if (!downloadResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail(downloadResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Downloading {package.DisplayName}...", 0.3f);

                var downloadFilename = downloadResult.Value.Filename ?? (package.GetUpmPackageName() + ".unitypackage");
                var tempPath = Path.Combine(Path.GetTempPath(), downloadFilename);

                var fileResult = await PurrPackageManagerAPI.DownloadFile(downloadResult.Value.Url, tempPath);
                if (!fileResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail(fileResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Installing package...", 0.7f);

                // Extract to a temp directory to read package.json
                var tempExtractDir = Path.Combine("Temp", "PurrNet_extract_" + DateTime.Now.Ticks);

                try
                {
                    ExtractUnityPackage(tempPath, tempExtractDir);
                }
                catch (Exception extractEx)
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail($"Failed to extract package: {extractEx.Message}");
                }

                // Read the real package name and version from package.json
                var pkgJsonPath = Path.Combine(tempExtractDir, "package.json");
                if (!File.Exists(pkgJsonPath))
                {
                    Directory.Delete(tempExtractDir, true);
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail("Extracted package does not contain a package.json");
                }

                var pkgJson = JObject.Parse(await File.ReadAllTextAsync(pkgJsonPath));
                var upmName = pkgJson["name"]?.ToString();
                var upmVersion = pkgJson["version"]?.ToString();

                if (string.IsNullOrEmpty(upmName) || string.IsNullOrEmpty(upmVersion))
                {
                    Directory.Delete(tempExtractDir, true);
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail("package.json is missing 'name' or 'version' field");
                }

                // Remove embedded packages if they exist (Unity prioritizes Packages/{name}/ over manifest)
                var apiName = package.GetUpmPackageName();
                if (HasEmbeddedPackage(apiName) || HasEmbeddedPackage(upmName))
                {
                    EditorUtility.ClearProgressBar();
                    if (!EditorUtility.DisplayDialog("Embedded Package Found",
                        $"An embedded copy of {package.DisplayName} exists in the Packages folder. " +
                        "It must be removed to install the new version. Any local changes will be lost.",
                        "Remove & Continue", "Cancel"))
                    {
                        Directory.Delete(tempExtractDir, true);
                        return Result<bool>.Fail("Installation cancelled by user.");
                    }
                    EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Removing embedded package...", 0.7f);
                    RemoveEmbeddedPackage(apiName);
                    RemoveEmbeddedPackage(upmName);
                }

                // Remove old version before installing new one
                var oldMatch = FindInstalledEntry(package);
                if (oldMatch != null)
                {
                    RemoveManifestEntry(oldMatch.Value.key);
                    CleanupLegacyPackageFiles(oldMatch.Value.key);
                }

                // Install as embedded package at Packages/{name}/
                var folderPath = Path.Combine("Packages", upmName);

                // Remove existing directory/junction BEFORE cleaning legacy files,
                // because Unity creates junctions in Packages/{name}/ pointing to
                // PurrPackages/ for file: manifest entries — must remove while target still exists
                SafeRemoveDirectory(folderPath);

                // Clean up any legacy PurrPackages/ files
                CleanupLegacyPackageFiles(upmName);

                // Move extracted content to final location
                Directory.Move(tempExtractDir, folderPath);

                // Embedded packages are auto-discovered by Unity — remove any stale manifest entry
                RemoveManifestEntry(upmName);

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Cleaning up...", 0.9f);

                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                if (Directory.Exists(tempExtractDir))
                    Directory.Delete(tempExtractDir, true);

                if (resolve)
                {
                    PurrPackageManagerCache.Invalidate();
                    UnityEditor.PackageManager.Client.Resolve();
                    AssetDatabase.Refresh();
                }
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
            var match = FindInstalledEntry(package);
            if (match == null)
                return false;

            if (!EditorUtility.DisplayDialog("Remove Package",
                $"Are you sure you want to remove {package.DisplayName}?",
                "Remove", "Cancel"))
                return false;

            try
            {
                var upmName = match.Value.key;
                var apiName = package.GetUpmPackageName();

                // Remove embedded packages if they exist
                RemoveEmbeddedPackage(upmName);
                if (apiName != upmName)
                    RemoveEmbeddedPackage(apiName);

                // Delete old package files (legacy PurrPackages/ tgz or folders)
                CleanupLegacyPackageFiles(upmName);

                RemoveManifestEntry(upmName);

                PurrPackageManagerCache.Invalidate();
                UnityEditor.PackageManager.Client.Resolve();
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to remove package: {e.Message}");
                return false;
            }
        }

        public static void InstallExternal(PackageInfo package, string gitUrl, bool resolve = true)
        {
            try
            {
                EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Installing {package.DisplayName}...", 0.5f);

                var upmName = package.GetUpmPackageName();

                // Remove old entry if present
                var oldMatch = FindInstalledEntry(package);
                if (oldMatch != null)
                {
                    RemoveManifestEntry(oldMatch.Value.key);

                    // Clean up old package files if switching install method
                    CleanupLegacyPackageFiles(oldMatch.Value.key);
                }

                // Remove embedded packages if they exist
                if (HasEmbeddedPackage(upmName))
                    RemoveEmbeddedPackage(upmName);

                SetManifestEntry(upmName, gitUrl);

                if (resolve)
                {
                    PurrPackageManagerCache.Invalidate();
                    UnityEditor.PackageManager.Client.Resolve();
                    AssetDatabase.Refresh();
                }
                EditorUtility.ClearProgressBar();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[PurrNet] Failed to install external package: {e.Message}");
            }
        }

        public static string GetInstalledGitChannel(PackageInfo package)
        {
            var match = FindInstalledEntry(package);
            if (match == null) return "release";
            var value = match.Value.value;
            if (!IsGitUrl(value)) return "release";

            if (!string.IsNullOrEmpty(package.GitInstallUrlDev) && value == package.GitInstallUrlDev)
                return "dev";
            return "release";
        }

        private static bool IsGitUrl(string value)
        {
            return value != null &&
                   (value.StartsWith("https://") || value.StartsWith("git://") || value.StartsWith("git+"));
        }

        /// <summary>
        /// Strips the #fragment from a git URL but preserves ?path= query parameters.
        /// Used when constructing git+tag manifest entries.
        /// </summary>
        private static string StripGitRef(string gitUrl)
        {
            if (gitUrl == null) return "";
            var hashIdx = gitUrl.IndexOf('#');
            return hashIdx >= 0 ? gitUrl.Substring(0, hashIdx) : gitUrl;
        }

        /// <summary>
        /// Picks the appropriate git install URL for a given channel,
        /// falling back to the other channel if the preferred one is null.
        /// </summary>
        private static string GetGitUrlForChannel(PackageInfo package, string channel)
        {
            if (string.Equals(channel, "dev", StringComparison.OrdinalIgnoreCase))
                return package.GitInstallUrlDev ?? package.GitInstallUrlRelease;
            return package.GitInstallUrlRelease ?? package.GitInstallUrlDev;
        }

        /// <summary>
        /// Cleans up old package files in the legacy PurrPackages/ directory for a given UPM name.
        /// Handles both legacy .tgz files and folder-based installs.
        /// Also removes the PurrPackages/ directory itself if it becomes empty.
        /// </summary>
        private static void CleanupLegacyPackageFiles(string upmName)
        {
            if (!Directory.Exists(LegacyPackagesDir)) return;

            // Old tgz files
            foreach (var f in Directory.GetFiles(LegacyPackagesDir, upmName + "-*.tgz"))
            {
                try { File.Delete(f); }
                catch
                {
                    // ignored
                }
            }

            // Folder installs
            foreach (var d in Directory.GetDirectories(LegacyPackagesDir, upmName + "-*"))
            {
                SafeRemoveDirectory(d);
            }

            // Remove PurrPackages/ itself if now empty
            try
            {
                if (Directory.GetFileSystemEntries(LegacyPackagesDir).Length == 0)
                    Directory.Delete(LegacyPackagesDir);
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Strips the fragment (#branch/tag/commit) and query string from a git URL
        /// so that URLs pointing to the same repo can be compared regardless of ref.
        /// </summary>
        private static string GetBaseGitUrl(string gitUrl)
        {
            if (gitUrl == null) return "";
            var hashIdx = gitUrl.IndexOf('#');
            if (hashIdx >= 0)
                gitUrl = gitUrl.Substring(0, hashIdx);
            var queryIdx = gitUrl.IndexOf('?');
            if (queryIdx >= 0)
                gitUrl = gitUrl.Substring(0, queryIdx);
            return gitUrl.TrimEnd('/');
        }

        /// <summary>
        /// Reads the actual semver version from the resolved package in Library/PackageCache.
        /// </summary>
        private static string GetResolvedPackageVersion(string packageName)
        {
            const string cacheDir = "Library/PackageCache";
            if (!Directory.Exists(cacheDir))
                return null;

            try
            {
                var dirs = Directory.GetDirectories(cacheDir, packageName + "@*");
                if (dirs.Length == 0)
                    return null;

                var pkgJsonPath = Path.Combine(dirs[0], "package.json");
                if (!File.Exists(pkgJsonPath))
                    return null;

                var json = JObject.Parse(File.ReadAllText(pkgJsonPath));
                return json["version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void SetManifestEntry(string packageName, string value)
        {
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                var deps = manifest["dependencies"] as JObject;
                if (deps == null)
                {
                    deps = new JObject();
                    manifest["dependencies"] = deps;
                }
                deps[packageName] = value;
                File.WriteAllText(ManifestPath, manifest.ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Failed to update manifest.json: {e.Message}");
            }
        }

        private static void RemoveManifestEntry(string packageName)
        {
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                var deps = manifest["dependencies"] as JObject;
                if (deps != null && deps.ContainsKey(packageName))
                {
                    deps.Remove(packageName);
                    File.WriteAllText(ManifestPath, manifest.ToString(Formatting.Indented));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Failed to update manifest.json: {e.Message}");
            }
        }

        private static void CreateTarGz(string sourceDir, string outputPath)
        {
            using var fileStream = File.Create(outputPath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);

            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                var relativePath = filePath[sourceDir.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');

                // npm/Unity tgz convention: all files under a "package/" root
                var tarPath = "package/" + relativePath;
                var content = File.ReadAllBytes(filePath);
                var header = CreateTarHeader(tarPath, content.Length);

                gzipStream.Write(header, 0, 512);
                gzipStream.Write(content, 0, content.Length);

                // Pad to 512-byte boundary
                int remainder = content.Length % 512;
                if (remainder > 0)
                {
                    var padding = new byte[512 - remainder];
                    gzipStream.Write(padding, 0, padding.Length);
                }
            }

            // End of archive: two 512-byte zero blocks
            var endBlock = new byte[1024];
            gzipStream.Write(endBlock, 0, 1024);
        }

        private static byte[] CreateTarHeader(string entryPath, long size)
        {
            var header = new byte[512];

            // Split path into prefix (max 155) and name (max 100) for ustar
            string name = entryPath;
            string prefix = "";

            if (Encoding.ASCII.GetByteCount(name) > 100)
            {
                var lastSlash = name.LastIndexOf('/', Math.Min(name.Length - 1, 155));
                if (lastSlash > 0)
                {
                    prefix = name.Substring(0, lastSlash);
                    name = name.Substring(lastSlash + 1);
                }
            }

            WriteAscii(header, 0, name, 100);
            WriteAscii(header, 100, "0100644\0", 8);   // File mode
            WriteAscii(header, 108, "0000000\0", 8);   // Owner ID
            WriteAscii(header, 116, "0000000\0", 8);   // Group ID

            var sizeStr = Convert.ToString(size, 8).PadLeft(11, '0');
            WriteAscii(header, 124, sizeStr + "\0", 12);

            var unixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            var timeStr = Convert.ToString(unixTime, 8).PadLeft(11, '0');
            WriteAscii(header, 136, timeStr + "\0", 12);

            header[156] = (byte)'0'; // Regular file

            WriteAscii(header, 257, "ustar\0", 6);     // Magic
            WriteAscii(header, 263, "00", 2);           // Version

            if (prefix.Length > 0)
                WriteAscii(header, 345, prefix, 155);

            // Compute checksum (fill checksum field with spaces first)
            for (int i = 148; i < 156; i++) header[i] = (byte)' ';
            int checksum = 0;
            for (int i = 0; i < 512; i++) checksum += header[i];
            var checksumStr = Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ";
            WriteAscii(header, 148, checksumStr, 8);

            return header;
        }

        private static void WriteAscii(byte[] buffer, int offset, string value, int fieldLength)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var len = Math.Min(bytes.Length, fieldLength);
            Array.Copy(bytes, 0, buffer, offset, len);
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
                    string tarName = Encoding.ASCII.GetString(tarBytes, pos, 100).TrimEnd('\0');
                    string sizeStr = Encoding.ASCII.GetString(tarBytes, pos + 124, 12).Trim('\0', ' ');
                    long size = sizeStr.Length > 0 ? Convert.ToInt64(sizeStr, 8) : 0;
                    char typeFlag = (char)tarBytes[pos + 156];

                    // ustar prefix field (offset 345, 155 bytes)
                    string tarPrefix = Encoding.ASCII.GetString(tarBytes, pos + 345, 155).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(tarPrefix))
                        tarName = tarPrefix + "/" + tarName;

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
                        tarName = longName;
                        longName = null;
                    }

                    // Skip pax extended headers
                    if (typeFlag == 'x' || typeFlag == 'g')
                        continue;

                    // Skip directories
                    if (typeFlag == '5')
                        continue;

                    // Strip leading "./"
                    if (tarName.StartsWith("./"))
                        tarName = tarName.Substring(2);

                    // Strip trailing "/"
                    tarName = tarName.TrimEnd('/');

                    // Entries are "{guid}/{type}" where type is pathname, asset, or asset.meta
                    var slashIdx = tarName.IndexOf('/');
                    if (slashIdx < 0)
                        continue;

                    string guid = tarName.Substring(0, slashIdx);
                    string entryName = tarName.Substring(slashIdx + 1);

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

            // Find the root prefix by locating the shallowest package.json
            string rootPrefix = null;
            foreach (var entry in entries.Values)
            {
                if (entry.Pathname == null)
                    continue;

                var fn = entry.Pathname;
                // Normalize slashes
                fn = fn.Replace('\\', '/');
                entry.Pathname = fn;

                if (fn.EndsWith("/package.json") || fn == "package.json")
                {
                    var prefix = fn.Substring(0, fn.Length - "package.json".Length);
                    if (rootPrefix == null || prefix.Length < rootPrefix.Length)
                        rootPrefix = prefix;
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

                // Skip entries that are parent directories of the root prefix
                // e.g., "Assets" or "Assets/SomePlugin" when rootPrefix is "Assets/SomePlugin/"
                if (rootPrefix.Length > 0 && rootPrefix.StartsWith(entry.Pathname + "/"))
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
