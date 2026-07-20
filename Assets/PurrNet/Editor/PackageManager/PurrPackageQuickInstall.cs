using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    /// <summary>
    /// Lightweight install-by-UPM-name surface over <see cref="PurrPackageManagerInstaller"/>.
    /// Designed to be called from custom inspectors that want a one-click install button
    /// without forcing the user to open the PurrNet Packages window first.
    /// </summary>
    public static class PurrPackageQuickInstall
    {
        private const string ManifestPath = "Packages/manifest.json";

        private static bool _inProgress;

        /// <summary>
        /// True while an InstallByUpmName call is running. UI should disable other actions.
        /// </summary>
        public static bool isInstalling => _inProgress;

        /// <summary>
        /// Cheap manifest-only check — returns true if the package is present in
        /// Packages/{upmName}/ as an embedded folder OR has an entry in manifest.json.
        /// Does not require the PurrNet API.
        /// </summary>
        public static bool IsInstalled(string upmName)
        {
            if (string.IsNullOrEmpty(upmName))
                return false;

            if (Directory.Exists(Path.Combine("Packages", upmName)))
                return true;

            if (!File.Exists(ManifestPath))
                return false;

            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                var deps = manifest["dependencies"] as JObject;
                return deps?[upmName] != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Looks up <paramref name="upmName"/> in the PurrNet packages catalog and installs
        /// the latest release version. External packages install via git URL with no auth;
        /// non-external packages require the user to be logged in and entitled.
        /// </summary>
        public static async Task<Result<bool>> InstallByUpmName(string upmName)
        {
            if (string.IsNullOrEmpty(upmName))
                return Result<bool>.Fail("UPM name is empty.");

            var packagesResp = await GetPackagesCached();
            if (!packagesResp.Success)
                return Result<bool>.Fail(packagesResp.Error);

            var match = FindByUpmName(packagesResp.Value, upmName);
            if (match == null)
                return Result<bool>.Fail($"No PurrNet package with UPM name '{upmName}' was found.");

            if (match.IsExternal)
            {
                var gitUrl = match.GitInstallUrlRelease ?? match.GitInstallUrlDev;
                if (string.IsNullOrEmpty(gitUrl))
                    return Result<bool>.Fail($"'{match.DisplayName}' is external but has no install URL.");
                return await PurrPackageManagerInstaller.InstallExternalWithDependencies(
                    PurrPackageManagerAuth.GetApiKey(), match, gitUrl, packagesResp.Value.Packages);
            }

            if (!match.HasAccess)
                return Result<bool>.Fail($"You don't have access to '{match.DisplayName}'. Open the PurrNet Packages window to subscribe.");

            var target = PickLatestRelease(match);
            if (target == null)
                return Result<bool>.Fail($"'{match.DisplayName}' has no installable version.");

            var apiKey = PurrPackageManagerAuth.GetApiKey();
            return await PurrPackageManagerInstaller.InstallWithDependencies(
                apiKey, match, target, packagesResp.Value.Packages);
        }

        /// <summary>
        /// Opens the PurrNet Packages window. Useful as a fallback when an automated
        /// install can't proceed (e.g. user not logged in, package gated by tier).
        /// </summary>
        public static void OpenPackagesWindow()
        {
            PurrPackageManagerWindow.ShowWindow();
        }

        /// <summary>
        /// Inspector helper — draws a HelpBox plus install/open-window buttons. Triggers
        /// <see cref="InstallByUpmName"/> when clicked and surfaces failures via a dialog
        /// that offers to fall back to the Packages window.
        /// </summary>
        public static void DrawInstallControls(string upmName, string displayName, string warning)
        {
            EditorGUILayout.HelpBox(warning, MessageType.Warning);

            using (new EditorGUI.DisabledScope(_inProgress))
            {
                if (GUILayout.Button(_inProgress
                        ? $"Installing {displayName}..."
                        : $"Install {displayName}"))
                {
                    _ = RunInstall(upmName, displayName);
                }
            }
        }

        private static async Task RunInstall(string upmName, string displayName)
        {
            if (_inProgress)
                return;

            _inProgress = true;
            try
            {
                var result = await InstallByUpmName(upmName);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog(
                        $"Install {displayName} failed",
                        result.Error,
                        "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Install of '{upmName}' threw: {e}");
            }
            finally
            {
                _inProgress = false;
            }
        }

        private static async Task<Result<PackagesResponse>> GetPackagesCached()
        {
            if (PurrPackageManagerCache.TryGetPackages(out var cached))
                return Result<PackagesResponse>.Ok(cached);

            var apiKey = PurrPackageManagerAuth.GetApiKey();
            var fetched = await PurrPackageManagerAPI.GetPackages(apiKey);
            if (!fetched.Success)
                return Result<PackagesResponse>.Fail(fetched.Error);

            PurrPackageManagerCache.SetPackages(fetched.Value);
            return Result<PackagesResponse>.Ok(fetched.Value);
        }

        private static PackageInfo FindByUpmName(PackagesResponse packagesResp, string upmName)
        {
            if (packagesResp?.Packages == null)
                return null;

            foreach (var p in packagesResp.Packages)
            {
                if (string.Equals(p.GetUpmPackageName(), upmName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return null;
        }

        private static VersionInfo PickLatestRelease(PackageInfo package)
        {
            if (package.Versions == null || package.Versions.Length == 0)
                return null;

            // Versions array is documented as newest-first; prefer the first release entry.
            foreach (var v in package.Versions)
            {
                if (string.Equals(v.Channel, "release", StringComparison.OrdinalIgnoreCase))
                    return v;
            }

            return package.Versions[0];
        }
    }
}
