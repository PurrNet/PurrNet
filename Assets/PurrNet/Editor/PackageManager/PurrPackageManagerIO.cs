using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PurrNet.Editor
{
    internal static class PurrPackageManagerIO
    {
        private static StringComparer PathComparer => Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static string CreateUniqueTempDirectory(string category)
        {
            var safeCategory = string.IsNullOrWhiteSpace(category) ? "operation" : GetSafeFileName(category, "operation");
            var path = Path.Combine(Path.GetTempPath(), "PurrNet", safeCategory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetSafeFileName(string candidate, string fallback)
        {
            // Treat both separators as directory syntax regardless of the current OS. The filename
            // originates on a server and may have been produced on a different platform.
            var normalized = (candidate ?? string.Empty).Replace('\\', '/');
            var separator = normalized.LastIndexOf('/');
            var fileName = separator >= 0 ? normalized[(separator + 1)..] : normalized;
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == "..")
                fileName = fallback;

            foreach (var invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');

            return fileName;
        }

        public static string GetContainedPath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A root directory is required.", nameof(root));
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("Package entry has an empty path.");
            if (Path.IsPathRooted(relativePath))
                throw new InvalidDataException($"Package entry uses a rooted path: '{relativePath}'.");

            var rootFullPath = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));
            var rootWithSeparator = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSeparator, PathComparison))
                throw new InvalidDataException($"Package entry escapes the extraction directory: '{relativePath}'.");

            return candidate;
        }

        public static void WriteAllTextAtomic(string path, string contents)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException($"Could not determine the directory for '{path}'.");

            Directory.CreateDirectory(directory);

            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException(
                    $"'{fullPath}' is read-only. Check it out in source control or make it writable, then try again.");

            var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.purrnet-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempPath, contents, new UTF8Encoding(false));

                if (File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, null);
                else
                    File.Move(tempPath, fullPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup. The original file was never modified if replacement failed.
                }
            }
        }

        public static void DeleteDirectoryBestEffort(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            try
            {
                DeleteDirectoryWithoutFollowingReparsePoints(path);
            }
            catch
            {
                // Temp cleanup must never turn an otherwise successful package operation into a failure.
            }
        }

        private static void DeleteDirectoryWithoutFollowingReparsePoints(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Deleting a junction/symlink recursively can delete its target on some runtimes.
                Directory.Delete(path, false);
                return;
            }

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
                DeleteDirectoryWithoutFollowingReparsePoints(directory);

            Directory.Delete(path, false);
        }

        public static void SyncDirectoryTransactional(string source, string destination)
        {
            var sourceRoot = Path.GetFullPath(source);
            var destinationRoot = Path.GetFullPath(destination);
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException($"Package staging directory does not exist: '{sourceRoot}'.");

            var destinationExisted = Directory.Exists(destinationRoot);
            var backupRoot = CreateUniqueTempDirectory("sync-backup");
            var backedUp = new Dictionary<string, FileAttributes>(PathComparer);
            var createdFiles = new HashSet<string>(PathComparer);
            var mutationStarted = false;

            try
            {
                var sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
                var sourceByRelativePath = sourceFiles.ToDictionary(
                    file => Path.GetRelativePath(sourceRoot, file),
                    file => file,
                    PathComparer);

                var destinationFiles = destinationExisted
                    ? Directory.GetFiles(destinationRoot, "*", SearchOption.AllDirectories)
                    : Array.Empty<string>();
                var destinationByRelativePath = destinationFiles.ToDictionary(
                    file => Path.GetRelativePath(destinationRoot, file),
                    file => file,
                    PathComparer);

                var filesToWrite = new List<(string relativePath, string sourcePath, string destinationPath)>();
                foreach (var pair in sourceByRelativePath)
                {
                    var destinationPath = GetContainedPath(destinationRoot, pair.Key);
                    if (File.Exists(destinationPath) && FilesEqual(pair.Value, destinationPath))
                        continue;

                    filesToWrite.Add((pair.Key, pair.Value, destinationPath));
                }

                // package.json is the visible version marker. Write it last so a failed transaction never
                // advertises the new version while old binaries are still present.
                filesToWrite.Sort((a, b) =>
                {
                    var aIsManifest = string.Equals(a.relativePath, "package.json", StringComparison.OrdinalIgnoreCase);
                    var bIsManifest = string.Equals(b.relativePath, "package.json", StringComparison.OrdinalIgnoreCase);
                    return aIsManifest == bIsManifest ? string.CompareOrdinal(a.relativePath, b.relativePath) : aIsManifest ? 1 : -1;
                });

                var filesToDelete = destinationByRelativePath
                    .Where(pair => !sourceByRelativePath.ContainsKey(pair.Key))
                    .Select(pair => (relativePath: pair.Key, destinationPath: pair.Value))
                    .ToList();

                // Preflight every existing file before changing anything. This catches loaded native DLLs,
                // source-control read-only files, and antivirus locks without leaving a half-updated package.
                foreach (var operation in filesToWrite)
                {
                    if (File.Exists(operation.destinationPath))
                        VerifyWritable(operation.destinationPath);
                }
                foreach (var operation in filesToDelete)
                    VerifyWritable(operation.destinationPath);

                foreach (var operation in filesToWrite)
                {
                    if (File.Exists(operation.destinationPath))
                        BackupFile(operation.relativePath, operation.destinationPath);
                    else
                        createdFiles.Add(operation.destinationPath);
                }
                foreach (var operation in filesToDelete)
                    BackupFile(operation.relativePath, operation.destinationPath);

                Directory.CreateDirectory(destinationRoot);
                mutationStarted = true;
                foreach (var operation in filesToWrite)
                {
                    var parent = Path.GetDirectoryName(operation.destinationPath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    File.Copy(operation.sourcePath, operation.destinationPath, true);
                }

                foreach (var operation in filesToDelete)
                {
                    File.Delete(operation.destinationPath);
                }

                PruneEmptyDirectories(destinationRoot);
                DeleteDirectoryBestEffort(backupRoot);
                return;

                void BackupFile(string relativePath, string originalPath)
                {
                    var backupPath = GetContainedPath(backupRoot, relativePath);
                    var backupDirectory = Path.GetDirectoryName(backupPath);
                    if (!string.IsNullOrEmpty(backupDirectory))
                        Directory.CreateDirectory(backupDirectory);
                    File.Copy(originalPath, backupPath, true);
                    backedUp[relativePath] = File.GetAttributes(originalPath);
                }
            }
            catch (Exception operationException)
            {
                var rollbackErrors = new List<string>();
                if (mutationStarted || createdFiles.Count > 0)
                {
                    foreach (var created in createdFiles)
                    {
                        try
                        {
                            if (File.Exists(created))
                                File.Delete(created);
                        }
                        catch (Exception e)
                        {
                            rollbackErrors.Add($"delete '{created}': {e.Message}");
                        }
                    }

                    foreach (var pair in backedUp)
                    {
                        try
                        {
                            var backupPath = GetContainedPath(backupRoot, pair.Key);
                            var originalPath = GetContainedPath(destinationRoot, pair.Key);
                            var parent = Path.GetDirectoryName(originalPath);
                            if (!string.IsNullOrEmpty(parent))
                                Directory.CreateDirectory(parent);
                            File.Copy(backupPath, originalPath, true);
                            File.SetAttributes(originalPath, pair.Value);
                        }
                        catch (Exception e)
                        {
                            rollbackErrors.Add($"restore '{pair.Key}': {e.Message}");
                        }
                    }

                    if (!destinationExisted && rollbackErrors.Count == 0)
                        DeleteDirectoryBestEffort(destinationRoot);
                }

                DeleteDirectoryBestEffort(backupRoot);
                var rollbackSuffix = rollbackErrors.Count == 0
                    ? string.Empty
                    : " Rollback also encountered: " + string.Join("; ", rollbackErrors);
                throw new IOException(
                    $"Could not update package files transactionally: {operationException.Message}.{rollbackSuffix}",
                    operationException);
            }
        }

        private static void VerifyWritable(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException(
                    $"'{path}' is read-only. Check it out in source control or make it writable, then try again.");

            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        private static bool FilesEqual(string a, string b)
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length)
                return false;
            if (infoA.Length == 0)
                return true;

            const int bufferSize = 64 * 1024;
            var bufferA = new byte[bufferSize];
            var bufferB = new byte[bufferSize];

            using var streamA = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize);
            using var streamB = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize);

            while (true)
            {
                var readA = streamA.Read(bufferA, 0, bufferA.Length);
                var readB = streamB.Read(bufferB, 0, bufferB.Length);
                if (readA != readB)
                    return false;
                if (readA == 0)
                    return true;
                if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                    return false;
            }
        }

        private static void PruneEmptyDirectories(string root)
        {
            if (!Directory.Exists(root))
                return;

            var directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
            Array.Sort(directories, (a, b) => b.Length.CompareTo(a.Length));
            foreach (var directory in directories)
            {
                try
                {
                    if (Directory.GetFileSystemEntries(directory).Length == 0)
                        Directory.Delete(directory, false);
                }
                catch
                {
                    // Empty-directory pruning is cosmetic and should not invalidate a successful sync.
                }
            }
        }
    }
}
