using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PurrNet.Editor
{
    public static class VerifyILMenu
    {
        const string MENU_PATH = "Tools/PurrNet/Analysis/Verify IL";

        [MenuItem(MENU_PATH)]
        public static void RunVerifyIL()
        {
            if (!TryLocateIlVerify(out var ilverifyExe, out var ilverifyArgsPrefix))
            {
                Debug.LogError(
                    "[Verify IL] Could not locate `ilverify`. Install it as a .NET global tool:\n" +
                    "    dotnet tool install --global dotnet-ilverify\n" +
                    "Then make sure %USERPROFILE%\\.dotnet\\tools is on your PATH and restart Unity.");
                return;
            }

            var projectPath = Directory.GetParent(Application.dataPath)!.FullName;
            var scriptAssembliesDir = Path.Combine(projectPath, "Library", "ScriptAssemblies");
            if (!Directory.Exists(scriptAssembliesDir))
            {
                Debug.LogError($"[Verify IL] ScriptAssemblies directory not found: {scriptAssembliesDir}");
                return;
            }

            var unityDataPath = EditorApplication.applicationContentsPath;
            var refDirs = CollectReferenceDirectories(scriptAssembliesDir, unityDataPath);

            var targets = Directory.GetFiles(scriptAssembliesDir, "*.dll");
            if (targets.Length == 0)
            {
                Debug.LogWarning($"[Verify IL] No DLLs found under {scriptAssembliesDir}");
                return;
            }

            Debug.Log($"[Verify IL] Verifying {targets.Length} assemblies against {refDirs.Count} reference dirs via `{ilverifyExe} {ilverifyArgsPrefix}`...");

            var responseFile = WriteReferenceResponseFile(refDirs);
            int totalFailures = 0;
            int totalAssembliesWithErrors = 0;
            var sw = Stopwatch.StartNew();

            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var dll = targets[i];
                    var name = Path.GetFileName(dll);
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Verify IL",
                            $"{name} ({i + 1}/{targets.Length})",
                            (float)i / targets.Length))
                    {
                        Debug.LogWarning("[Verify IL] Cancelled by user.");
                        return;
                    }

                    if (RunIlverify(ilverifyExe, ilverifyArgsPrefix, dll, responseFile, "mscorlib", out var stdout, out var stderr, out var exitCode))
                        continue;

                    var output = (stdout + "\n" + stderr).Trim();
                    ClassifyOutput(output, out var errorCount, out var calliSkipCount, out var realErrorBlock);
                    if (errorCount > 0)
                    {
                        totalAssembliesWithErrors++;
                        totalFailures += errorCount;
                        Debug.LogError($"[Verify IL] {name}: {errorCount} error(s) (exit {exitCode})\n{realErrorBlock}");
                    }
                    if (calliSkipCount > 0)
                    {
                        Debug.LogWarning($"[Verify IL] {name}: {calliSkipCount} `calli` site(s) skipped (ILVerify limitation, not an IL defect).");
                    }
                    if (errorCount == 0 && calliSkipCount == 0 && exitCode != 0)
                    {
                        Debug.LogWarning($"[Verify IL] {name}: ilverify exited {exitCode} but produced no parseable errors\n{output}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                sw.Stop();
                try { if (File.Exists(responseFile)) File.Delete(responseFile); }
                catch
                {
                    // ignored
                }
            }

            if (totalAssembliesWithErrors == 0)
                Debug.Log($"[Verify IL] All {targets.Length} assemblies verified clean ({sw.Elapsed.TotalSeconds:F1}s).");
            else
                Debug.LogError($"[Verify IL] {totalAssembliesWithErrors}/{targets.Length} assemblies had IL errors ({totalFailures} total) in {sw.Elapsed.TotalSeconds:F1}s.");
        }

        static bool TryLocateIlVerify(out string exe, out string argsPrefix)
        {
            // Preferred: dotnet global tool shim `ilverify` on PATH.
            if (ToolChecker.CheckTool("ilverify", "--help"))
            {
                exe = "ilverify";
                argsPrefix = string.Empty;
                return true;
            }

            // Fallback: `dotnet ilverify` (some installs expose it via the `dotnet` driver).
            if (ToolChecker.CheckTool("dotnet", "ilverify --help"))
            {
                exe = "dotnet";
                argsPrefix = "ilverify";
                return true;
            }

            exe = null;
            argsPrefix = null;
            return false;
        }

        static List<string> CollectReferenceDirectories(string scriptAssembliesDir, string unityDataPath)
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { scriptAssembliesDir };

            if (!string.IsNullOrEmpty(unityDataPath))
            {
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "Managed"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "Managed", "UnityEngine"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "MonoBleedingEdge", "lib", "mono", "unityaot-win32"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "MonoBleedingEdge", "lib", "mono", "unityaot-win32", "Facades"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "MonoBleedingEdge", "lib", "mono", "unityaot-macos"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "MonoBleedingEdge", "lib", "mono", "unityaot-macos", "Facades"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "MonoBleedingEdge", "lib", "mono", "unityaot-linux"));
                AddIfHasDlls(dirs, Path.Combine(unityDataPath, "MonoBleedingEdge", "lib", "mono", "unityaot-linux", "Facades"));
                AddDllDirsRecursive(dirs, Path.Combine(unityDataPath, "PlaybackEngines"));
                AddDllDirsRecursive(dirs, Path.Combine(unityDataPath, "UnityExtensions"));
            }

            // Unity packages: nunit.framework, etc. live under Library/PackageCache/<pkg>/...
            var projectPath = Directory.GetParent(scriptAssembliesDir)!.Parent!.FullName;
            AddDllDirsRecursive(dirs, Path.Combine(projectPath, "Library", "PackageCache"));
            AddDllDirsRecursive(dirs, Path.Combine(projectPath, "Library", "ScriptAssemblies"));
            AddDllDirsRecursive(dirs, Path.Combine(projectPath, "Assets"));

            return new List<string>(dirs);
        }

        static void AddIfHasDlls(HashSet<string> set, string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                using var enumerator = Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly).GetEnumerator();
                if (enumerator.MoveNext())
                    set.Add(path);
            }
            catch { /* ignore unreadable dirs */ }
        }

        static void AddDllDirsRecursive(HashSet<string> set, string root)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
                {
                    var dir = Path.GetDirectoryName(dll);
                    if (!string.IsNullOrEmpty(dir))
                        set.Add(dir);
                }
            }
            catch { /* ignore unreadable dirs */ }
        }

        static string WriteReferenceResponseFile(List<string> refDirs)
        {
            // System.CommandLine (used by ilverify) auto-expands `@<file>` response files.
            // Each line contributes one CLI argument, so emit alternating `-r` and the glob path.
            var path = Path.Combine(Path.GetTempPath(), $"purrnet-ilverify-{Guid.NewGuid():N}.rsp");
            using var w = new StreamWriter(path);
            foreach (var dir in refDirs)
            {
                w.WriteLine("-r");
                w.WriteLine(Path.Combine(dir, "*.dll"));
            }
            return path;
        }

        static bool RunIlverify(string exe, string argsPrefix, string targetDll, string responseFile, string systemModule,
            out string stdout, out string stderr, out int exitCode)
        {
            var args = new StringBuilder();
            if (!string.IsNullOrEmpty(argsPrefix))
                args.Append(argsPrefix).Append(' ');
            args.Append('"').Append(targetDll).Append('"');
            args.Append(" \"@").Append(responseFile).Append('"');
            args.Append(" --system-module ").Append(systemModule);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using var p = Process.Start(psi);
                stdout = p!.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                exitCode = p.ExitCode;
                if (exitCode != 0) return false;
                ClassifyOutput(stdout + "\n" + stderr, out var errs, out _, out _);
                return errs == 0;
            }
            catch (Exception ex)
            {
                stdout = string.Empty;
                stderr = ex.Message;
                exitCode = -1;
                return false;
            }
        }

        const string CALLI_NOT_IMPLEMENTED = "ImportCalli not implemented";

        static void ClassifyOutput(string output, out int errorCount, out int calliSkipCount, out string realErrorBlock)
        {
            errorCount = 0;
            calliSkipCount = 0;
            if (string.IsNullOrEmpty(output))
            {
                realErrorBlock = string.Empty;
                return;
            }

            var sb = new StringBuilder();
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (!trimmed.StartsWith("[IL]:", StringComparison.Ordinal)) continue;

                if (trimmed.IndexOf(CALLI_NOT_IMPLEMENTED, StringComparison.Ordinal) >= 0)
                {
                    calliSkipCount++;
                    continue;
                }

                errorCount++;
                sb.AppendLine(line);
            }
            realErrorBlock = sb.ToString().TrimEnd();
        }
    }
}
