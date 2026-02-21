using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PurrNet;
using PurrNet.Packing;

namespace PurrNet.DeltaPackerAnalysis.Editor
{
    /// <summary>
    /// Editor window for analyzing DeltaPacker bit usage and performance.
    /// Use this to compare how many bits different data types take (full vs delta unchanged vs delta changed)
    /// and to benchmark write/read times. Save snapshots under DeltaSnapshots and select them to compare.
    /// </summary>
    public class DeltaPackerAnalysisWindow : EditorWindow
    {
        private const int BenchmarkIterations = 500_000;
        private const float BarWidth = 12f;
        private const float LabelWidth = 80f;
        private const float GraphHeight = 220f;
        private const string PrefKeyGraphHeight = "PurrNet_DeltaPackerAnalysis_GraphHeight";
        private const string SnapshotsFolderName = "DeltaSnapshots";
        private const string SnapshotFilePrefix = "DeltaSnapshot_";

        private readonly List<TypeEntry> _typeEntries = new List<TypeEntry>();
        private readonly List<TypeResult> _results = new List<TypeResult>();
        private Vector2 _scrollPosition;
        private Vector2 _graphScrollPosition;
        private Vector2 _snapshotListScroll;
        private float _graphHeight = GraphHeight;
        private bool _hasRunAnalysis;
        private string _errorMessage;
        private int _selectedResultIndex = -1;

        // Snapshots: folder under Assets/PurrNet/Tools/DeltaPackerAnalysis/DeltaSnapshots
        private string _snapshotsFolder;
        private readonly List<SnapshotEntry> _snapshotEntries = new List<SnapshotEntry>();
        private readonly List<int> _snapshotSelection = new List<int>();
        private DeltaPackerSnapshotData _viewSnapshot;
        private DeltaPackerSnapshotData _compareSnapshotA;
        private DeltaPackerSnapshotData _compareSnapshotB;
        private bool _showCompareView;

        private struct SnapshotEntry
        {
            public string FilePath;
            public string DisplayName;
            public string FileDate;
        }

        [Serializable]
        private struct TypeEntry
        {
            public Type Type;
            public string DisplayName;
            public object OldValue;
            public object NewValue;
        }

        [Serializable]
        private struct TypeResult
        {
            public string TypeName;
            public int BitsFull;
            public int BitsDeltaUnchanged;
            public int BitsDeltaChanged;
            public double WriteTimeMicroseconds;
            public double ReadTimeMicroseconds;
            public string Error;
        }

        [MenuItem("Tools/PurrNet/Analysis/DeltaPacker Analysis")]
        public static void ShowWindow()
        {
            var window = GetWindow<DeltaPackerAnalysisWindow>("DeltaPacker Analysis");
            window.minSize = new Vector2(420, 400);
        }

        private void OnEnable()
        {
            _graphHeight = EditorPrefs.GetFloat(PrefKeyGraphHeight, GraphHeight);
            BuildTypeEntries();
            _snapshotsFolder = GetSnapshotsFolder();
            RefreshSnapshotList();
        }

        private static string GetSnapshotsFolder()
        {
            string path = Path.Combine(Application.dataPath, "PurrNet", "Tools", "DeltaPackerAnalysis", SnapshotsFolderName);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        private void RefreshSnapshotList()
        {
            _snapshotEntries.Clear();
            if (!Directory.Exists(_snapshotsFolder)) return;
            var files = Directory.GetFiles(_snapshotsFolder, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string date = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm");
                if (fileName.StartsWith(SnapshotFilePrefix))
                    fileName = fileName.Substring(SnapshotFilePrefix.Length).Replace('_', ' ');
                _snapshotEntries.Add(new SnapshotEntry
                {
                    FilePath = filePath,
                    DisplayName = fileName,
                    FileDate = date
                });
            }
        }

        private void OnDisable()
        {
            EditorPrefs.SetFloat(PrefKeyGraphHeight, _graphHeight);
        }

        private void BuildTypeEntries()
        {
            _typeEntries.Clear();

            void Add<T>(string name, T oldVal, T newVal)
            {
                _typeEntries.Add(new TypeEntry
                {
                    Type = typeof(T),
                    DisplayName = name,
                    OldValue = oldVal,
                    NewValue = newVal
                });
            }

            // Primitives
            Add("bool", false, true);
            Add("byte", (byte)0, (byte)42);
            Add("sbyte", (sbyte)0, (sbyte)-10);
            Add("short", (short)0, (short)1000);
            Add("ushort", (ushort)0, (ushort)1000);
            Add("int", 0, 42);
            Add("uint", 0u, 42u);
            Add("long", 0L, 12345L);
            Add("ulong", 0UL, 12345UL);
            Add("float", 0f, 1f);
            Add("double", 0.0, 1.0);

            // Packed types
            Add("PackedUInt", new PackedUInt(0), new PackedUInt(42));
            Add("PackedLong", new PackedLong(0), new PackedLong(12345));

            // Unity types
            Add("Vector2", Vector2.zero, Vector2.one);
            Add("Vector2Int", Vector2Int.zero, new Vector2Int(1, 1));
            Add("Vector3", Vector3.zero, Vector3.one);
            Add("Vector3Int", Vector3Int.zero, new Vector3Int(1, 1, 1));
            Add("Vector4", Vector4.zero, Vector4.one);
            Add("Quaternion", Quaternion.identity, Quaternion.Euler(45f, 0f, 0f));

            // Complex struct (IPackedAuto): big struct, 0 / 1 / all fields changed
            Add("BS 0 ch", BigStructSample.Default, BigStructSample.Default);
            Add("BS 1 fld", BigStructSample.Default, BigStructSample.OneFieldChanged);
            Add("BS all", BigStructSample.Default, BigStructSample.AllFieldsChanged);
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("DeltaPacker Analysis", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Compare bits used by different types: Full (Packer), Delta unchanged (1 bit), Delta changed. " +
                "Save snapshots under DeltaSnapshots, then select one to View or two to Compare.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Analysis", GUILayout.Width(120)))
            {
                RunAnalysis();
            }
            if (GUILayout.Button("Save Snapshot", GUILayout.Width(110)))
            {
                SaveSnapshot();
            }
            if (GUILayout.Button("Export CSV", GUILayout.Width(90)))
            {
                ExportCsv();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            }

            // Snapshots panel
            DrawSnapshotsPanel();

            EditorGUILayout.Space(8);

            // Content: current run, single snapshot view, or comparison
            bool hasCurrentRun = _hasRunAnalysis && _results.Count > 0;
            bool hasViewSnapshot = _viewSnapshot != null && _viewSnapshot.results != null && _viewSnapshot.results.Length > 0;
            bool hasCompare = _showCompareView && _compareSnapshotA != null && _compareSnapshotB != null;

            if (hasCompare)
            {
                DrawCompareView();
            }
            else if (hasViewSnapshot)
            {
                DrawSnapshotView(_viewSnapshot);
            }
            else if (hasCurrentRun)
            {
                DrawGraphHeightSlider();
                DrawBitsGraph(ResultsToData(_results));
                EditorGUILayout.Space(12);
                DrawBenchmarkTable(ResultsToData(_results));
            }
            else
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Run Analysis to measure bits and benchmarks, or select a snapshot to View / Compare.");
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSnapshotsPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Snapshots", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
                RefreshSnapshotList();
            EditorGUILayout.LabelField($"Saved under: {_snapshotsFolder}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (_snapshotEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No snapshots yet. Run Analysis then Save Snapshot.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _snapshotListScroll = EditorGUILayout.BeginScrollView(_snapshotListScroll, GUILayout.MaxHeight(120));
            for (int i = 0; i < _snapshotEntries.Count; i++)
            {
                var entry = _snapshotEntries[i];
                bool selected = _snapshotSelection.Contains(i);
                EditorGUILayout.BeginHorizontal();
                bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(18));
                if (newSelected != selected)
                {
                    if (newSelected)
                    {
                        _snapshotSelection.Add(i);
                        if (_snapshotSelection.Count > 2)
                            _snapshotSelection.RemoveAt(0);
                    }
                    else
                        _snapshotSelection.Remove(i);
                }
                if (GUILayout.Button($"{entry.DisplayName}  ({entry.FileDate})", EditorStyles.label))
                {
                    int idx = _snapshotSelection.IndexOf(i);
                    if (idx >= 0) _snapshotSelection.RemoveAt(idx);
                    else
                    {
                        _snapshotSelection.Add(i);
                        if (_snapshotSelection.Count > 2)
                            _snapshotSelection.RemoveAt(0);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (_snapshotSelection.Count == 1 && GUILayout.Button("View", GUILayout.Width(70)))
            {
                _viewSnapshot = LoadSnapshot(_snapshotEntries[_snapshotSelection[0]].FilePath);
                _showCompareView = false;
                _compareSnapshotA = _compareSnapshotB = null;
            }
            if (_snapshotSelection.Count == 2 && GUILayout.Button("Compare", GUILayout.Width(70)))
            {
                _compareSnapshotA = LoadSnapshot(_snapshotEntries[_snapshotSelection[0]].FilePath);
                _compareSnapshotB = LoadSnapshot(_snapshotEntries[_snapshotSelection[1]].FilePath);
                _viewSnapshot = null;
                _showCompareView = true;
            }
            if (GUILayout.Button("Clear view", GUILayout.Width(80)))
            {
                _viewSnapshot = null;
                _showCompareView = false;
                _compareSnapshotA = _compareSnapshotB = null;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawGraphHeightSlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Graph height");
            _graphHeight = EditorGUILayout.Slider(_graphHeight, 120f, 400f, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSnapshotView(DeltaPackerSnapshotData snapshot)
        {
            DrawGraphHeightSlider();
            var data = snapshot.results != null ? new List<TypeResultData>(snapshot.results) : new List<TypeResultData>();
            DrawBitsGraph(data);
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField($"Snapshot: {snapshot.name} ({snapshot.timestamp})", EditorStyles.miniLabel);
            DrawBenchmarkTable(data);
        }

        private void DrawCompareView()
        {
            var a = _compareSnapshotA.results ?? Array.Empty<TypeResultData>();
            var b = _compareSnapshotB.results ?? Array.Empty<TypeResultData>();
            DrawGraphHeightSlider();
            DrawBitsGraphComparison(a, b);
            EditorGUILayout.Space(12);
            DrawBenchmarkTableComparison(a, b);
        }

        private static List<TypeResultData> ResultsToData(List<TypeResult> results)
        {
            var list = new List<TypeResultData>(results.Count);
            foreach (var r in results)
            {
                list.Add(new TypeResultData
                {
                    TypeName = r.TypeName,
                    BitsFull = r.BitsFull,
                    BitsDeltaUnchanged = r.BitsDeltaUnchanged,
                    BitsDeltaChanged = r.BitsDeltaChanged,
                    WriteTimeMicroseconds = r.WriteTimeMicroseconds,
                    ReadTimeMicroseconds = r.ReadTimeMicroseconds,
                    Error = r.Error
                });
            }
            return list;
        }

        private void RunAnalysis()
        {
            _errorMessage = null;
            _results.Clear();
            _hasRunAnalysis = true;

            int totalTypes = _typeEntries.Count;
            const int phasesPerType = 4; // bits, write bench, read bench, add
            int totalSteps = totalTypes * phasesPerType;

            // Ensure all Packer/DeltaPacker types are registered before we use them (avoids slow reflection path).
            NetworkManager.CallAllRegisters();

            try
            {
                EditorUtility.DisplayProgressBar("DeltaPacker Analysis", "Starting...", 0f);

                using (var packer = BitPackerPool.Get())
                {
                    for (int typeIndex = 0; typeIndex < totalTypes; typeIndex++)
                    {
                        var entry = _typeEntries[typeIndex];
                        int step = typeIndex * phasesPerType;

                        if (EditorUtility.DisplayCancelableProgressBar("DeltaPacker Analysis", $"Measuring bits: {entry.DisplayName}...", (float)step / totalSteps))
                        {
                            _errorMessage = "Analysis cancelled.";
                            _results.Clear();
                            return;
                        }

                        packer.ResetPositionAndMode(true);
                        var result = new TypeResult { TypeName = entry.DisplayName };

                        try
                        {
                            // Bits: Full (Packer write of new value only)
                            packer.ResetPosition();
                            int startBits = packer.positionInBits;
                            Packer.Write(packer, entry.Type, entry.NewValue);
                            result.BitsFull = packer.positionInBits - startBits;

                            // Bits: Delta unchanged (old == new)
                            packer.ResetPosition();
                            startBits = packer.positionInBits;
                            DeltaPacker.Write(packer, entry.Type, entry.OldValue, entry.OldValue);
                            result.BitsDeltaUnchanged = packer.positionInBits - startBits;

                            // Bits: Delta changed (old != new)
                            packer.ResetPosition();
                            startBits = packer.positionInBits;
                            DeltaPacker.Write(packer, entry.Type, entry.OldValue, entry.NewValue);
                            result.BitsDeltaChanged = packer.positionInBits - startBits;
                        }
                        catch (Exception ex)
                        {
                            result.Error = ex.Message;
                            _results.Add(result);
                            continue;
                        }

                        step++;
                        if (EditorUtility.DisplayCancelableProgressBar("DeltaPacker Analysis", $"Benchmark write: {entry.DisplayName}...", (float)step / totalSteps))
                        {
                            _errorMessage = "Analysis cancelled.";
                            _results.Clear();
                            return;
                        }

                        try
                        {
                            int writeIterations = BenchmarkIterations;
                            var sw = Stopwatch.StartNew();
                            for (int i = 0; i < writeIterations; i++)
                            {
                                packer.ResetPosition();
                                DeltaPacker.Write(packer, entry.Type, entry.OldValue, entry.NewValue);
                            }
                            sw.Stop();
                            result.WriteTimeMicroseconds = (double)sw.ElapsedTicks / Stopwatch.Frequency * 1_000_000 / writeIterations;
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error != null ? result.Error + "; " : "") + ex.Message;
                        }

                        step++;
                        if (EditorUtility.DisplayCancelableProgressBar("DeltaPacker Analysis", $"Benchmark read: {entry.DisplayName}...", (float)step / totalSteps))
                        {
                            _errorMessage = "Analysis cancelled.";
                            _results.Clear();
                            return;
                        }

                        try
                        {
                            packer.ResetPosition();
                            DeltaPacker.Write(packer, entry.Type, entry.OldValue, entry.NewValue);
                            packer.ResetPositionAndMode(false);

                            int writeIterations = BenchmarkIterations;
                            var sw = Stopwatch.StartNew();
                            for (int i = 0; i < writeIterations; i++)
                            {
                                packer.SetBitPosition(0);
                                object readVal = entry.NewValue;
                                DeltaPacker.Read(packer, entry.Type, entry.OldValue, ref readVal);
                            }
                            sw.Stop();
                            result.ReadTimeMicroseconds = (double)sw.ElapsedTicks / Stopwatch.Frequency * 1_000_000 / writeIterations;
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error != null ? result.Error + "; " : "") + ex.Message;
                        }

                        _results.Add(result);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawBitsGraph(IList<TypeResultData> data)
        {
            if (data == null || data.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Bits per type (Full vs Delta)", EditorStyles.boldLabel);

            float maxBits = 1f;
            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                if (!string.IsNullOrEmpty(r.Error)) continue;
                maxBits = Mathf.Max(maxBits, r.BitsFull, r.BitsDeltaUnchanged, r.BitsDeltaChanged);
            }

            float availableWidth = position.width - LabelWidth - 48f;
            availableWidth = Mathf.Max(availableWidth, 200f);
            float barAreaWidth = availableWidth;
            int typeCount = data.Count;
            float groupWidth = typeCount > 0 ? barAreaWidth / typeCount : barAreaWidth;
            float gap = 2f;
            float barWidth = typeCount > 0 ? Mathf.Max(3f, (groupWidth - gap * 2f) / 3f) : BarWidth;

            float totalHeight = _graphHeight + 50f;
            Rect graphRect = GUILayoutUtility.GetRect(barAreaWidth + LabelWidth + 48f, totalHeight);

            Rect labelRect = new Rect(graphRect.x, graphRect.y, LabelWidth, _graphHeight);
            EditorGUI.LabelField(new Rect(labelRect.x, labelRect.y, labelRect.width, 18), "bits", EditorStyles.miniLabel);
            for (int i = 4; i >= 0; i--)
            {
                float val = maxBits * i / 4f;
                EditorGUI.LabelField(new Rect(labelRect.x, labelRect.y + 20 + (_graphHeight - 40) * (1f - (float)i / 4), labelRect.width, 18), val.ToString("0"));
            }

            Rect barRect = new Rect(graphRect.x + LabelWidth + 8f, graphRect.y + 4f, barAreaWidth, _graphHeight);
            EditorGUI.DrawRect(barRect, new Color(0.22f, 0.22f, 0.22f, 1f));

            float barStartX = barRect.x + gap;
            float barY = barRect.y + barRect.height - 4f;
            const float labelHeight = 32f;
            float maxH = barRect.height - labelHeight - 6f;

            // Vertical separators between type groups
            Handles.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            for (int i = 1; i < data.Count; i++)
            {
                float lineX = barStartX + i * groupWidth;
                Handles.DrawLine(new Vector3(lineX, barRect.y, 0), new Vector3(lineX, barRect.y + barRect.height, 0));
            }
            Handles.color = Color.white;

            var wrapStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, alignment = TextAnchor.UpperCenter };

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                if (!string.IsNullOrEmpty(r.Error)) continue;

                float x = barStartX + i * groupWidth;
                float hFull = maxBits > 0 ? (r.BitsFull / maxBits) * maxH : 0;
                EditorGUI.DrawRect(new Rect(x, barY - hFull, barWidth, hFull), new Color(0.9f, 0.35f, 0.35f, 0.9f));
                float hUnch = maxBits > 0 ? (r.BitsDeltaUnchanged / maxBits) * maxH : 0;
                EditorGUI.DrawRect(new Rect(x + barWidth + gap, barY - hUnch, barWidth, hUnch), new Color(0.35f, 0.8f, 0.35f, 0.9f));
                float hChg = maxBits > 0 ? (r.BitsDeltaChanged / maxBits) * maxH : 0;
                EditorGUI.DrawRect(new Rect(x + (barWidth + gap) * 2f, barY - hChg, barWidth, hChg), new Color(0.35f, 0.5f, 0.9f, 0.9f));
                GUI.Label(new Rect(x, barY + 2f, groupWidth - gap, labelHeight), r.TypeName, wrapStyle);
            }

            GUILayout.Space(labelHeight + 4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(graphRect.x + LabelWidth + 8);
            DrawLegendSwatch(new Color(0.9f, 0.35f, 0.35f, 0.9f));
            EditorGUILayout.LabelField("Full (Packer)", EditorStyles.miniLabel, GUILayout.Width(90));
            DrawLegendSwatch(new Color(0.35f, 0.8f, 0.35f, 0.9f));
            EditorGUILayout.LabelField("Delta unchanged", EditorStyles.miniLabel, GUILayout.Width(100));
            DrawLegendSwatch(new Color(0.35f, 0.5f, 0.9f, 0.9f));
            EditorGUILayout.LabelField("Delta changed", EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawBitsGraphComparison(TypeResultData[] a, TypeResultData[] b)
        {
            var typeNames = new HashSet<string>();
            for (int i = 0; i < (a?.Length ?? 0); i++) if (a[i] != null) typeNames.Add(a[i].TypeName);
            for (int i = 0; i < (b?.Length ?? 0); i++) if (b[i] != null) typeNames.Add(b[i].TypeName);
            var types = new List<string>(typeNames);
            types.Sort();

            float maxBits = 1f;
            var aByType = (a ?? Array.Empty<TypeResultData>()).ToDictionary(x => x.TypeName, x => x);
            var bByType = (b ?? Array.Empty<TypeResultData>()).ToDictionary(x => x.TypeName, x => x);
            foreach (string t in types)
            {
                if (aByType.TryGetValue(t, out var ra) && string.IsNullOrEmpty(ra.Error))
                    maxBits = Mathf.Max(maxBits, ra.BitsDeltaChanged);
                if (bByType.TryGetValue(t, out var rb) && string.IsNullOrEmpty(rb.Error))
                    maxBits = Mathf.Max(maxBits, rb.BitsDeltaChanged);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Compare: Delta changed (bits)", EditorStyles.boldLabel);

            float availableWidth = position.width - LabelWidth - 48f;
            availableWidth = Mathf.Max(availableWidth, 200f);
            float barAreaWidth = availableWidth;
            int typeCount = types.Count;
            float groupWidth = typeCount > 0 ? barAreaWidth / typeCount : barAreaWidth;
            float gap = 2f;
            float barWidth = typeCount > 0 ? Mathf.Max(3f, (groupWidth - gap) / 2f) : BarWidth;

            float totalHeight = _graphHeight + 50f;
            Rect graphRect = GUILayoutUtility.GetRect(barAreaWidth + LabelWidth + 48f, totalHeight);

            Rect labelRect = new Rect(graphRect.x, graphRect.y, LabelWidth, _graphHeight);
            EditorGUI.LabelField(new Rect(labelRect.x, labelRect.y, labelRect.width, 18), "bits", EditorStyles.miniLabel);
            for (int i = 4; i >= 0; i--)
            {
                float val = maxBits * i / 4f;
                EditorGUI.LabelField(new Rect(labelRect.x, labelRect.y + 20 + (_graphHeight - 40) * (1f - (float)i / 4), labelRect.width, 18), val.ToString("0"));
            }

            Rect barRect = new Rect(graphRect.x + LabelWidth + 8f, graphRect.y + 4f, barAreaWidth, _graphHeight);
            EditorGUI.DrawRect(barRect, new Color(0.22f, 0.22f, 0.22f, 1f));
            float barStartX = barRect.x + gap;
            float barY = barRect.y + barRect.height - 4f;
            const float labelHeight = 32f;
            float maxH = barRect.height - labelHeight - 6f;

            // Vertical separators between type groups
            Handles.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            for (int i = 1; i < types.Count; i++)
            {
                float lineX = barStartX + i * groupWidth;
                Handles.DrawLine(new Vector3(lineX, barRect.y, 0), new Vector3(lineX, barRect.y + barRect.height, 0));
            }
            Handles.color = Color.white;

            var wrapStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, alignment = TextAnchor.UpperCenter };

            for (int i = 0; i < types.Count; i++)
            {
                string t = types[i];
                float x = barStartX + i * groupWidth;
                if (aByType.TryGetValue(t, out var ra) && string.IsNullOrEmpty(ra.Error))
                {
                    float h = maxBits > 0 ? (ra.BitsDeltaChanged / maxBits) * maxH : 0;
                    EditorGUI.DrawRect(new Rect(x, barY - h, barWidth, h), new Color(0.9f, 0.5f, 0.2f, 0.9f));
                }
                if (bByType.TryGetValue(t, out var rb) && string.IsNullOrEmpty(rb.Error))
                {
                    float h = maxBits > 0 ? (rb.BitsDeltaChanged / maxBits) * maxH : 0;
                    EditorGUI.DrawRect(new Rect(x + barWidth + gap, barY - h, barWidth, h), new Color(0.2f, 0.5f, 0.9f, 0.9f));
                }
                GUI.Label(new Rect(x, barY + 2f, groupWidth - gap, labelHeight), t, wrapStyle);
            }
            GUILayout.Space(labelHeight + 4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(graphRect.x + LabelWidth + 8);
            DrawLegendSwatch(new Color(0.9f, 0.5f, 0.2f, 0.9f));
            EditorGUILayout.LabelField("Snapshot A", EditorStyles.miniLabel, GUILayout.Width(80));
            DrawLegendSwatch(new Color(0.2f, 0.5f, 0.9f, 0.9f));
            EditorGUILayout.LabelField("Snapshot B", EditorStyles.miniLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawLegendSwatch(Color c)
        {
            var r = GUILayoutUtility.GetRect(14, 14);
            EditorGUI.DrawRect(r, c);
        }

        private void DrawBenchmarkTable(IList<TypeResultData> data)
        {
            if (data == null || data.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Benchmarks (per operation, " + BenchmarkIterations + " iterations)", EditorStyles.boldLabel);

            float typeColWidth = 100f;
            float colWidth = 72f;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Type", EditorStyles.miniBoldLabel, GUILayout.Width(typeColWidth));
            EditorGUILayout.LabelField("Full", GUILayout.Width(40));
            EditorGUILayout.LabelField("Δ same", GUILayout.Width(48));
            EditorGUILayout.LabelField("Δ diff", GUILayout.Width(48));
            EditorGUILayout.LabelField("Write μs", EditorStyles.miniBoldLabel, GUILayout.Width(colWidth));
            EditorGUILayout.LabelField("Read μs", EditorStyles.miniBoldLabel, GUILayout.Width(colWidth));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(1));
            EditorGUILayout.EndVertical();

            for (int i = 0; i < data.Count; i++)
            {
                var r = data[i];
                bool selected = _selectedResultIndex == i;
                bool hasError = !string.IsNullOrEmpty(r.Error);

                if (selected)
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                else
                    EditorGUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight + 2));

                if (hasError)
                {
                    EditorGUILayout.LabelField(r.TypeName, GUILayout.Width(typeColWidth));
                    EditorGUILayout.LabelField("—", GUILayout.Width(40));
                    EditorGUILayout.LabelField("—", GUILayout.Width(48));
                    EditorGUILayout.LabelField("—", GUILayout.Width(48));
                    EditorGUILayout.LabelField("—", GUILayout.Width(colWidth));
                    EditorGUILayout.LabelField("—", GUILayout.Width(colWidth));
                    EditorGUILayout.LabelField(r.Error, EditorStyles.miniLabel);
                }
                else
                {
                    if (GUILayout.Button(r.TypeName, EditorStyles.label, GUILayout.Width(typeColWidth)))
                        _selectedResultIndex = _selectedResultIndex == i ? -1 : i;

                    EditorGUILayout.LabelField(r.BitsFull.ToString(), GUILayout.Width(40));
                    EditorGUILayout.LabelField(r.BitsDeltaUnchanged.ToString(), GUILayout.Width(48));
                    EditorGUILayout.LabelField(r.BitsDeltaChanged.ToString(), GUILayout.Width(48));
                    EditorGUILayout.LabelField(r.WriteTimeMicroseconds.ToString("F3"), GUILayout.Width(colWidth));
                    EditorGUILayout.LabelField(r.ReadTimeMicroseconds.ToString("F3"), GUILayout.Width(colWidth));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBenchmarkTableComparison(TypeResultData[] a, TypeResultData[] b)
        {
            var typeNames = new HashSet<string>();
            for (int i = 0; i < (a?.Length ?? 0); i++) if (a[i] != null) typeNames.Add(a[i].TypeName);
            for (int i = 0; i < (b?.Length ?? 0); i++) if (b[i] != null) typeNames.Add(b[i].TypeName);
            var types = new List<string>(typeNames);
            types.Sort();

            var aByType = (a ?? Array.Empty<TypeResultData>()).ToDictionary(x => x.TypeName, x => x);
            var bByType = (b ?? Array.Empty<TypeResultData>()).ToDictionary(x => x.TypeName, x => x);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Compare: bits and times", EditorStyles.boldLabel);

            float typeColWidth = 90f;
            float colWidth = 52f;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Type", EditorStyles.miniBoldLabel, GUILayout.Width(typeColWidth));
            EditorGUILayout.LabelField("A Δdiff", GUILayout.Width(colWidth));
            EditorGUILayout.LabelField("B Δdiff", GUILayout.Width(colWidth));
            EditorGUILayout.LabelField("A Write", GUILayout.Width(colWidth));
            EditorGUILayout.LabelField("B Write", GUILayout.Width(colWidth));
            EditorGUILayout.LabelField("A Read", GUILayout.Width(colWidth));
            EditorGUILayout.LabelField("B Read", GUILayout.Width(colWidth));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(1));
            EditorGUILayout.EndVertical();

            foreach (string t in types)
            {
                aByType.TryGetValue(t, out var ra);
                bByType.TryGetValue(t, out var rb);

                EditorGUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight + 2));
                EditorGUILayout.LabelField(t, GUILayout.Width(typeColWidth));

                bool aOk = ra != null && string.IsNullOrEmpty(ra.Error);
                bool bOk = rb != null && string.IsNullOrEmpty(rb.Error);

                EditorGUILayout.LabelField(aOk ? ra.BitsDeltaChanged.ToString() : "—", GUILayout.Width(colWidth));
                EditorGUILayout.LabelField(bOk ? rb.BitsDeltaChanged.ToString() : "—", GUILayout.Width(colWidth));
                EditorGUILayout.LabelField(aOk ? ra.WriteTimeMicroseconds.ToString("F3") : "—", GUILayout.Width(colWidth));
                EditorGUILayout.LabelField(bOk ? rb.WriteTimeMicroseconds.ToString("F3") : "—", GUILayout.Width(colWidth));
                EditorGUILayout.LabelField(aOk ? ra.ReadTimeMicroseconds.ToString("F3") : "—", GUILayout.Width(colWidth));
                EditorGUILayout.LabelField(bOk ? rb.ReadTimeMicroseconds.ToString("F3") : "—", GUILayout.Width(colWidth));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void SaveSnapshot()
        {
            if (_results == null || _results.Count == 0)
            {
                EditorUtility.DisplayDialog("Save Snapshot", "Run analysis first.", "OK");
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string defaultName = $"{SnapshotFilePrefix}{timestamp}.json";
            string path = Path.Combine(_snapshotsFolder, defaultName);

            var data = new DeltaPackerSnapshotData
            {
                name = $"Snapshot {DateTime.Now:yyyy-MM-dd HH:mm}",
                timestamp = DateTime.Now.ToString("o"),
                results = ResultsToData(_results).ToArray()
            };

            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(path, json);
                RefreshSnapshotList();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Save Snapshot", "Saved to DeltaSnapshots.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Save Snapshot Failed", ex.Message, "OK");
            }
        }

        private static DeltaPackerSnapshotData LoadSnapshot(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var data = JsonUtility.FromJson<DeltaPackerSnapshotData>(json);
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ExportCsv()
        {
            if (_results == null || _results.Count == 0)
            {
                EditorUtility.DisplayDialog("Export", "Run analysis first.", "OK");
                return;
            }

            var path = EditorUtility.SaveFilePanel("Export DeltaPacker Analysis", "", "DeltaPackerAnalysis.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var lines = new List<string>
            {
                "Type,BitsFull,BitsDeltaUnchanged,BitsDeltaChanged,WriteMicroseconds,ReadMicroseconds,Error"
            };

            foreach (var r in _results)
            {
                string err = string.IsNullOrEmpty(r.Error) ? "" : "\"" + r.Error.Replace("\"", "\"\"") + "\"";
                lines.Add($"{r.TypeName},{r.BitsFull},{r.BitsDeltaUnchanged},{r.BitsDeltaChanged},{r.WriteTimeMicroseconds:F4},{r.ReadTimeMicroseconds:F4},{err}");
            }

            try
            {
                File.WriteAllLines(path, lines);
                EditorUtility.DisplayDialog("Export", "Saved to " + path, "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }
    }
}
