using System;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(PurrTransport), true)]
    public class PurrTransportInspector : OptionalInspector
    {
        private static readonly string[] _editorAttributesExcludedProperties =
        {
            "_masterServer", "_roomName", "_region", "_host", "_timeoutInSeconds", "_useNat",
            "_natResolveTimeout", "_webRtcStunServer", "_networkSimulation"
        };

        protected override string[] editorAttributesExcludedProperties => _editorAttributesExcludedProperties;

        private static readonly GUIContent _hostRelayLabel = new("Host relay", "This host's connection to the relay.");
        private static readonly GUIContent _relayBudgetLabel = new("Relay budget",
            "Players connected and traffic relayed this month across all of the linked project's rooms, " +
            "against what the project's plan allows — as the balancer last reported it. " +
            "Past a limit, new rooms and joins are refused.");

        private SerializedProperty _masterServer;
        private SerializedProperty _roomName;
        private SerializedProperty _region;
        private SerializedProperty _host;
        private SerializedProperty _timeoutInSeconds;
        private SerializedProperty _attemptDirectConnection;
        private SerializedProperty _natResolveTimeout;
        private SerializedProperty _webRtcStunServer;
        private bool _showDirectConnectionSettings;
        private SerializedProperty _networkSimulation;

        private bool _lookingForBestRegion;
        string[] _regions = Array.Empty<string>();
        string[] _hosts = Array.Empty<string>();

        void OnEnable()
        {
            _masterServer = serializedObject.FindProperty("_masterServer");
            _roomName = serializedObject.FindProperty("_roomName");
            _region = serializedObject.FindProperty("_region");
            _host = serializedObject.FindProperty("_host");
            _timeoutInSeconds = serializedObject.FindProperty("_timeoutInSeconds");
            _attemptDirectConnection = serializedObject.FindProperty("_useNat");
            _natResolveTimeout = serializedObject.FindProperty("_natResolveTimeout");
            _webRtcStunServer = serializedObject.FindProperty("_webRtcStunServer");
            _networkSimulation = serializedObject.FindProperty("_networkSimulation");

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                LoadRegions();
        }

        bool _loadingRegions;

        async void LoadRegions()
        {
            try
            {
                if (_loadingRegions)
                    return;

                _loadingRegions = true;
                var servers = await PurrTransportUtils.ActualGetRelayServersAsync(_masterServer.stringValue);

                if (servers.servers == null)
                {
                    _loadingRegions = false;
                    return;
                }

                _hosts = new string[servers.servers.Length];
                _regions = new string[servers.servers.Length];

                for (var i = 0; i < servers.servers.Length; i++)
                {
                    _hosts[i] = servers.servers[i].host;
                    _regions[i] = servers.servers[i].region;
                }

                _loadingRegions = false;
            }
            catch (Exception e)
            {
                _loadingRegions = false;
                Debug.LogException(e);
            }
        }

        // Match by region only. Returning -1 on a host mismatch made the popup fall
        // back to index 0 and write it back — "Find Best Region" could pick France
        // and the next repaint would silently turn it into Brazil (2026-09-23).
        int RegionId(string region)
        {
            for (var i = 0; i < _regions.Length; i++)
            {
                if (_regions[i] == region)
                    return i;
            }

            return -1;
        }

        private async void FindBestRegion()
        {
            try
            {
                if (_lookingForBestRegion)
                    return;

                _lookingForBestRegion = true;

                var server = await PurrTransportUtils.ActualGetRelayServerAsync(_masterServer.stringValue);

                _region.stringValue = server.region;
                _host.stringValue = server.host;
                serializedObject.ApplyModifiedProperties();

                _lookingForBestRegion = false;
            }
            catch (Exception e)
            {
                _lookingForBestRegion = false;
                Debug.LogException(e);
            }
        }

        float _lastMasterServerUpdate;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var transport = (PurrTransport)target;

            var oldMasterServer = _masterServer.stringValue;
            EditorGUILayout.PropertyField(_masterServer);

            if (oldMasterServer != _masterServer.stringValue)
                _lastMasterServerUpdate = Time.realtimeSinceStartup;

            if (_lastMasterServerUpdate != 0 && Time.realtimeSinceStartup - _lastMasterServerUpdate > 1)
            {
                LoadRegions();
                _lastMasterServerUpdate = 0;
            }

            EditorGUILayout.PropertyField(_roomName);

            bool oldEnabled = GUI.enabled;
            if (_lookingForBestRegion || _loadingRegions || _lastMasterServerUpdate != 0)
                GUI.enabled = false;

            EditorGUILayout.BeginHorizontal();

            if (_regions.Length == 0)
            {
                bool enabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUILayout.PropertyField(_region);
                GUI.enabled = enabled;
            }
            else
            {
                int region = RegionId(transport.region);
                var newRegion = EditorGUILayout.Popup("Region", region, _regions);

                // A known region whose stored host drifted (older serialization, or a
                // relay that moved) gets its host refreshed without changing region.
                if (region >= 0 && newRegion == region && _host.stringValue != _hosts[region])
                    _host.stringValue = _hosts[region];

                // Only an explicit pick changes the region; an unknown current value
                // shows as an empty popup instead of being replaced by the first entry.
                if (newRegion != region && newRegion >= 0 && newRegion < _regions.Length)
                {
                    _region.stringValue = _regions[newRegion];
                    _host.stringValue = _hosts[newRegion];
                }
            }

            if (GUILayout.Button("Find Best Region", GUILayout.ExpandWidth(false)))
                FindBestRegion();

            GUI.enabled = oldEnabled;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            GUILayout.Label(_host.stringValue);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            // Which PurrNet project this relay traffic belongs to. Only meaningful on
            // PurrNet's own fleet; a self-hosted balancer has no project concept.
            var server = _masterServer.stringValue;
            if (Uri.TryCreate(server, UriKind.Absolute, out var url) && url.Host.EndsWith("purrservers.com"))
                PurrTransportProjectSetup.Draw();

            EditorGUILayout.PropertyField(_timeoutInSeconds);
            EditorGUILayout.PropertyField(_attemptDirectConnection, new GUIContent("Connect Players Directly (P2P)",
                "Connect players straight to the host when possible, so game traffic skips the relay. " +
                "If a direct connection cannot be made, use the relay automatically. " +
                "Enable on both host and clients before connecting. " +
                "If an established direct connection is lost, that player disconnects."));

            if (_attemptDirectConnection.boolValue)
            {
                EditorGUI.indentLevel++;
                _showDirectConnectionSettings = EditorGUILayout.Foldout(_showDirectConnectionSettings,
                    "Advanced Direct Connection Settings", true);
                if (_showDirectConnectionSettings)
                {
                    EditorGUILayout.PropertyField(_natResolveTimeout, new GUIContent("UDP Connection Timeout",
                        "Seconds to attempt a direct UDP connection before using the relay. " +
                        "Keep this below the game's connection/authentication timeout."));
                    if (_natResolveTimeout.floatValue < 1f)
                        _natResolveTimeout.floatValue = 1f;
                    EditorGUILayout.PropertyField(_webRtcStunServer, new GUIContent("STUN Server",
                        "Helps WebRTC peers behind routers find a direct route. Leave empty for local-network discovery only."));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_networkSimulation, new GUIContent("Network Simulation (UDP)"));

            if (GUILayout.Button("Refresh"))
                LoadRegions();

            TransportInspector.DrawTransportStatus(transport);

            DrawConnectionMode(transport);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConnectionMode(PurrTransport transport)
        {
            if (!Application.isPlaying)
                return;

            var clientLine = transport.clientLinkDescription;
            var hostLine = transport.hostLinkDescription;

            int total = transport.connections.Count;

            var usage = transport.relayUsage;
            if (clientLine == null && hostLine == null && total == 0 && usage == null)
                return;

            EditorGUILayout.Space(4);

            if (usage != null && usage.isValid)
            {
                var overPlayers = usage.players.allowed > 0 && usage.players.used >= usage.players.allowed;
                var overTraffic = usage.traffic.allowedBytes > 0 && usage.traffic.usedBytes >= usage.traffic.allowedBytes;
                var previous = GUI.color;
                if (overPlayers || overTraffic) GUI.color = new Color(1f, 0.6f, 0.4f);
                EditorGUILayout.LabelField(_relayBudgetLabel, new GUIContent(usage.ToString()), EditorStyles.wordWrappedLabel);
                GUI.color = previous;
            }

            if (clientLine != null)
                EditorGUILayout.LabelField("Client session", clientLine, EditorStyles.wordWrappedLabel);

            if (hostLine != null)
                EditorGUILayout.LabelField(_hostRelayLabel,
                    new GUIContent(hostLine), EditorStyles.wordWrappedLabel);

            if (total > 0)
            {
                EditorGUILayout.LabelField("Host links",
                    $"{transport.p2pConnectionCount} P2P / {total - transport.p2pConnectionCount} relay");

                foreach (var conn in transport.connections)
                {
                    var isP2p = transport.GetSessionLink(conn) == PurrTransport.SessionLink.P2P;
                    EditorGUILayout.LabelField($"    conn {conn.connectionId}",
                        isP2p ? $"P2P ({transport.GetConnectionProtocol(conn)})" : "Relay");
                }
            }
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
