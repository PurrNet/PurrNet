using System;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(RelayTransport), true)]
    public class RelayTransportInspector : UnityEditor.Editor
    {
        private SerializedProperty _masterServer;
        private SerializedProperty _roomName;
        private SerializedProperty _region;
        private SerializedProperty _host;
        private SerializedProperty _pollEventsInUpdate;

        private bool _lookingForBestRegion;
        static string[] _regions = Array.Empty<string>();
        static string[] _hosts = Array.Empty<string>();

        void OnEnable()
        {
            _masterServer = serializedObject.FindProperty("_masterServer");
            _roomName = serializedObject.FindProperty("_roomName");
            _region = serializedObject.FindProperty("_region");
            _host = serializedObject.FindProperty("_host");
            _pollEventsInUpdate = serializedObject.FindProperty("_pollEventsInUpdate");

            if (_regions.Length == 0)
                LoadRegions();
        }

        public static string _bestRegion;
        static bool _loadingRegions;

        async void LoadRegions()
        {
            try
            {
                if (_loadingRegions)
                    return;

                _loadingRegions = true;
                var servers = await RelayTransportUtils.GetRelayServersAsync(_masterServer.stringValue);

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

        static int RegionId(string region, string host)
        {
            for (var i = 0; i < _regions.Length; i++)
            {
                if (_regions[i] == region)
                {
                    if (_hosts[i] != host)
                        return -1;
                    return i;
                }
            }

            return -1;
        }

        private async void DelayedFindBestRegion()
        {
            try
            {
                // Wait for regions to load first with timeout
                int waitCount = 0;
                while (_loadingRegions && waitCount < 50) // 5 second timeout (50 * 100ms)
                {
                    await System.Threading.Tasks.Task.Delay(100);
                    waitCount++;
                }
                
                // Small additional delay to ensure everything is ready
                await System.Threading.Tasks.Task.Delay(500);
                
                FindBestRegion();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async void FindBestRegion()
        {
            try
            {
                if (_lookingForBestRegion)
                    return;

                _lookingForBestRegion = true;
                
                // Use the current value from the inspector, not cached
                serializedObject.ApplyModifiedProperties();
                var currentMasterServer = _masterServer.stringValue;

                var server = await RelayTransportUtils.GetRelayServerAsync(currentMasterServer, null);

                _region.stringValue = server.region;
                _bestRegion = server.region;
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

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            var transport = (RelayTransport)target;

            var previousMasterServer = _masterServer.stringValue?.TrimEnd('/');
            EditorGUILayout.PropertyField(_masterServer);
            
            // Normalize the URL by removing trailing slash for comparison
            var currentMasterServer = _masterServer.stringValue?.TrimEnd('/');
            
            // If master server changed, clear regions cache to force reload and find best region
            if (previousMasterServer != currentMasterServer && !string.IsNullOrEmpty(currentMasterServer))
            {
                _regions = Array.Empty<string>();
                _hosts = Array.Empty<string>();
                LoadRegions();
                DelayedFindBestRegion();
            }

            var server = _masterServer.stringValue;
            if (Uri.TryCreate(server, UriKind.Absolute, out var url) && url.Host.EndsWith("riten.dev"))
            {
                // draw help box saying this is meant for dev use only
                EditorGUILayout.HelpBox("This server is meant for development use only.\n" +
                                        "Usage in production is strictly prohibited.\n" +
                                        "You need to host your own relay servers for production.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(_roomName);

            bool oldEnabled = GUI.enabled;
            if (_lookingForBestRegion)
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
                int region = RegionId(transport.region, transport.host);
                var newRegion = EditorGUILayout.Popup("Region", region, _regions);

                if (newRegion < 0 && _regions.Length > 0)
                    newRegion = 0;

                if (region != newRegion && newRegion >= 0 && newRegion < _regions.Length)
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

            EditorGUILayout.PropertyField(_pollEventsInUpdate);

            TransportInspector.DrawTransportStatus(transport);

            serializedObject.ApplyModifiedProperties();
        }
    }
}