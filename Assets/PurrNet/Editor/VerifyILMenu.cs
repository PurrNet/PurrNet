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
                    @"Then make sure %USERPROFILE%\.dotnet\tools is on your PATH and restart Unity.");
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
                return;

            var responseFile = WriteReferenceResponseFile(refDirs);
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
                        return;
                    }

                    if (RunIlverify(ilverifyExe, ilverifyArgsPrefix, dll, responseFile, "mscorlib", out var stdout, out var stderr, out var exitCode))
                        continue;

                    var output = (stdout + "\n" + stderr).Trim();
                    ClassifyOutput(output, out var errorCount, out var realErrorBlock);
                    if (errorCount > 0)
                        Debug.LogError($"[Verify IL] {name}: {errorCount} error(s) (exit {exitCode})\n{realErrorBlock}");
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
                ClassifyOutput(stdout + "\n" + stderr, out var errs, out _);
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

        static readonly string[] _ignoredErrorCodes =
        {
            "[UnmanagedPointer]",      // P/Invoke / unsafe wrappers with byte* etc.
            "[ReturnPtrToStack]",      // Span<T> / ReadOnlySpan<T> returns (ref-struct pattern)
            "[InitOnly]",              // Unsafe.AsRef / pointer-reinterpret writes through readonly
            "[FieldAccess]",           // codegen-synthesized types touching private/internal fields — runtimes skip access checks
            "[MethodAccess]",          // codegen-synthesized types calling private/internal methods — runtimes skip access checks
            "[InitLocals]",            // .locals init flag missing — equivalent to [SkipLocalsInit]; runtimes don't require it
            "[UnsatisfiedMethodInst]", // generic stub through codegen indirection; constraints are runtime-checked, not statically provable
            "[CallVirtOnValueType]",   // codegen emits callvirt on value-type method without `constrained.` prefix; runtimes treat as call
            "[Unverifiable]",          // generic "instruction not verifiable" — almost always unsafe pointer/interop code
            "[ThisMismatch]",          // codegen 'this' parameter type the verifier can't statically reconcile
            "[ConstrainedCallWithNonByRefThis]", // `constrained.` on a by-value 'this'; runtime boxes implicitly
        };

        const string CALLI_NOT_IMPLEMENTED = "ImportCalli not implemented";
        const string EXPECTED_NUMERIC_TYPE = "[ExpectedNumericType]";
        const string STACK_UNEXPECTED = "[StackUnexpected]";
        const string STACK_BY_REF = "[StackByRef]";
        const string ADDRESS_OF = "found address of"; // managed-ref → native-int conversion
        const string FOUND_REF = "found ref ";        // managed object ref reinterpreted (Unsafe.As)
        const string READONLY_ADDRESS = "readonly address"; // `in` parameter / `readonly ref` field
        const string FOUND_NATIVE_INT = "found Native Int"; // pointer → byref interop conversion

        static bool IsIgnorableError(string line)
        {
            if (line.IndexOf(CALLI_NOT_IMPLEMENTED, StringComparison.Ordinal) >= 0)
                return true;

            if (line.IndexOf(EXPECTED_NUMERIC_TYPE, StringComparison.Ordinal) >= 0 &&
                (line.IndexOf(ADDRESS_OF, StringComparison.Ordinal) >= 0 ||
                 line.IndexOf(FOUND_REF, StringComparison.Ordinal) >= 0))
                return true;

            if (line.IndexOf(STACK_UNEXPECTED, StringComparison.Ordinal) >= 0)
            {
                if (line.IndexOf(READONLY_ADDRESS, StringComparison.Ordinal) >= 0 ||
                    line.IndexOf(FOUND_NATIVE_INT, StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("[found ", StringComparison.Ordinal) < 0)
                    return true;
            }

            if (line.IndexOf(STACK_BY_REF, StringComparison.Ordinal) >= 0 &&
                line.IndexOf(FOUND_NATIVE_INT, StringComparison.Ordinal) >= 0)
                return true;

            foreach (var code in _ignoredErrorCodes)
                if (line.IndexOf(code, StringComparison.Ordinal) >= 0)
                    return true;

            return false;
        }

        static void ClassifyOutput(string output, out int errorCount, out string realErrorBlock)
        {
            errorCount = 0;
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
                if (IsIgnorableError(trimmed)) continue;

                errorCount++;
                sb.AppendLine(line);
            }
            realErrorBlock = sb.ToString().TrimEnd();
        }
    }
}
