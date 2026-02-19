using System;
using System.Collections.Generic;
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

        private static readonly Color _headerBg = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color _accentColor = new Color(0.4f, 0.7f, 1f, 1f);
        private static readonly Color _installedColor = new Color(0.35f, 0.8f, 0.35f, 1f);
        private static readonly Color _updateColor = new Color(1f, 0.75f, 0.2f, 1f);
        private static readonly Color _frozenColor = new Color(0.9f, 0.35f, 0.35f, 1f);
        private static readonly Color _separatorColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        private GUIStyle _titleStyle;
        private GUIStyle _descStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _smallLabelStyle;
        private GUIStyle _cardStyle;
        private Texture2D _logo;

        [MenuItem("Tools/PurrNet/Package Manager", false, -99)]
        public static void ShowWindow()
        {
            var window = GetWindow<PurrPackageManagerWindow>("PurrNet Package Manager");
            window.minSize = new Vector2(420, 350);
        }

        private void OnEnable()
        {
            _logo = Resources.Load<Texture2D>("purrlogo");
            _apiKeyInput = PurrPackageManagerAuth.GetApiKey();
            if (PurrPackageManagerAuth.HasApiKey())
                LoadData();
        }

        private void InitStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 0, 0)
            };

            _descStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 10,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = Color.white }
            };

            _smallLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
            };

            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(4, 4, 2, 4)
            };
        }

        private void OnGUI()
        {
            InitStyles();

            DrawHeader();
            DrawSeparator();

            if (!PurrPackageManagerAuth.HasApiKey())
            {
                EditorGUILayout.Space(8);
                DrawApiKeySection();
                EditorGUILayout.Space(8);
                DrawCenteredMessage("Enter your API key to view available packages.", MessageType.Info);
                return;
            }

            DrawApiKeySection();
            DrawSeparator();

            if (_isLoading)
            {
                EditorGUILayout.Space(40);
                DrawCenteredLabel("Loading packages...");
                return;
            }

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                EditorGUILayout.Space(4);
                if (GUILayout.Button("Retry", GUILayout.Height(24)))
                    LoadData();
                return;
            }

            if (_packages?.Packages == null || _packages.Packages.Length == 0)
            {
                EditorGUILayout.Space(40);
                DrawCenteredLabel("No packages available.");
                return;
            }

            EditorGUILayout.Space(4);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Collect visible packages sorted by display order
            var visible = new List<(PackageInfo pkg, VersionInfo release, VersionInfo dev)>();
            foreach (var package in _packages.Packages)
            {
                if (package.Versions == null || package.Versions.Length == 0)
                    continue;
                visible.Add((package, FindLatestByChannel(package, "release"), FindLatestByChannel(package, "dev")));
            }
            visible.Sort((a, b) => a.pkg.DisplayOrder.CompareTo(b.pkg.DisplayOrder));

            // Group by category
            var categories = new List<(string name, List<(PackageInfo pkg, VersionInfo release, VersionInfo dev)> items)>();
            var categoryMap = new Dictionary<string, int>();

            foreach (var item in visible)
            {
                var cat = item.pkg.Category ?? "";
                if (!categoryMap.TryGetValue(cat, out int idx))
                {
                    idx = categories.Count;
                    categoryMap[cat] = idx;
                    categories.Add((cat, new List<(PackageInfo, VersionInfo, VersionInfo)>()));
                }
                categories[idx].items.Add(item);
            }

            // Responsive card grid
            const float minCardWidth = 280f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 16) / minCardWidth));
            float cardWidth = (position.width - 16 - (columns - 1) * 4) / columns;

            foreach (var (categoryName, items) in categories)
            {
                // Category header
                EditorGUILayout.Space(6);
                GUILayout.Label(string.IsNullOrEmpty(categoryName) ? "Other" : categoryName, EditorStyles.boldLabel);
                DrawSeparator();
                EditorGUILayout.Space(4);

                // Card grid for this category
                for (int i = 0; i < items.Count; i += columns)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = 0; j < columns; j++)
                    {
                        if (i + j < items.Count)
                        {
                            var (pkg, release, dev) = items[i + j];
                            DrawPackageCard(pkg, release, dev, cardWidth);
                        }
                        else
                        {
                            GUILayout.Space(cardWidth);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var headerRect = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, _headerBg);

            var logoRect = new Rect(headerRect.x + 10, headerRect.y + 7, 28, 28);
            if (_logo != null)
                GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);

            var labelRect = new Rect(logoRect.xMax + 8, headerRect.y + 4, 200, 20);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            GUI.Label(labelRect, "Package Manager", headerStyle);

            // Tier badge
            if (_entitlements != null)
            {
                var tier = string.IsNullOrEmpty(_entitlements.Tier) ? "Free" : _entitlements.Tier;
                var tierRect = new Rect(labelRect.x, labelRect.yMax - 2, 100, 16);
                GUI.Label(tierRect, tier, _smallLabelStyle);
            }

            // Refresh button
            var buttonRect = new Rect(headerRect.xMax - 78, headerRect.y + 10, 68, 22);
            GUI.enabled = !_isLoading && PurrPackageManagerAuth.HasApiKey();
            if (GUI.Button(buttonRect, "Refresh"))
            {
                PurrPackageManagerCache.Invalidate();
                LoadData();
            }
            GUI.enabled = true;
        }

        private void DrawApiKeySection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUILayout.Label("API Key", EditorStyles.miniLabel, GUILayout.Width(44));
            _apiKeyInput = EditorGUILayout.PasswordField(_apiKeyInput, GUILayout.Height(20));

            if (string.IsNullOrEmpty(_apiKeyInput) && !PurrPackageManagerAuth.HasApiKey())
            {
                GUI.color = _accentColor;
                if (GUILayout.Button("Get API Key", GUILayout.Width(80), GUILayout.Height(20)))
                    Application.OpenURL("https://purrnet.dev/profile?tab=api-keys");
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("Save", GUILayout.Width(46), GUILayout.Height(20)))
                {
                    PurrPackageManagerAuth.SetApiKey(_apiKeyInput);
                    PurrPackageManagerCache.Invalidate();
                    LoadData();
                }
            }

            GUI.enabled = PurrPackageManagerAuth.HasApiKey();
            if (GUILayout.Button("Clear", GUILayout.Width(46), GUILayout.Height(20)))
            {
                _apiKeyInput = "";
                PurrPackageManagerAuth.ClearApiKey();
                PurrPackageManagerCache.Invalidate();
                _packages = null;
                _entitlements = null;
                _errorMessage = null;
            }
            GUI.enabled = true;

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        private void DrawPackageCard(PackageInfo package, VersionInfo release, VersionInfo dev, float width)
        {
            EditorGUILayout.BeginVertical(_cardStyle, GUILayout.Width(width));

            // Row 1: Name + status badges + remove X
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(package.DisplayName, _titleStyle);
            GUILayout.FlexibleSpace();

            var installedVersion = PurrPackageManagerInstaller.GetInstalledVersion(package);
            bool isInstalled = PurrPackageManagerInstaller.IsInstalled(package);

            bool hasUpdate = isInstalled && installedVersion != null
                            && !string.IsNullOrEmpty(package.LatestVersion)
                            && installedVersion != package.LatestVersion;

            if (package.Frozen)
            {
                DrawBadge("FROZEN", _frozenColor);
            }
            else if (hasUpdate)
            {
                DrawBadge("UPDATE", _updateColor);
                DrawBadge($"v{installedVersion}", _installedColor);
            }
            else if (installedVersion != null)
            {
                DrawBadge($"v{installedVersion}", _installedColor);
            }
            else if (!string.IsNullOrEmpty(package.LatestVersion))
            {
                DrawBadge($"v{package.LatestVersion}", _accentColor);
            }

            // Remove X button in the top-right corner
            if (isInstalled)
            {
                GUILayout.Space(4);
                GUI.color = _frozenColor;
                if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(18)))
                    PurrPackageManagerInstaller.Remove(package);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();

            // Description
            if (!string.IsNullOrEmpty(package.Description))
            {
                EditorGUILayout.Space(2);
                GUILayout.Label(package.Description, _descStyle);
            }

            // Info row
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (!string.IsNullOrEmpty(package.RequiredTier))
                GUILayout.Label($"Tier: {package.RequiredTier}", _smallLabelStyle);

            if (isInstalled && installedVersion != null)
                GUILayout.Label($"Installed: v{installedVersion}", _smallLabelStyle);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Frozen notice
            if (package.Frozen)
            {
                EditorGUILayout.Space(4);

                string frozenMsg = !string.IsNullOrEmpty(package.EntitledVersion)
                    ? $"Access limited to v{package.EntitledVersion} and below. Resubscribe to unlock v{package.LatestVersion}."
                    : "Your access to this package is limited. Resubscribe to unlock the latest versions.";
                EditorGUILayout.HelpBox(frozenMsg, MessageType.Warning);

                GUI.color = _accentColor;
                if (GUILayout.Button("Resubscribe", GUILayout.Height(22)))
                    Application.OpenURL("https://purrnet.dev");
                GUI.color = Color.white;
            }

            // No access
            if (release == null && dev == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("You don't have access to this package.", MessageType.Info);

                GUI.color = _accentColor;
                if (GUILayout.Button("Get Access", GUILayout.Height(22)))
                    Application.OpenURL("https://purrnet.dev/membership");
                GUI.color = Color.white;

                EditorGUILayout.EndVertical();
                return;
            }

            // Action buttons
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();

            bool releaseUpToDate = release != null && isInstalled && installedVersion == release.Version;
            bool devUpToDate = dev != null && isInstalled && installedVersion == dev.Version;

            if (release != null)
            {
                if (releaseUpToDate)
                {
                    GUI.enabled = false;
                    GUILayout.Button($"Release v{release.Version} (installed)", GUILayout.Height(24));
                    GUI.enabled = true;
                }
                else
                {
                    GUI.color = _installedColor;
                    if (GUILayout.Button(isInstalled ? $"Switch to Release v{release.Version}" : $"Install Release v{release.Version}", GUILayout.Height(24)))
                        InstallPackage(package, release);
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("No Release", GUILayout.Height(24));
                GUI.enabled = true;
            }

            if (dev != null)
            {
                if (devUpToDate)
                {
                    GUI.enabled = false;
                    GUILayout.Button($"Dev v{dev.Version} (installed)", GUILayout.Height(24));
                    GUI.enabled = true;
                }
                else
                {
                    GUI.color = _accentColor;
                    if (GUILayout.Button(isInstalled ? $"Switch to Dev v{dev.Version}" : $"Install Dev v{dev.Version}", GUILayout.Height(24)))
                        InstallPackage(package, dev);
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("No Dev", GUILayout.Height(24));
                GUI.enabled = true;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawBadge(string text, Color color)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(text), _badgeStyle);
            rect.height = 18;
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.2f));
            var prevColor = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, _badgeStyle);
            GUI.color = prevColor;
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, _separatorColor);
        }

        private static void DrawCenteredLabel(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawCenteredMessage(string text, MessageType type)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(20);
            EditorGUILayout.HelpBox(text, type);
            GUILayout.Space(20);
            EditorGUILayout.EndHorizontal();
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
