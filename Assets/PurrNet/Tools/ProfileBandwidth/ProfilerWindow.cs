using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using PurrNet.Packing;

namespace PurrNet.Profiler.Editor
{
    /// <summary>
    /// Editor window for the PurrNet Profiler, allowing visualization and analysis of network traffic.
    /// </summary>
    public class ProfilerWindow : EditorWindow
    {
        #region Fields

        // Scroll positions
        private Vector2 graphScrollPosition;
        private Vector2 detailsScrollPosition;

        // Graph settings
        private int selectedSampleIndex = -1;
        private int hoveredSampleIndex = -1;
        private float graphHeight = 200f;
        private const float minGraphHeight = 100f;
        private const float maxGraphHeight = 500f;
        private const float defaultGraphHeight = 200f;
        private const string graphHeightPrefKey = "PurrNet_Profiler_GraphHeight";
        private const float barWidth = 20f;
        private const float labelWidth = 50f;
        private bool isResizingGraph;
        private float resizeStartY;
        private float resizeStartHeight;

        // Graph data
        private readonly List<float> receivedRpcData = new List<float>(100);
        private readonly List<float> sentRpcData = new List<float>(100);
        private readonly List<float> receivedBroadcastData = new List<float>(100);
        private readonly List<float> sentBroadcastData = new List<float>(100);
        private readonly List<float> forwardedBytesData = new List<float>(100);

        // UI state
        private readonly Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>(32);
        private readonly Dictionary<string, bool> expandedPacketStates = new Dictionary<string, bool>(32);

        // Cache for the most recently used RPC data
        private string lastRpcDataString;
        private Type lastRpcType;
        private string lastRpcMethod;
        private RPCType lastRpcRpcType;
        private BitPacker lastRpcPacker;

        #endregion

        #region Unity Editor Integration

        [MenuItem("Tools/PurrNet/Analysis/Bandwidth Profiler")]
        public static void ShowWindow()
        {
            var window = GetWindow<ProfilerWindow>("Bandwidth Profiler");
            var purrnetLogo = Resources.Load("purricon") as Texture2D;
            window.titleContent = new GUIContent("Bandwidth Profiler", purrnetLogo, "Bandwidth Profiler");
            window.Show();
        }

        [MenuItem("Tools/PurrNet/Analysis/Bandwidth Profiler (New Window)")]
        public static void CreateWindow()
        {
            var window = CreateInstance<ProfilerWindow>();
            var purrnetLogo = Resources.Load("purricon") as Texture2D;
            window.titleContent = new GUIContent("Bandwidth Profiler", purrnetLogo, "Bandwidth Profiler");
            window.Show();
        }

        void OnEnable()
        {
            Statistics.inspecting += 1;

            // Subscribe to the onSampleEnded event to refresh the GUI
            Statistics.onSampleEnded += OnSampleEnded;

            // Load saved graph height from EditorPrefs
            graphHeight = EditorPrefs.GetFloat(graphHeightPrefKey, defaultGraphHeight);
        }

        void OnDisable()
        {
            Statistics.inspecting -= 1;

            // Unsubscribe from the event when the window is closed
            Statistics.onSampleEnded -= OnSampleEnded;

            // Save graph height to EditorPrefs
            EditorPrefs.SetFloat(graphHeightPrefKey, graphHeight);
        }

        private void OnSampleEnded()
        {
            // Refresh the GUI when a new sample is added
            Repaint();
        }

        #endregion

        #region GUI Rendering

        void OnGUI()
        {
            // Add button row
            GUILayout.BeginHorizontal();

            // Add record/stop button
            if (GUILayout.Button(Statistics.paused ? "Start Recording" : "Stop Recording"))
            {
                Statistics.paused = !Statistics.paused;
                Repaint();
            }

            // Add clear button
            if (GUILayout.Button("Clear"))
            {
                Statistics.samples.Clear();
                selectedSampleIndex = -1;
                Repaint();
            }

            if (GUILayout.Button("Load File"))
            {
                string path = EditorUtility.OpenFilePanel("Load Bandwidth Profile", "", "data");
                if (!string.IsNullOrEmpty(path))
                {
                    BandwidthProfilerToFile.LoadSamples(path);
                    selectedSampleIndex = -1; // Reset selection when loading new data
                    Repaint();
                }
            }

            GUILayout.EndHorizontal();

            var samples = Statistics.samples;

            // Update graph data
            UpdateGraphData();

            // Always draw the graph
            DrawGraph();

            // Draw details view if a sample is selected
            if (selectedSampleIndex >= 0 && selectedSampleIndex < samples.Count)
            {
                DrawSampleDetails(samples[selectedSampleIndex]);
            }
            else if (samples.Count > 0)
            {
                int idx = samples.Count - 1;
                DrawSample(samples[idx], idx);
            }
        }

        #endregion

        #region Graph Management

        private void UpdateGraphData()
        {
            // Clear existing data
            receivedRpcData.Clear();
            sentRpcData.Clear();
            receivedBroadcastData.Clear();
            sentBroadcastData.Clear();
            forwardedBytesData.Clear();

            // Add data from each sample
            foreach (var sample in Statistics.samples)
            {
                receivedRpcData.Add(sample.receivedRpcs.Sum(rpc => rpc.data.length));
                sentRpcData.Add(sample.sentRpcs.Sum(rpc => rpc.data.length));
                receivedBroadcastData.Add(sample.receivedBroadcasts.Sum(b => b.data.length));
                sentBroadcastData.Add(sample.sentBroadcasts.Sum(b => b.data.length));
                forwardedBytesData.Add(sample.forwardedBytes.Sum());
            }

            bool isInPlayMode = EditorApplication.isPlaying;
            bool isPaused = EditorApplication.isPaused;
            // Auto-scroll to the end when new data is added and actively recording
            if (receivedRpcData.Count > 0 && isInPlayMode && !isPaused)
            {
                float totalWidth = receivedRpcData.Count * barWidth;
                graphScrollPosition.x = totalWidth;
            }
        }

        private void DrawGraph()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Network Traffic Graph", EditorStyles.boldLabel);

            // Get the maximum value for scaling
            float maxValue = 1f; // Avoid division by zero
            if (receivedRpcData.Count > 0)
            {
                // Calculate the maximum total height for each bar
                for (int i = 0; i < receivedRpcData.Count; i++)
                {
                    float totalBarHeight = receivedRpcData[i] +
                                          sentRpcData[i] +
                                          receivedBroadcastData[i] +
                                          sentBroadcastData[i] +
                                          forwardedBytesData[i];
                    maxValue = Math.Max(maxValue, totalBarHeight);
                }
            }

            // Calculate the total width needed for all bars
            float totalWidth = receivedRpcData.Count * barWidth;

            // Create a horizontal layout for the graph and labels
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);

            // Draw the Y-axis labels
            EditorGUILayout.BeginVertical(GUILayout.Width(labelWidth));

            // Draw value labels on the left side
            for (int i = 4; i >= 0; i--)
            {
                float value = maxValue * i / 4;
                string label = FormatBytes(value);
                EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth), GUILayout.Height(graphHeight / 5));
            }

            EditorGUILayout.EndVertical();

            // Create a scroll view for the graph
            graphScrollPosition = EditorGUILayout.BeginScrollView(graphScrollPosition, GUILayout.Height(graphHeight + 20));

            // Draw the graph background
            var graphRect = GUILayoutUtility.GetRect(totalWidth, graphHeight);

            // Draw background
            EditorGUI.DrawRect(graphRect, new Color(0.2f, 0.2f, 0.2f, 1));

            // Draw grid lines
            Handles.color = new Color(0.3f, 0.3f, 0.3f, 1);
            float gridSegmentHeight = graphRect.height / 5;
            for (int i = 0; i <= 5; i++)
            {
                float y = graphRect.y + (graphRect.height - i * gridSegmentHeight);
                Handles.DrawLine(
                    new Vector3(graphRect.x, y, 0),
                    new Vector3(graphRect.x + graphRect.width, y, 0)
                );
            }

            // Draw data points
            if (receivedRpcData.Count > 0)
            {
                const float spacing = 2f;
                hoveredSampleIndex = -1; // Reset hover index at the start of drawing

                for (int i = 0; i < receivedRpcData.Count; i++)
                {
                    float x = graphRect.x + (i * barWidth + i * spacing);
                    float currentY = graphRect.y + graphRect.height;

                    // Create a click/hover rect for the entire bar
                    var barRect = new Rect(x, graphRect.y, barWidth, graphRect.height);

                    // Check for hover
                    if (barRect.Contains(Event.current.mousePosition))
                    {
                        hoveredSampleIndex = i;

                        // Show tooltip with sample information
                        if (i >= 0 && i < Statistics.samples.Count)
                        {
                            var sample = Statistics.samples[i];
                            string tooltip = $"Frame {i}\n" +
                                            $"Received RPCs: {FormatBytes(sample.receivedRpcs.Sum(rpc => rpc.data.length))}\n" +
                                            $"Sent RPCs: {FormatBytes(sample.sentRpcs.Sum(rpc => rpc.data.length))}\n" +
                                            $"Received Broadcasts: {FormatBytes(sample.receivedBroadcasts.Sum(b => b.data.length))}\n" +
                                            $"Sent Broadcasts: {FormatBytes(sample.sentBroadcasts.Sum(b => b.data.length))}\n" +
                                            $"Forwarded: {FormatBytes(sample.forwardedBytes.Sum())}";

                            GUI.tooltip = tooltip;
                        }

                        Repaint(); // Repaint to update hover effect
                    }

                    // Determine if this bar is selected or hovered
                    bool isSelected = i == selectedSampleIndex;
                    bool isHovered = i == hoveredSampleIndex;

                    // Draw a highlight for selected or hovered bars
                    if (isSelected || isHovered)
                    {
                        var highlightColor = isSelected ?
                            new Color(1f, 1f, 1f, 0.3f) : // White for selected
                            new Color(0.8f, 0.8f, 0.8f, 0.2f); // Light gray for hovered

                        EditorGUI.DrawRect(barRect, highlightColor);
                    }

                    // Draw received RPCs
                    float height = receivedRpcData[i] / maxValue * graphRect.height;
                    EditorGUI.DrawRect(
                        new Rect(x, currentY - height, barWidth, height),
                        new Color(0.2f, 0.8f, 0.2f, 0.8f)
                    );
                    currentY -= height;

                    // Draw sent RPCs
                    height = sentRpcData[i] / maxValue * graphRect.height;
                    EditorGUI.DrawRect(
                        new Rect(x, currentY - height, barWidth, height),
                        new Color(0.8f, 0.2f, 0.2f, 0.8f)
                    );
                    currentY -= height;

                    // Draw received broadcasts
                    height = receivedBroadcastData[i] / maxValue * graphRect.height;
                    EditorGUI.DrawRect(
                        new Rect(x, currentY - height, barWidth, height),
                        new Color(0.2f, 0.2f, 0.8f, 0.8f)
                    );
                    currentY -= height;

                    // Draw sent broadcasts
                    height = sentBroadcastData[i] / maxValue * graphRect.height;
                    EditorGUI.DrawRect(
                        new Rect(x, currentY - height, barWidth, height),
                        new Color(0.8f, 0.8f, 0.2f, 0.8f)
                    );
                    currentY -= height;

                    // Draw forwarded bytes
                    height = forwardedBytesData[i] / maxValue * graphRect.height;
                    EditorGUI.DrawRect(
                        new Rect(x, currentY - height, barWidth, height),
                        new Color(0.8f, 0.2f, 0.8f, 0.8f)
                    );

                    // Handle click to select sample
                    if (Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
                    {
                        // Toggle selection - if already selected, deselect it
                        if (selectedSampleIndex == i)
                        {
                            selectedSampleIndex = -1;
                        }
                        else
                        {
                            selectedSampleIndex = i;

                            // Pause the editor if in play mode
                            if (Application.isPlaying)
                            {
                                EditorApplication.isPaused = true;
                            }
                        }
                        Repaint();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Add a more visible resize handle at the bottom of the graph
            Rect resizeHandleRect = GUILayoutUtility.GetRect(0, 10);

            // Draw a visual indicator for the resize handle
            Color originalColor = GUI.color;
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            EditorGUI.DrawRect(resizeHandleRect, new Color(0.5f, 0.5f, 0.5f, 1f));

            // Draw a grip texture to indicate draggability
            Rect gripRect = new Rect(resizeHandleRect.x + resizeHandleRect.width / 2 - 20,
                                    resizeHandleRect.y + resizeHandleRect.height / 2 - 2,
                                    40, 4);
            EditorGUI.DrawRect(new Rect(gripRect.x, gripRect.y, gripRect.width, 1), Color.white);
            EditorGUI.DrawRect(new Rect(gripRect.x, gripRect.y + 3, gripRect.width, 1), Color.white);

            GUI.color = originalColor;

            // Add cursor feedback
            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeVertical);

            // Handle resize events
            if (Event.current.type == EventType.MouseDown && resizeHandleRect.Contains(Event.current.mousePosition))
            {
                isResizingGraph = true;
                resizeStartY = Event.current.mousePosition.y;
                resizeStartHeight = graphHeight;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && isResizingGraph)
            {
                isResizingGraph = false;
                // Save the graph height to EditorPrefs when resizing is complete
                EditorPrefs.SetFloat(graphHeightPrefKey, graphHeight);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && isResizingGraph)
            {
                float deltaY = Event.current.mousePosition.y - resizeStartY;
                float newHeight = Mathf.Clamp(resizeStartHeight + deltaY, minGraphHeight, maxGraphHeight);

                // Only repaint if the height actually changed
                if (!Mathf.Approximately(newHeight, graphHeight))
                {
                    graphHeight = newHeight;
                    Repaint();
                }

                Event.current.Use();
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Sample Management

        private void DrawSampleDetails(TickSample sample)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Sample Details", EditorStyles.boldLabel);

            // Create a scroll view for the sample details
            try
            {
                // Use GUILayout.ExpandWidth(false) to prevent horizontal expansion
                detailsScrollPosition = EditorGUILayout.BeginScrollView(detailsScrollPosition, GUILayout.ExpandWidth(false));

                // Wrap content in a vertical layout that expands to fill available width
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                if (sample.receivedRpcs.Count > 0)
                {
                    Color originalBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.3f, 0.9f, 0.3f, 0.2f);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = originalBgColor;

                    var headerStyle = new GUIStyle(EditorStyles.foldout);
                    Color headerColor = new Color(0.2f, 0.8f, 0.2f, 1f);
                    headerStyle.normal.textColor = headerColor;
                    headerStyle.onNormal.textColor = headerColor;
                    headerStyle.focused.textColor = headerColor;
                    headerStyle.onFocused.textColor = headerColor;
                    headerStyle.active.textColor = headerColor;
                    headerStyle.onActive.textColor = headerColor;
                    headerStyle.fontStyle = FontStyle.Bold;
                    bool sectionExpanded = EditorGUILayout.Foldout(GetFoldoutState("section_received_rpcs", true), "Received RPCs", true, headerStyle);
                    SetFoldoutState("section_received_rpcs", sectionExpanded);

                    if (sectionExpanded)
                    {
                        EditorGUI.indentLevel++;
                        // Aggregate received RPCs by type and method
                        var aggregatedReceivedRpcs = sample.receivedRpcs
                            .GroupBy(rpc => new { rpc.type, rpc.method })
                            .Select(group => new
                            {
                                Type = group.Key.type,
                                Method = group.Key.method,
                                Count = group.Count(),
                                TotalBytes = group.Sum(rpc => rpc.data.length),
                                Items = group.ToList()
                            })
                            .OrderByDescending(rpc => rpc.TotalBytes);

                        foreach (var rpcGroup in aggregatedReceivedRpcs)
                        {
                            // Create a foldout for each RPC group
                            string label = $"{rpcGroup.Type.GetFriendlyTypeName()}.{rpcGroup.Method} ({FormatBytes(rpcGroup.TotalBytes)}) - {rpcGroup.Count} calls";
                            bool expanded = EditorGUILayout.Foldout(GetFoldoutState($"received_{rpcGroup.Type.Name}_{rpcGroup.Method}", false), label, true);
                            SetFoldoutState($"received_{rpcGroup.Type.Name}_{rpcGroup.Method}", expanded);

                            if (expanded)
                            {
                                EditorGUI.indentLevel++;
                                foreach (var rpc in rpcGroup.Items)
                                {
                                    string packetKey = $"received_rpc_{rpc.type.Name}_{rpc.method}_{rpc.GetHashCode()}";
                                    bool isExpanded = GetExpandedPacketState(packetKey);

                                    EditorGUILayout.BeginHorizontal();
                                    GUILayout.Space(30); // Increased indentation space

                                    EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                                    // Temporarily decrease indent level for the foldout
                                    EditorGUI.indentLevel--;
                                    GUILayout.BeginHorizontal();
                                    bool newExpanded = EditorGUILayout.Foldout(isExpanded, $"{FormatBytes(rpc.data.length)} bytes", true);
                                    if (rpc.context != null)
                                        EditorGUILayout.ObjectField(rpc.context, typeof(UnityEngine.Object), true);
                                    GUILayout.EndHorizontal();
                                    EditorGUI.indentLevel++;

                                    if (newExpanded != isExpanded)
                                    {
                                        SetExpandedPacketState(packetKey, newExpanded);
                                        Repaint();
                                    }

                                    // Show packet data if expanded
                                    if (isExpanded)
                                    {
                                        EditorGUILayout.BeginVertical();
                                        EditorGUILayout.TextArea(GetRpcOrBroadcastDataString(rpc.data, rpc.type, rpc.rpcType, rpc.method));
                                        EditorGUILayout.EndVertical();
                                    }

                                    EditorGUILayout.EndVertical();
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUI.indentLevel--;
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }

                if (sample.sentRpcs.Count > 0)
                {
                    Color originalBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 0.2f);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = originalBgColor;

                    var headerStyle = new GUIStyle(EditorStyles.foldout);
                    Color headerColor = new Color(0.8f, 0.2f, 0.2f, 1f);
                    headerStyle.normal.textColor = headerColor;
                    headerStyle.onNormal.textColor = headerColor;
                    headerStyle.focused.textColor = headerColor;
                    headerStyle.onFocused.textColor = headerColor;
                    headerStyle.active.textColor = headerColor;
                    headerStyle.onActive.textColor = headerColor;
                    headerStyle.fontStyle = FontStyle.Bold;
                    bool sectionExpanded = EditorGUILayout.Foldout(GetFoldoutState("section_sent_rpcs", true), "Sent RPCs", true, headerStyle);
                    SetFoldoutState("section_sent_rpcs", sectionExpanded);

                    if (sectionExpanded)
                    {
                        EditorGUI.indentLevel++;
                        // Aggregate sent RPCs by type and method
                        var aggregatedSentRpcs = sample.sentRpcs
                            .GroupBy(rpc => new { rpc.type, rpc.method })
                            .Select(group => new
                            {
                                Type = group.Key.type,
                                Method = group.Key.method,
                                Count = group.Count(),
                                TotalBytes = group.Sum(rpc => rpc.data.length),
                                Items = group.ToList()
                            })
                            .OrderByDescending(rpc => rpc.TotalBytes);

                        foreach (var rpcGroup in aggregatedSentRpcs)
                        {
                            // Create a foldout for each RPC group
                            string label = $"{rpcGroup.Type.GetFriendlyTypeName()}.{rpcGroup.Method} ({FormatBytes(rpcGroup.TotalBytes)}) - {rpcGroup.Count} calls";
                            bool expanded = EditorGUILayout.Foldout(GetFoldoutState($"sent_{rpcGroup.Type.Name}_{rpcGroup.Method}", false), label, true);
                            SetFoldoutState($"sent_{rpcGroup.Type.Name}_{rpcGroup.Method}", expanded);

                            if (expanded)
                            {
                                EditorGUI.indentLevel++;
                                foreach (var rpc in rpcGroup.Items)
                                {
                                    string packetKey = $"sent_rpc_{rpc.type.Name}_{rpc.method}_{rpc.GetHashCode()}";
                                    bool isExpanded = GetExpandedPacketState(packetKey);

                                    EditorGUILayout.BeginHorizontal();
                                    GUILayout.Space(30); // Increased indentation space

                                    EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                                    // Temporarily decrease indent level for the foldout
                                    EditorGUI.indentLevel--;
                                    GUILayout.BeginHorizontal();
                                    bool newExpanded = EditorGUILayout.Foldout(isExpanded, $"{FormatBytes(rpc.data.length)} bytes", true);
                                    if (rpc.context != null)
                                        EditorGUILayout.ObjectField(rpc.context, typeof(UnityEngine.Object), true);
                                    GUILayout.EndHorizontal();
                                    EditorGUI.indentLevel++;

                                    if (newExpanded != isExpanded)
                                    {
                                        SetExpandedPacketState(packetKey, newExpanded);
                                        Repaint();
                                    }

                                    // Show packet data if expanded
                                    if (isExpanded)
                                    {
                                        EditorGUILayout.BeginVertical();
                                        EditorGUILayout.TextArea(GetRpcOrBroadcastDataString(rpc.data, rpc.type, rpc.rpcType, rpc.method));
                                        EditorGUILayout.EndVertical();
                                    }

                                    EditorGUILayout.EndVertical();
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUI.indentLevel--;
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }

                if (sample.receivedBroadcasts.Count > 0)
                {
                    Color originalBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.3f, 0.3f, 0.9f, 0.2f);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = originalBgColor;

                    var headerStyle = new GUIStyle(EditorStyles.foldout);
                    Color headerColor = new Color(0.4f, 0.6f, 1.0f, 1f);
                    headerStyle.normal.textColor = headerColor;
                    headerStyle.onNormal.textColor = headerColor;
                    headerStyle.focused.textColor = headerColor;
                    headerStyle.onFocused.textColor = headerColor;
                    headerStyle.active.textColor = headerColor;
                    headerStyle.onActive.textColor = headerColor;
                    headerStyle.fontStyle = FontStyle.Bold;
                    bool sectionExpanded = EditorGUILayout.Foldout(GetFoldoutState("section_received_broadcasts", true), "Received Broadcasts", true, headerStyle);
                    SetFoldoutState("section_received_broadcasts", sectionExpanded);

                    if (sectionExpanded)
                    {
                        EditorGUI.indentLevel++;
                        // Aggregate received broadcasts by type
                        var aggregatedReceivedBroadcasts = sample.receivedBroadcasts
                            .GroupBy(broadcast => broadcast.type)
                            .Select(group => new
                            {
                                Type = group.Key,
                                Count = group.Count(),
                                TotalBytes = group.Sum(broadcast => broadcast.data.length),
                                Items = group.ToList()
                            })
                            .OrderByDescending(broadcast => broadcast.TotalBytes);

                        foreach (var broadcastGroup in aggregatedReceivedBroadcasts)
                        {
                            // Create a foldout for each broadcast group
                            string label = $"{broadcastGroup.Type.GetFriendlyTypeName()} ({FormatBytes(broadcastGroup.TotalBytes)}) - {broadcastGroup.Count} broadcasts";
                            bool expanded = EditorGUILayout.Foldout(GetFoldoutState($"received_broadcast_{broadcastGroup.Type.Name}", false), label, true);
                            SetFoldoutState($"received_broadcast_{broadcastGroup.Type.Name}", expanded);

                            if (expanded)
                            {
                                EditorGUI.indentLevel++;
                                foreach (var broadcast in broadcastGroup.Items)
                                {
                                    string packetKey = $"received_broadcast_{broadcast.type.Name}_{broadcast.GetHashCode()}";
                                    bool isExpanded = GetExpandedPacketState(packetKey);

                                    EditorGUILayout.BeginHorizontal();
                                    GUILayout.Space(30); // Increased indentation space

                                    EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                                    // Temporarily decrease indent level for the foldout
                                    EditorGUI.indentLevel--;
                                    bool newExpanded = EditorGUILayout.Foldout(isExpanded, $"{FormatBytes(broadcast.data.length)} bytes", true);
                                    EditorGUI.indentLevel++;

                                    if (newExpanded != isExpanded)
                                    {
                                        SetExpandedPacketState(packetKey, newExpanded);
                                        Repaint();
                                    }

                                    // Show packet data if expanded
                                    if (isExpanded)
                                    {
                                        EditorGUILayout.BeginVertical();
                                        EditorGUILayout.TextArea(GetRpcOrBroadcastDataString(broadcast.data, broadcast.type, default));
                                        EditorGUILayout.EndVertical();
                                    }

                                    EditorGUILayout.EndVertical();
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUI.indentLevel--;
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }

                if (sample.sentBroadcasts.Count > 0)
                {
                    Color originalBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.9f, 0.9f, 0.3f, 0.2f);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = originalBgColor;

                    var headerStyle = new GUIStyle(EditorStyles.foldout);
                    Color headerColor = new Color(0.8f, 0.8f, 0.2f, 1f);
                    headerStyle.normal.textColor = headerColor;
                    headerStyle.onNormal.textColor = headerColor;
                    headerStyle.focused.textColor = headerColor;
                    headerStyle.onFocused.textColor = headerColor;
                    headerStyle.active.textColor = headerColor;
                    headerStyle.onActive.textColor = headerColor;
                    headerStyle.fontStyle = FontStyle.Bold;
                    bool sectionExpanded = EditorGUILayout.Foldout(GetFoldoutState("section_sent_broadcasts", true), "Sent Broadcasts", true, headerStyle);
                    SetFoldoutState("section_sent_broadcasts", sectionExpanded);

                    if (sectionExpanded)
                    {
                        EditorGUI.indentLevel++;
                        // Aggregate sent broadcasts by type
                        var aggregatedSentBroadcasts = sample.sentBroadcasts
                            .GroupBy(broadcast => broadcast.type)
                            .Select(group => new
                            {
                                Type = group.Key,
                                Count = group.Count(),
                                TotalBytes = group.Sum(broadcast => broadcast.data.length),
                                Items = group.ToList()
                            })
                            .OrderByDescending(broadcast => broadcast.TotalBytes);

                        foreach (var broadcastGroup in aggregatedSentBroadcasts)
                        {
                            // Create a foldout for each broadcast group
                            string label = $"{broadcastGroup.Type.GetFriendlyTypeName()} ({FormatBytes(broadcastGroup.TotalBytes)}) - {broadcastGroup.Count} broadcasts";
                            bool expanded = EditorGUILayout.Foldout(GetFoldoutState($"sent_broadcast_{broadcastGroup.Type.Name}", false), label, true);
                            SetFoldoutState($"sent_broadcast_{broadcastGroup.Type.Name}", expanded);

                            if (expanded)
                            {
                                EditorGUI.indentLevel++;
                                foreach (var broadcast in broadcastGroup.Items)
                                {
                                    string packetKey = $"sent_broadcast_{broadcast.type.Name}_{broadcast.GetHashCode()}";
                                    bool isExpanded = GetExpandedPacketState(packetKey);

                                    EditorGUILayout.BeginHorizontal();
                                    GUILayout.Space(30); // Increased indentation space

                                    EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                                    // Temporarily decrease indent level for the foldout
                                    EditorGUI.indentLevel--;
                                    bool newExpanded = EditorGUILayout.Foldout(isExpanded, $"{FormatBytes(broadcast.data.length)} bytes", true);
                                    EditorGUI.indentLevel++;

                                    if (newExpanded != isExpanded)
                                    {
                                        SetExpandedPacketState(packetKey, newExpanded);
                                        Repaint();
                                    }

                                    // Show packet data if expanded
                                    if (isExpanded)
                                    {
                                        EditorGUILayout.BeginVertical();
                                        EditorGUILayout.TextArea(GetRpcOrBroadcastDataString(broadcast.data, broadcast.type, default));
                                        EditorGUILayout.EndVertical();
                                    }

                                    EditorGUILayout.EndVertical();
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUI.indentLevel--;
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }

                // Draw Forwarded Bytes
                if (sample.forwardedBytes.Count > 0)
                {
                    Color originalBgColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.9f, 0.3f, 0.9f, 0.2f);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = originalBgColor;

                    var headerStyle = new GUIStyle(EditorStyles.foldout);
                    Color headerColor = new Color(0.8f, 0.2f, 0.8f, 1f);
                    headerStyle.normal.textColor = headerColor;
                    headerStyle.onNormal.textColor = headerColor;
                    headerStyle.focused.textColor = headerColor;
                    headerStyle.onFocused.textColor = headerColor;
                    headerStyle.active.textColor = headerColor;
                    headerStyle.onActive.textColor = headerColor;
                    headerStyle.fontStyle = FontStyle.Bold;
                    bool sectionExpanded = EditorGUILayout.Foldout(GetFoldoutState("section_forwarded_bytes", true), "Forwarded Bytes", true, headerStyle);
                    SetFoldoutState("section_forwarded_bytes", sectionExpanded);

                    if (sectionExpanded)
                    {
                        EditorGUI.indentLevel++;
                        int totalBytes = sample.forwardedBytes.Sum();
                        EditorGUILayout.LabelField($"Total: {FormatBytes(totalBytes)}");
                        EditorGUILayout.LabelField($"Count: {sample.forwardedBytes.Count} packets");
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
            }
            catch
            {
                // Make sure to close all layout groups in case of exception
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSample(TickSample sample, int index)
        {
            // Determine if this sample is selected
            bool isSelected = index == selectedSampleIndex;

            // Use a different style for the selected sample
            var boxStyle = isSelected ?
                new GUIStyle(EditorStyles.helpBox) { normal = { background = EditorGUIUtility.whiteTexture }, border = new RectOffset(2, 2, 2, 2) } :
                EditorStyles.helpBox;

            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Overview", EditorStyles.boldLabel);

            // Draw summary
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"RPCs: {sample.receivedRpcs.Count} received, {sample.sentRpcs.Count} sent");
            EditorGUILayout.LabelField($"Broadcasts: {sample.receivedBroadcasts.Count} received, {sample.sentBroadcasts.Count} sent");
            EditorGUILayout.LabelField($"Forwarded: {FormatBytes(sample.forwardedBytes.Sum())} in {sample.forwardedBytes.Count} packets");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        #endregion

        #region Helper Methods

        private string FormatBytes(float bytes)
        {
            if (bytes < 1024)
                return $"{bytes:F0}B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024:F1}KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024 * 1024):F1}MB";
            else
                return $"{bytes / (1024 * 1024 * 1024):F1}GB";
        }

        // Helper method to get foldout state
        private bool GetFoldoutState(string key, bool defaultValue = false)
        {
            foldoutStates.TryAdd(key, defaultValue);
            return foldoutStates[key];
        }

        // Helper method to set foldout state
        private void SetFoldoutState(string key, bool value)
        {
            foldoutStates[key] = value;
        }

        // Helper method to get expanded packet state
        private bool GetExpandedPacketState(string key)
        {
            if (expandedPacketStates.TryAdd(key, false))
                return false;
            return expandedPacketStates[key];
        }

        // Helper method to set expanded packet state
        private void SetExpandedPacketState(string key, bool value)
        {
            expandedPacketStates[key] = value;
        }

        /// <summary>
        /// Provides detailed information about RPC or broadcast packet data
        /// </summary>
        /// <param name="packer">The BitPacker containing the packet data</param>
        /// <param name="type">The type of the RPC or broadcast</param>
        /// <param name="rpcType">The type of RPC (e.g., TargetRPC)</param>
        /// <param name="method">The method name (for RPCs)</param>
        /// <returns>A detailed string representation of the packet data</returns>
        private string GetRpcOrBroadcastDataString(BitPacker packer, Type type, RPCType rpcType, string method = null)
        {
            if (packer == null || packer.length == 0)
                return "Empty packet";

            // Check if we can use cached result
            if (lastRpcPacker == packer && lastRpcType == type && lastRpcMethod == method && lastRpcRpcType == rpcType)
            {
                return lastRpcDataString;
            }

            int oldPos = packer.positionInBits;
            packer.ResetPositionAndMode(true);

            string result;
            if (!string.IsNullOrEmpty(method))
            {
                result = PrintRPC(type, packer, method, rpcType);
            }
            else
            {
                result = PrintBroadcast(type, packer);
            }

            // Cache the result
            lastRpcPacker = packer;
            lastRpcType = type;
            lastRpcMethod = method;
            lastRpcRpcType = rpcType;
            lastRpcDataString = result;

            packer.SetBitPosition(oldPos);
            return result;
        }

        static readonly Dictionary<Type, object> _deserializedObjects = new Dictionary<Type, object>();

        static bool ShouldIgnore(RPCType rpcType, Type paramType, int index, int count)
        {
            if (index == count - 1 && paramType == typeof(RPCInfo))
                return true;

            if (index == 0 && rpcType == RPCType.TargetRPC && paramType == typeof(PlayerID))
                return true;

            return false;
        }

        private static string PrintRPC(Type type, BitPacker tempPacker, string methodName, RPCType rpcType)
        {
            var method = type.GetMethod(methodName);

            if (method == null)
                return $"Failed to find method {methodName} in type {type.Name}";

            var parameters = method.GetParameters();
            var _sb = new StringBuilder();

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramType = param.ParameterType;
                var paramName = param.Name;

                if (paramType.IsGenericParameter)
                    continue;

                if (ShouldIgnore(rpcType, paramType, i, parameters.Length))
                    continue;

                object obj = _deserializedObjects.GetValueOrDefault(paramType);
                Packer.Read(tempPacker, paramType, ref obj);
                _deserializedObjects[paramType] = obj;

                _sb.AppendLine($"Parameter {i + 1:00}: {paramName} ({paramType.Name}) = {obj}");
            }

            return _sb.ToString();
        }

        private static string PrintBroadcast(Type type, BitPacker tempPacker)
        {
            uint typeIdx = default;
            object obj = _deserializedObjects.GetValueOrDefault(type);
            Packer<uint>.Read(tempPacker, ref typeIdx);
            Packer.Read(tempPacker, type, ref obj);
            _deserializedObjects[type] = obj;
            return $"{obj}";
        }

        #endregion
    }
}
