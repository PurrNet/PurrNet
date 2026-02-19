using System;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public class PurrPackageManagerWindow : EditorWindow
    {
        private string _apiKeyInput = "";
        private string _errorMessage;
        private bool _isLoading;
        private Vector2 _scrollPosition;

        private PackagesResponse _packages;
        private EntitlementsResponse _entitlements;

        [MenuItem("Tools/PurrNet/Package Manager", false, -99)]
        public static void ShowWindow()
        {
            var window = GetWindow<PurrPackageManagerWindow>("PurrNet Package Manager");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            _apiKeyInput = PurrPackageManagerAuth.GetApiKey();
            if (PurrPackageManagerAuth.HasApiKey())
                LoadData();
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(4);
            DrawApiKeySection();
            EditorGUILayout.Space(4);

            if (!PurrPackageManagerAuth.HasApiKey())
            {
                EditorGUILayout.HelpBox("Enter your API key to view available packages.", MessageType.Info);
                return;
            }

            if (_isLoading)
            {
                GUILayout.BeginVertical("Box");
                GUILayout.Label("Loading...");
                GUILayout.EndVertical();
                return;
            }

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                if (GUILayout.Button("Retry"))
                    LoadData();
                return;
            }

            if (_entitlements != null)
            {
                var tier = string.IsNullOrEmpty(_entitlements.Tier) ? "Free" : _entitlements.Tier;
                EditorGUILayout.LabelField("Tier", tier, EditorStyles.boldLabel);
                EditorGUILayout.Space(2);
            }

            if (_packages?.Packages == null || _packages.Packages.Length == 0)
            {
                EditorGUILayout.HelpBox("No packages available.", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var package in _packages.Packages)
            {
                if (package.Versions == null || package.Versions.Length == 0)
                    continue;

                var release = FindLatestByChannel(package, "release");
                var dev = FindLatestByChannel(package, "dev");

                DrawPackageCard(package, release, dev);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("PurrNet Package Manager", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.enabled = !_isLoading && PurrPackageManagerAuth.HasApiKey();
            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                PurrPackageManagerCache.Invalidate();
                LoadData();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawApiKeySection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key", GUILayout.Width(50));
            _apiKeyInput = EditorGUILayout.PasswordField(_apiKeyInput);

            if (string.IsNullOrEmpty(_apiKeyInput) && !PurrPackageManagerAuth.HasApiKey())
            {
                if (GUILayout.Button("Get API Key", GUILayout.Width(80)))
                    Application.OpenURL("https://purrnet.dev/profile?tab=api-keys");
            }
            else
            {
                if (GUILayout.Button("Save", GUILayout.Width(50)))
                {
                    PurrPackageManagerAuth.SetApiKey(_apiKeyInput);
                    PurrPackageManagerCache.Invalidate();
                    LoadData();
                }
            }

            GUI.enabled = PurrPackageManagerAuth.HasApiKey();
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                _apiKeyInput = "";
                PurrPackageManagerAuth.ClearApiKey();
                PurrPackageManagerCache.Invalidate();
                _packages = null;
                _entitlements = null;
                _errorMessage = null;
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPackageCard(PackageInfo package, VersionInfo release, VersionInfo dev)
        {
            var boxStyle = new GUIStyle("box");
            EditorGUILayout.BeginVertical(boxStyle);

            // Header row: name + latest version or FROZEN
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(package.DisplayName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (package.Frozen)
            {
                var frozenStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.9f, 0.3f, 0.3f) } };
                GUILayout.Label("FROZEN", frozenStyle);
            }
            else if (!string.IsNullOrEmpty(package.LatestVersion))
            {
                GUILayout.Label("v" + package.LatestVersion);
            }

            EditorGUILayout.EndHorizontal();

            // Description
            if (!string.IsNullOrEmpty(package.Description))
            {
                var wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                GUILayout.Label(package.Description, wrapStyle);
            }

            // Info row: tier + installed version
            var installedVersion = PurrPackageManagerInstaller.GetInstalledVersion(package);
            EditorGUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(package.RequiredTier))
                GUILayout.Label($"Tier: {package.RequiredTier}", GUILayout.Width(150));

            if (installedVersion != null)
                GUILayout.Label($"Installed: v{installedVersion}");
            else if (PurrPackageManagerInstaller.IsInstalled(package))
                GUILayout.Label("Installed (unknown version)");

            EditorGUILayout.EndHorizontal();

            // Frozen state
            if (package.Frozen)
            {
                EditorGUILayout.Space(2);

                if (!string.IsNullOrEmpty(package.EntitledVersion))
                    EditorGUILayout.HelpBox($"Your access is frozen at v{package.EntitledVersion}. Resubscribe to get v{package.LatestVersion}.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("Your access to this package is frozen. Resubscribe to regain access.", MessageType.Warning);

                if (GUILayout.Button("Resubscribe"))
                    Application.OpenURL("https://purrnet.dev");

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
                return;
            }

            // Install buttons
            bool isInstalled = PurrPackageManagerInstaller.IsInstalled(package);
            EditorGUILayout.Space(2);

            if (release == null && dev == null)
            {
                EditorGUILayout.HelpBox("You don't have access to this package.", MessageType.Info);
                if (GUILayout.Button("Get Access"))
                    Application.OpenURL("https://purrnet.dev/membership");

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            bool releaseUpToDate = release != null && isInstalled && installedVersion == release.Version;
            bool devUpToDate = dev != null && isInstalled && installedVersion == dev.Version;

            if (release != null)
            {
                GUI.enabled = !releaseUpToDate;
                string releaseLabel = releaseUpToDate ? $"Release v{release.Version}" : GetInstallButtonLabel("Release", release.Version, installedVersion, isInstalled);
                if (GUILayout.Button(releaseLabel))
                    InstallPackage(package, release);
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("No Release");
                GUI.enabled = true;
            }

            if (dev != null)
            {
                GUI.enabled = !devUpToDate;
                string devLabel = devUpToDate ? $"Dev v{dev.Version}" : GetInstallButtonLabel("Dev", dev.Version, installedVersion, isInstalled);
                if (GUILayout.Button(devLabel))
                    InstallPackage(package, dev);
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("No Dev");
                GUI.enabled = true;
            }

            if (isInstalled)
            {
                var redStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = Color.red } };
                if (GUILayout.Button("Remove", redStyle, GUILayout.Width(70)))
                    PurrPackageManagerInstaller.Remove(package);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private static string GetInstallButtonLabel(string channel, string version, string installedVersion, bool isInstalled)
        {
            if (!isInstalled)
                return $"{channel} v{version}";

            if (installedVersion == version)
                return $"Reinstall {channel}";

            return $"{channel} v{version}";
        }

        private static VersionInfo FindLatestByChannel(PackageInfo package, string channel)
        {
            if (package.Versions == null)
                return null;

            foreach (var v in package.Versions)
            {
                if (string.Equals(v.Channel, channel, StringComparison.OrdinalIgnoreCase))
                    return v;
            }

            return null;
        }

        private async void LoadData()
        {
            _isLoading = true;
            _errorMessage = null;
            Repaint();

            try
            {
                var apiKey = PurrPackageManagerAuth.GetApiKey();

                if (PurrPackageManagerCache.TryGetEntitlements(out var cachedEntitlements))
                {
                    _entitlements = cachedEntitlements;
                }
                else
                {
                    var entitlementsResult = await PurrPackageManagerAPI.GetEntitlements(apiKey);
                    if (entitlementsResult.Success)
                    {
                        _entitlements = entitlementsResult.Value;
                        PurrPackageManagerCache.SetEntitlements(_entitlements);
                    }
                    else
                    {
                        _errorMessage = entitlementsResult.Error;
                        _isLoading = false;
                        Repaint();
                        return;
                    }
                }

                if (PurrPackageManagerCache.TryGetPackages(out var cachedPackages))
                {
                    _packages = cachedPackages;
                }
                else
                {
                    var packagesResult = await PurrPackageManagerAPI.GetPackages(apiKey);
                    if (packagesResult.Success)
                    {
                        _packages = packagesResult.Value;
                        PurrPackageManagerCache.SetPackages(_packages);
                    }
                    else
                    {
                        _errorMessage = packagesResult.Error;
                        _isLoading = false;
                        Repaint();
                        return;
                    }
                }

                _isLoading = false;
                Repaint();
            }
            catch (Exception e)
            {
                _errorMessage = e.Message;
                _isLoading = false;
                Repaint();
            }
        }

        private async void InstallPackage(PackageInfo package, VersionInfo version)
        {
            try
            {
                var apiKey = PurrPackageManagerAuth.GetApiKey();
                var result = await PurrPackageManagerInstaller.Install(apiKey, package, version);

                if (!result.Success)
                    EditorUtility.DisplayDialog("Install Failed", result.Error, "Ok");

                Repaint();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Install Failed", e.Message, "Ok");
                Repaint();
            }
        }
    }
}
