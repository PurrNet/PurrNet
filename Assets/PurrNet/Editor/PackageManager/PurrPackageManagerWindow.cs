using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public class PurrPackageManagerWindow : EditorWindow
    {
        private string _apiKeyInput = "";
        private string _errorMessage;
        private bool _isLoading;

        private int _selectedIndex = -1;
        private Vector2 _listScrollPosition;
        private Vector2 _detailScrollPosition;

        private float _splitWidth = 240f;
        private bool _isDraggingSplitter;
        private Rect _cachedSplitterRect;
        private int _prevKeyboardControl;
        private int _releasePopupIndex;
        private int _devPopupIndex;

        private PackagesResponse _packages;
        private EntitlementsResponse _entitlements;

        // Cached sorted list rebuilt each frame from _packages
        private readonly List<(PackageInfo pkg, VersionInfo release, VersionInfo dev)> _sortedPackages = new();
        private readonly List<(string name, int startIndex, int count)> _categories = new();

        private static readonly Color _headerBg = new Color(0.17f, 0.17f, 0.17f, 1f);
        private static readonly Color _accentColor = new Color(0.4f, 0.7f, 1f, 1f);
        private static readonly Color _installedColor = new Color(0.4f, 0.8f, 0.4f, 1f);
        private static readonly Color _updateColor = new Color(1f, 0.76f, 0.28f, 1f);
        private static readonly Color _frozenColor = new Color(0.95f, 0.5f, 0.5f, 1f);
        private static readonly Color _separatorColor = new Color(0.13f, 0.13f, 0.13f, 1f);
        private static readonly Color _listBg = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color _selectedBg = new Color(0.17f, 0.36f, 0.53f, 1f);
        private static readonly Color _hoverBg = new Color(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color _noAccessColor = new Color(0.95f, 0.45f, 0.45f, 1f);
        private static readonly Color _categoryBg = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color _selectedAccent = new Color(0.35f, 0.65f, 0.95f, 1f);

        private GUIStyle _titleStyle;
        private GUIStyle _descStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _smallLabelStyle;
        private GUIStyle _listItemStyle;
        private GUIStyle _listItemDetailStyle;
        private GUIStyle _categoryStyle;
        private GUIStyle _detailTitleStyle;
        private GUIStyle _releaseNotesStyle;
        private Texture2D _logo;

        private const float SplitMargin = 80f;
        private const float ListItemHeight = 28f;
        private const float CategoryHeaderHeight = 20f;
        private const float CategoryGap = 8f;
        private const float SplitterWidth = 6f;

        [MenuItem("Tools/PurrNet/Package Manager", false, -99)]
        public static void ShowWindow()
        {
            var window = GetWindow<PurrPackageManagerWindow>("PurrNet Package Manager");
            window.minSize = new Vector2(520, 350);
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            _logo = Resources.Load<Texture2D>("purrlogo");
            _apiKeyInput = PurrPackageManagerAuth.GetApiKey();
            LoadData();
        }

        private void InitStyles()
        {
            if (_detailTitleStyle != null && _listItemDetailStyle != null && _releaseNotesStyle != null)
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
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f, 1f) }
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
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };

            _listItemStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                padding = new RectOffset(12, 4, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleLeft
            };

            _listItemDetailStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                padding = new RectOffset(0, 6, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
            };

            _categoryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(10, 4, 3, 3),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.48f, 0.48f, 0.48f, 1f) }
            };

            _detailTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                margin = new RectOffset(0, 0, 0, 4)
            };

            var notesColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            _releaseNotesStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 11,
                richText = true,
                normal = { textColor = notesColor },
                focused = { textColor = notesColor },
                onFocused = { textColor = notesColor }
            };
        }

        private void RebuildSortedPackages()
        {
            _sortedPackages.Clear();
            _categories.Clear();

            if (_packages?.Packages == null)
                return;

            foreach (var package in _packages.Packages)
                _sortedPackages.Add((package, FindLatestByChannel(package, "release"), FindLatestByChannel(package, "dev")));

            _sortedPackages.Sort((a, b) => a.pkg.DisplayOrder.CompareTo(b.pkg.DisplayOrder));

            // Build category index
            var categoryMap = new Dictionary<string, int>();
            foreach (var item in _sortedPackages)
            {
                var cat = item.pkg.Category ?? "";
                if (!categoryMap.ContainsKey(cat))
                {
                    categoryMap[cat] = _categories.Count;
                    _categories.Add((cat, 0, 0));
                }
            }

            // Compute start index and count per category
            int idx = 0;
            foreach (var item in _sortedPackages)
            {
                var cat = item.pkg.Category ?? "";
                int ci = categoryMap[cat];
                var c = _categories[ci];
                if (c.count == 0)
                    _categories[ci] = (c.name, idx, 1);
                else
                    _categories[ci] = (c.name, c.startIndex, c.count + 1);
                idx++;
            }
        }

        private void OnGUI()
        {
            InitStyles();

            // Handle splitter drag FIRST, before any GUILayout controls can consume events
            HandleSplitterDrag(_cachedSplitterRect);

            DrawHeader();
            DrawSeparator();

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

            RebuildSortedPackages();

            // Clamp selection
            if (_selectedIndex >= _sortedPackages.Count)
                _selectedIndex = _sortedPackages.Count - 1;

            // Auto-select first if nothing selected
            if (_selectedIndex < 0 && _sortedPackages.Count > 0)
                _selectedIndex = 0;

            _splitWidth = Mathf.Clamp(_splitWidth, SplitMargin, position.width - SplitMargin);

            // Split view: left placeholder + splitter space + right detail (EditorGUILayout)
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // Left panel: reserve space for the list (drawn with immediate-mode later)
            EditorGUILayout.BeginVertical(GUILayout.Width(_splitWidth));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            var listRect = GUILayoutUtility.GetLastRect();

            // Splitter space
            GUILayout.Space(SplitterWidth);

            // Right panel: detail using EditorGUILayout (gets proper Layout pass)
            DrawPackageDetail();

            EditorGUILayout.EndHorizontal();

            // Overlay immediate-mode list and splitter, using exact positions to avoid layout padding gaps
            if (Event.current.type != EventType.Layout)
            {
                var fullListRect = new Rect(0, listRect.y, _splitWidth, listRect.height);
                _cachedSplitterRect = new Rect(_splitWidth, listRect.y, SplitterWidth, listRect.height);
                DrawPackageList(fullListRect);
                DrawSplitter(_cachedSplitterRect);
            }

            _prevKeyboardControl = GUIUtility.keyboardControl;
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
            GUI.enabled = !_isLoading;
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
                _entitlements = null;
                _errorMessage = null;
                LoadData();
            }
            GUI.enabled = true;

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        private void DrawPackageList(Rect areaRect)
        {
            EditorGUI.DrawRect(areaRect, _listBg);

            // Calculate total content height
            float totalHeight = 0;
            for (int c = 0; c < _categories.Count; c++)
            {
                if (c > 0) totalHeight += CategoryGap;
                totalHeight += CategoryHeaderHeight + _categories[c].count * ListItemHeight;
            }

            bool needsScroll = totalHeight > areaRect.height;
            var viewRect = new Rect(0, 0, areaRect.width - (needsScroll ? 13f : 0f), totalHeight);
            _listScrollPosition = GUI.BeginScrollView(areaRect, _listScrollPosition, viewRect);

            float y = 0;
            bool firstCategory = true;
            foreach (var (categoryName, startIndex, count) in _categories)
            {
                // Gap between categories
                if (!firstCategory)
                    y += CategoryGap;
                firstCategory = false;

                // Category header — non-interactive delimiter
                var catLabel = string.IsNullOrEmpty(categoryName) ? "Other" : categoryName;
                var catRect = new Rect(0, y, viewRect.width, CategoryHeaderHeight);

                EditorGUI.DrawRect(catRect, _categoryBg);
                GUI.Label(catRect, catLabel.ToUpperInvariant(), _categoryStyle);
                y += CategoryHeaderHeight;

                // Package items in this category
                for (int i = startIndex; i < startIndex + count; i++)
                {
                    var itemRect = new Rect(0, y, viewRect.width, ListItemHeight);
                    DrawListItem(_sortedPackages[i].pkg, i, itemRect);
                    y += ListItemHeight;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawListItem(PackageInfo package, int index, Rect itemRect)
        {
            bool isSelected = index == _selectedIndex;
            bool isInstalled = PurrPackageManagerInstaller.IsInstalled(package);
            var installedVersion = isInstalled ? PurrPackageManagerInstaller.GetInstalledVersion(package) : null;
            bool hasUpdate = isInstalled && installedVersion != null
                             && !string.IsNullOrEmpty(package.LatestVersion)
                             && installedVersion != package.LatestVersion;

            // Hover detection
            bool isHover = itemRect.Contains(Event.current.mousePosition);

            // Background
            if (isSelected)
            {
                EditorGUI.DrawRect(itemRect, _selectedBg);
                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 3, itemRect.height), _selectedAccent);
            }
            else if (isHover)
            {
                EditorGUI.DrawRect(itemRect, _hoverBg);
            }

            // External update detection — compare lock file hash against both channel commits
            bool hasExternalUpdate = false;
            if (package.IsExternal && isInstalled)
            {
                var hash = PurrPackageManagerInstaller.GetInstalledCommitHash(package);
                if (hash != null)
                    hasExternalUpdate = hash != package.LatestCommitRelease && hash != package.LatestCommitDev;
            }

            // Build right-side info text
            string info;
            if (!package.HasAccess)
                info = "No access";
            else if (package.IsExternal)
                info = hasExternalUpdate ? "update" : isInstalled ? "installed" : "";
            else if (hasUpdate)
                info = $"v{installedVersion} \u2192 v{package.LatestVersion}";
            else if (isInstalled && installedVersion != null)
                info = $"v{installedVersion}";
            else if (!string.IsNullOrEmpty(package.LatestVersion))
                info = $"v{package.LatestVersion}";
            else
                info = "";

            // Measure right-side text width
            float infoWidth = string.IsNullOrEmpty(info) ? 0 : _listItemDetailStyle.CalcSize(new GUIContent(info)).x + 4;

            // Status dot
            bool showDot = hasUpdate || hasExternalUpdate || isInstalled;
            float dotSpace = showDot ? 12 : 0;

            // Name (left) — drawn as pure text, no event handling
            float nameWidth = itemRect.width - infoWidth - dotSpace;
            var nameRect = new Rect(itemRect.x, itemRect.y, nameWidth, itemRect.height);
            if (Event.current.type == EventType.Repaint)
                _listItemStyle.Draw(nameRect, package.DisplayName, false, false, false, false);

            // Status dot (between name and info)
            if (showDot)
            {
                var dotColor = (hasUpdate || hasExternalUpdate) ? _updateColor : _installedColor;
                float dotX = nameRect.xMax + 2;
                var dotRect = new Rect(dotX, itemRect.y + (itemRect.height - 6) / 2, 6, 6);
                EditorGUI.DrawRect(dotRect, dotColor);
            }

            // Info text (right-aligned) — drawn as pure text, no event handling
            if (infoWidth > 0 && Event.current.type == EventType.Repaint)
            {
                var infoRect = new Rect(itemRect.xMax - infoWidth, itemRect.y, infoWidth, itemRect.height);
                if (!package.HasAccess)
                {
                    var noAccessStyle = new GUIStyle(_listItemDetailStyle)
                    {
                        fontSize = 10,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = _noAccessColor }
                    };
                    noAccessStyle.Draw(infoRect, info, false, false, false, false);
                }
                else
                {
                    _listItemDetailStyle.Draw(infoRect, info, false, false, false, false);
                }
            }

            // Click to select — at the end so nothing above can interfere
            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
            {
                _selectedIndex = index;
                _releasePopupIndex = -1;
                _devPopupIndex = -1;
                GUI.FocusControl(null);
                Event.current.Use();
                Repaint();
            }

            // Repaint on hover for highlight
            if (isHover && Event.current.type == EventType.Repaint)
                Repaint();
        }

        private void DrawPackageDetail()
        {
            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_selectedIndex < 0 || _selectedIndex >= _sortedPackages.Count)
            {
                EditorGUILayout.Space(40);
                DrawCenteredLabel("Select a package to view details.");
                EditorGUILayout.EndScrollView();
                return;
            }

            var (package, release, dev) = _sortedPackages[_selectedIndex];
            var installedVersion = PurrPackageManagerInstaller.GetInstalledVersion(package);
            bool isInstalled = PurrPackageManagerInstaller.IsInstalled(package);
            bool hasUpdate = isInstalled && installedVersion != null
                             && !string.IsNullOrEmpty(package.LatestVersion)
                             && installedVersion != package.LatestVersion;

            // External update detection — compare lock file hash against both channel commits
            string externalInstalledHash = null;
            bool hasExternalUpdate = false;
            if (package.IsExternal && isInstalled)
            {
                externalInstalledHash = PurrPackageManagerInstaller.GetInstalledCommitHash(package);
                if (externalInstalledHash != null)
                {
                    bool matchesRelease = externalInstalledHash == package.LatestCommitRelease;
                    bool matchesDev = externalInstalledHash == package.LatestCommitDev;
                    hasExternalUpdate = !matchesRelease && !matchesDev;
                }
            }

            EditorGUILayout.Space(8);
            GUILayout.Space(4);

            // Title row: name + badges
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(package.DisplayName, _detailTitleStyle);
            GUILayout.FlexibleSpace();

            if (package.IsExternal)
            {
                if (hasExternalUpdate)
                {
                    DrawBadge("UPDATE", _updateColor);
                    DrawBadge("INSTALLED", _installedColor);
                }
                else if (isInstalled)
                {
                    DrawBadge("INSTALLED", _installedColor);
                }
            }
            else if (package.Frozen)
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

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Description
            if (!string.IsNullOrEmpty(package.Description))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                EditorGUILayout.LabelField(package.Description, _descStyle);
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }

            // Info section
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            if (!string.IsNullOrEmpty(package.RequiredTier))
                GUILayout.Label($"Tier: {package.RequiredTier}", _smallLabelStyle);

            if (package.IsExternal)
            {
                if (isInstalled)
                {
                    GUILayout.Label("Installed via git", _smallLabelStyle);
                    if (externalInstalledHash != null)
                        GUILayout.Label($"Commit: {externalInstalledHash.Substring(0, Math.Min(8, externalInstalledHash.Length))}", _smallLabelStyle);
                    if (hasExternalUpdate)
                        GUILayout.Label("Update available", _smallLabelStyle);
                }
            }
            else
            {
                if (isInstalled && installedVersion != null)
                    GUILayout.Label($"Installed: v{installedVersion}", _smallLabelStyle);

                if (!string.IsNullOrEmpty(package.LatestVersion))
                    GUILayout.Label($"Latest: v{package.LatestVersion}", _smallLabelStyle);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Frozen notice (non-external only)
            if (!package.IsExternal && package.Frozen)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                EditorGUILayout.BeginVertical();

                string frozenMsg = !string.IsNullOrEmpty(package.EntitledVersion)
                    ? $"Access limited to v{package.EntitledVersion} and below. Resubscribe to unlock v{package.LatestVersion}."
                    : "Your access to this package is limited. Resubscribe to unlock the latest versions.";
                EditorGUILayout.HelpBox(frozenMsg, MessageType.Warning);

                GUI.color = _accentColor;
                if (GUILayout.Button("Resubscribe", GUILayout.Height(24)))
                    Application.OpenURL("https://purrnet.dev");
                GUI.color = Color.white;

                EditorGUILayout.EndVertical();
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }

            // No access
            if (!package.HasAccess)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                GUI.color = _accentColor;
                if (GUILayout.Button("Get Access", GUILayout.Height(28)))
                    Application.OpenURL("https://purrnet.dev/membership");
                GUI.color = Color.white;
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();

                if (isInstalled)
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    GUI.color = _frozenColor;
                    if (GUILayout.Button("Remove Package", GUILayout.Height(24)))
                        PurrPackageManagerInstaller.Remove(package);
                    GUI.color = Color.white;
                    GUILayout.Space(8);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            // Action buttons
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            if (package.IsExternal)
            {
                // External packages: git URL install buttons
                EditorGUILayout.BeginHorizontal();
                DrawExternalInstallButton(package, "Release", package.GitInstallUrlRelease,
                    isInstalled, externalInstalledHash, package.LatestCommitRelease, _installedColor);
                GUILayout.Space(4);
                DrawExternalInstallButton(package, "Dev", package.GitInstallUrlDev,
                    isInstalled, externalInstalledHash, package.LatestCommitDev, _accentColor);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                DrawInstallButton(package, release, "Release", isInstalled, installedVersion, _installedColor);
                GUILayout.Space(4);
                DrawInstallButton(package, dev, "Dev", isInstalled, installedVersion, _accentColor);
                EditorGUILayout.EndHorizontal();

                // Version history dropdowns — split by channel, capped at 20 each
                if (package.Versions != null && package.Versions.Length > 0)
                {
                    var releaseVersions = new List<VersionInfo>();
                    var devVersions = new List<VersionInfo>();

                    foreach (var v in package.Versions)
                    {
                        if (string.Equals(v.Channel, "release", StringComparison.OrdinalIgnoreCase))
                        {
                            if (releaseVersions.Count < 20) releaseVersions.Add(v);
                        }
                        else
                        {
                            if (devVersions.Count < 20) devVersions.Add(v);
                        }
                    }

                    EditorGUILayout.Space(8);
                    DrawVersionDropdown("Release", releaseVersions, ref _releasePopupIndex,
                        isInstalled, installedVersion, package, _installedColor);
                    EditorGUILayout.Space(4);
                    DrawVersionDropdown("Dev", devVersions, ref _devPopupIndex,
                        isInstalled, installedVersion, package, _accentColor);
                }
            }

            // Remove button
            if (isInstalled)
            {
                EditorGUILayout.Space(8);
                GUI.color = _frozenColor;
                if (GUILayout.Button("Remove Package", GUILayout.Height(24)))
                    PurrPackageManagerInstaller.Remove(package);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Changelog (non-external only)
            if (!package.IsExternal && package.Versions != null && package.Versions.Length > 0)
            {
                // Collect relevant versions:
                // - Not installed: just the latest version
                // - Installed: only versions newer than the installed one
                var relevantVersions = new List<VersionInfo>();
                if (!isInstalled)
                {
                    // Show the latest version that has release notes
                    foreach (var v in package.Versions)
                    {
                        if (!string.IsNullOrEmpty(v.ReleaseNotes))
                        {
                            relevantVersions.Add(v);
                            break;
                        }
                    }
                }
                else
                {
                    // Versions array is newest-first; collect until we hit the installed version
                    foreach (var v in package.Versions)
                    {
                        if (v.Version == installedVersion)
                            break;
                        if (!string.IsNullOrEmpty(v.ReleaseNotes))
                            relevantVersions.Add(v);
                    }
                }

                if (relevantVersions.Count > 0)
                {
                    EditorGUILayout.Space(12);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    EditorGUILayout.BeginVertical();
                    DrawSeparator();
                    EditorGUILayout.Space(4);

                    var title = isInstalled && hasUpdate
                        ? $"What's New ({relevantVersions.Count} update{(relevantVersions.Count > 1 ? "s" : "")})"
                        : "Release Notes";
                    GUILayout.Label(title, _detailTitleStyle);
                    EditorGUILayout.Space(4);

                    foreach (var v in relevantVersions)
                    {
                        DrawReleaseNotesText(v.ReleaseNotes);
                        EditorGUILayout.Space(8);
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(8);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        private void DrawVersionDropdown(string channelLabel, List<VersionInfo> versions,
            ref int popupIndex, bool isInstalled, string installedVersion, PackageInfo package, Color color)
        {
            if (versions.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(channelLabel, EditorStyles.miniLabel, GUILayout.Width(48));

            var labels = new string[versions.Count];
            for (int i = 0; i < versions.Count; i++)
            {
                labels[i] = "v" + versions[i].Version;
                if (isInstalled && installedVersion == versions[i].Version)
                    labels[i] += " (installed)";
            }

            // Default to the installed version if uninitialized
            if (popupIndex < 0 && isInstalled && installedVersion != null)
            {
                for (int i = 0; i < versions.Count; i++)
                {
                    if (versions[i].Version == installedVersion)
                    {
                        popupIndex = i;
                        break;
                    }
                }
            }
            popupIndex = Mathf.Clamp(popupIndex, 0, labels.Length - 1);
            popupIndex = EditorGUILayout.Popup(popupIndex, labels, GUILayout.Height(20));

            var selected = versions[popupIndex];
            bool isSelectedInstalled = isInstalled && installedVersion == selected.Version;

            GUI.enabled = !isSelectedInstalled;
            GUI.color = color;
            if (GUILayout.Button(isSelectedInstalled ? "Installed" : "Install", GUILayout.Width(66), GUILayout.Height(20)))
                InstallPackage(package, selected);
            GUI.color = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInstallButton(PackageInfo package, VersionInfo version, string channelLabel,
            bool isInstalled, string installedVersion, Color buttonColor)
        {
            if (version == null)
            {
                GUI.enabled = false;
                GUILayout.Button($"No {channelLabel} Version", GUILayout.Height(26));
                GUI.enabled = true;
                return;
            }

            bool upToDate = isInstalled && installedVersion == version.Version;

            if (upToDate)
            {
                GUI.enabled = false;
                GUILayout.Button($"{channelLabel} v{version.Version} (installed)", GUILayout.Height(26));
                GUI.enabled = true;
            }
            else
            {
                GUI.color = buttonColor;
                string label;
                if (!isInstalled)
                    label = $"Install {channelLabel} v{version.Version}";
                else if (IsInstalledOnChannel(package, version.Channel, installedVersion))
                    label = $"Update to {channelLabel} v{version.Version}";
                else
                    label = $"Switch to {channelLabel} v{version.Version}";
                if (GUILayout.Button(label, GUILayout.Height(26)))
                    InstallPackage(package, version);
                GUI.color = Color.white;
            }
        }

        private void DrawExternalInstallButton(PackageInfo package, string channelLabel, string gitUrl,
            bool isInstalled, string installedHash, string latestCommit, Color buttonColor)
        {
            if (string.IsNullOrEmpty(gitUrl))
            {
                GUI.enabled = false;
                GUILayout.Button($"No {channelLabel} Version", GUILayout.Height(26));
                GUI.enabled = true;
                return;
            }

            if (!isInstalled)
            {
                GUI.color = buttonColor;
                if (GUILayout.Button($"Install {channelLabel}", GUILayout.Height(26)))
                    PurrPackageManagerInstaller.InstallExternal(package, gitUrl);
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(latestCommit)
                && !string.IsNullOrEmpty(installedHash)
                && installedHash == latestCommit)
            {
                GUI.enabled = false;
                GUILayout.Button($"{channelLabel} (up to date)", GUILayout.Height(26));
                GUI.enabled = true;
            }
            else
            {
                GUI.color = buttonColor;
                if (GUILayout.Button($"Install {channelLabel}", GUILayout.Height(26)))
                    PurrPackageManagerInstaller.InstallExternal(package, gitUrl);
                GUI.color = Color.white;
            }
        }

        private void DrawReleaseNotesText(string notes)
        {
            var rendered = MarkdownToRichText(notes);
            var content = new GUIContent(rendered);
            var width = EditorGUIUtility.currentViewWidth - 40;
            var height = _releaseNotesStyle.CalcHeight(content, width);
            var rect = GUILayoutUtility.GetRect(content, _releaseNotesStyle, GUILayout.Height(height));
            EditorGUI.SelectableLabel(rect, rendered, _releaseNotesStyle);

            // Clear the select-all that happens on first focus
            int kb = GUIUtility.keyboardControl;
            if (kb != 0 && kb != _prevKeyboardControl)
            {
                var te = GUIUtility.GetStateObject(typeof(TextEditor), kb) as TextEditor;
                if (te != null)
                    te.selectIndex = te.cursorIndex;
            }
        }

        private void DrawBadge(string text, Color color)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(text), _badgeStyle);
            rect.height = 18;
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.25f));
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

        private void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, _separatorColor);

            // Draw grip dots in the center to hint it's draggable
            float centerX = rect.x + rect.width / 2f;
            float centerY = rect.y + rect.height / 2f;
            var dotColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            for (int i = -2; i <= 2; i++)
            {
                var dotRect = new Rect(centerX - 1, centerY + i * 5, 2, 2);
                EditorGUI.DrawRect(dotRect, dotColor);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
        }

        private void HandleSplitterDrag(Rect splitterRect)
        {
            if (splitterRect.width < 1)
                return;

            var evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(evt.mousePosition))
                    {
                        _isDraggingSplitter = true;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDraggingSplitter)
                    {
                        _splitWidth = evt.mousePosition.x;
                        _splitWidth = Mathf.Clamp(_splitWidth, SplitMargin, position.width - SplitMargin);
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDraggingSplitter)
                    {
                        _isDraggingSplitter = false;
                        evt.Use();
                    }
                    break;
            }
        }

        private static void DrawCenteredLabel(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static bool IsInstalledOnChannel(PackageInfo package, string channel, string installedVersion)
        {
            if (package.Versions == null || installedVersion == null)
                return false;

            foreach (var v in package.Versions)
            {
                if (v.Version == installedVersion)
                    return string.Equals(v.Channel, channel, StringComparison.OrdinalIgnoreCase);
            }

            return false;
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

        private static string MarkdownToRichText(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            var sb = new StringBuilder();
            var lines = markdown.Split('\n');
            bool lastWasBlank = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Collapse consecutive blank lines into one
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (lastWasBlank) continue;
                    lastWasBlank = true;
                    sb.AppendLine();
                    continue;
                }
                lastWasBlank = false;

                // Headers
                if (line.StartsWith("### "))
                {
                    sb.AppendLine($"<b>{ProcessInline(line.Substring(4))}</b>");
                    continue;
                }
                if (line.StartsWith("## "))
                {
                    sb.AppendLine($"<size=13><b>{ProcessInline(line.Substring(3))}</b></size>");
                    continue;
                }
                if (line.StartsWith("# "))
                {
                    sb.AppendLine($"<size=15><b>{ProcessInline(line.Substring(2))}</b></size>");
                    continue;
                }

                // Unordered list items
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    sb.AppendLine($"  \u2022 {ProcessInline(line.Substring(2))}");
                    continue;
                }

                // Horizontal rules
                var trimmed = line.Trim();
                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    sb.AppendLine("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
                    continue;
                }

                sb.AppendLine(ProcessInline(line));
            }

            return sb.ToString().TrimEnd();
        }

        private static string ProcessInline(string text)
        {
            // Links [text](url) → colored text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "<color=#66aaff>$1</color>");

            // Inline code `text`
            text = Regex.Replace(text, @"`([^`]+)`", "<color=#88cccc>$1</color>");

            // Bold **text** or __text__
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            text = Regex.Replace(text, @"__(.+?)__", "<b>$1</b>");

            // Italic *text* or _text_
            text = Regex.Replace(text, @"(?<!\*)\*(.+?)\*(?!\*)", "<i>$1</i>");
            text = Regex.Replace(text, @"(?<!_)_(.+?)_(?!_)", "<i>$1</i>");

            return text;
        }

        private async void LoadData()
        {
            _isLoading = true;
            _errorMessage = null;
            Repaint();

            try
            {
                var apiKey = PurrPackageManagerAuth.GetApiKey();
                bool hasKey = !string.IsNullOrEmpty(apiKey);

                if (hasKey)
                {
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
                    }
                }
                else
                {
                    _entitlements = null;
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

                _releasePopupIndex = -1;
                _devPopupIndex = -1;
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
