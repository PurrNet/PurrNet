using System.Collections.Generic;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(NetworkManager), true)]
    public class NetworkManagerInspector : UnityEditor.Editor
    {
        private SerializedProperty _scriptProp;
        private SerializedProperty _startServerFlags;
        private SerializedProperty _startClientFlags;
        private SerializedProperty _stopPlayingOnDisconnect;
        private SerializedProperty _cookieScope;
        private SerializedProperty _dontDestroyOnLoad;
        private SerializedProperty _networkPrefabs;
        private SerializedProperty _addressableNetworkPrefabs;
        private SerializedProperty _networkAssets;
        private SerializedProperty _networkRules;
        private SerializedProperty _authenticator;
        private SerializedProperty _transport;
        private SerializedProperty _tickRate;
        private SerializedProperty _visibilityRules;
        private SerializedProperty _patchLingeringProcessBug;

        private bool _showStatusFoldout = true;
        private bool _showPlayersFoldout;
        private readonly Dictionary<object, bool> _playerFoldouts = new Dictionary<object, bool>();

        // Cache for performance
        private NetworkManager _networkManager;
        private ConnectionState _lastServerState;
        private ConnectionState _lastClientState;
        private int _lastPlayerCount;
        private float _nextRepaintTime;
        private const float REPAINT_INTERVAL = 0.5f; // Repaint every 500ms

        private void OnEnable()
        {
            _scriptProp = serializedObject.FindProperty("m_Script");
            _startServerFlags = serializedObject.FindProperty("_startServerFlags");
            _startClientFlags = serializedObject.FindProperty("_startClientFlags");
            _stopPlayingOnDisconnect = serializedObject.FindProperty("_stopPlayingOnDisconnect");
            _cookieScope = serializedObject.FindProperty("_cookieScope");
            _dontDestroyOnLoad = serializedObject.FindProperty("_dontDestroyOnLoad");
            _networkPrefabs = serializedObject.FindProperty("_networkPrefabs");
            _addressableNetworkPrefabs = serializedObject.FindProperty("_addressableNetworkPrefabs");
            _networkAssets = serializedObject.FindProperty("_networkAssets");
            _networkRules = serializedObject.FindProperty("_networkRules");
            _transport = serializedObject.FindProperty("_transport");
            _tickRate = serializedObject.FindProperty("_tickRate");
            _visibilityRules = serializedObject.FindProperty("_visibilityRules");
            _patchLingeringProcessBug = serializedObject.FindProperty("_patchLingeringProcessBug");
            _authenticator = serializedObject.FindProperty("_authenticator");

            _networkManager = (NetworkManager)target;

            // Only update when playing
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.update += CheckForStateChanges;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= CheckForStateChanges;
            }
        }

        private void CheckForStateChanges()
        {
            if (_networkManager == null) return;

            bool needsRepaint = false;

            if (_lastServerState != _networkManager.serverState)
            {
                _lastServerState = _networkManager.serverState;
                needsRepaint = true;
            }

            if (_lastClientState != _networkManager.clientState)
            {
                _lastClientState = _networkManager.clientState;
                needsRepaint = true;
            }

            if (_lastPlayerCount != _networkManager.playerCount)
            {
                _lastPlayerCount = _networkManager.playerCount;
                needsRepaint = true;
            }

            if (needsRepaint || Time.realtimeSinceStartup >= _nextRepaintTime)
            {
                Repaint();
                _nextRepaintTime = Time.realtimeSinceStartup + REPAINT_INTERVAL;
            }
        }

        public override void OnInspectorGUI()
        {
            if (_networkManager == null)
                _networkManager = (NetworkManager)target;

            if (_networkManager.networkRules == null)
            {
                DrawInitialSetup();
                return;
            }

            serializedObject.Update();

            DrawHeaderSection(_networkManager, _scriptProp);

            if (Application.isPlaying)
                RenderStartStopButtons(_networkManager);
            DrawConfigurationSection();

            DrawRuntimeSettings();

            if (_showStatusFoldout)
                DrawStatusFoldout();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInitialSetup()
        {
            GUILayout.Label("Network Rules", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            const string description = "Set the network rules of your network manager. This can be changed later. ";
            GUILayout.Label(description, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.Space(10);
            GUI.backgroundColor = Color.yellow;
            EditorGUILayout.PropertyField(_networkRules, new GUIContent("Network Rules"));
            serializedObject.ApplyModifiedProperties();
        }

        public static void DrawHeaderSection(INetworkManager manager, SerializedProperty scriptProp)
        {
            bool willStartServer = manager.shouldAutoStartServer;
            bool willStartClient = manager.shouldAutoStartClient;
            string status = willStartClient && willStartServer ? "HOST" :
                willStartClient ? "CLIENT" :
                willStartServer ? "SERVER" : "NONE";

            if (scriptProp != null)
            {
                GUI.enabled = false;
                EditorGUILayout.PropertyField(scriptProp, true);
                GUI.enabled = true;
            }

            GUI.color = willStartClient && willStartServer ? Color.green :
                willStartClient ? Color.blue :
                willStartServer ? Color.red : Color.white;
            GUILayout.BeginVertical("box");
            GUI.color = Color.white;
            EditorGUILayout.LabelField($"During play mode this instance will start as a <b>{status}</b>",
                new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.EndVertical();
        }

        private void DrawConfigurationSection()
        {
            EditorGUILayout.PropertyField(_startServerFlags);
            EditorGUILayout.PropertyField(_startClientFlags);
            EditorGUILayout.PropertyField(_cookieScope);

            bool isRunning = _networkManager && (_networkManager.isClient || _networkManager.isServer);
            GUI.enabled = !isRunning;

            EditorGUILayout.PropertyField(_dontDestroyOnLoad);
            EditorGUILayout.PropertyField(_transport);
            DrawNetworkPrefabs();
            if(_addressableNetworkPrefabs != null)
                EditorGUILayout.PropertyField(_addressableNetworkPrefabs);
            DrawNetworkAssets();
            EditorGUILayout.PropertyField(_networkRules);
            EditorGUILayout.PropertyField(_visibilityRules);
            EditorGUILayout.PropertyField(_authenticator);

            GUI.enabled = true;
        }

        private void DrawRuntimeSettings()
        {
            bool isDisconnected = _networkManager.serverState == ConnectionState.Disconnected &&
                                  _networkManager.clientState == ConnectionState.Disconnected;

            GUI.enabled = isDisconnected;
            RenderTickSlider();
            EditorGUILayout.PropertyField(_stopPlayingOnDisconnect);
            EditorGUILayout.PropertyField(_patchLingeringProcessBug);
            GUI.enabled = true;

            if (Application.isPlaying)
                _showStatusFoldout = EditorGUILayout.Foldout(_showStatusFoldout, "Status");
        }

        private void DrawNetworkPrefabs()
        {
            EditorGUILayout.BeginHorizontal();
            Color originalBgColor = GUI.backgroundColor;

            if (_networkPrefabs.objectReferenceValue == null)
                GUI.backgroundColor = Color.yellow;

            EditorGUILayout.PropertyField(_networkPrefabs);
            GUI.backgroundColor = originalBgColor;

            if (_networkPrefabs.objectReferenceValue == null)
            {
                if (GUILayout.Button("New", GUILayout.Width(50)))
                    CreateNewNetworkPrefabs();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNetworkAssets()
        {
            EditorGUILayout.BeginHorizontal();
            Color originalBgColor = GUI.backgroundColor;

            EditorGUILayout.PropertyField(_networkAssets);
            GUI.backgroundColor = originalBgColor;

            if (_networkAssets.objectReferenceValue == null)
            {
                if (GUILayout.Button("New", GUILayout.Width(50)))
                    CreateNewNetworkAssets();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusFoldout()
        {
            if (!_networkManager.isServer && !_networkManager.isClient)
                return;

            if (_networkManager.players == null)
                return;

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField("Server State:", _networkManager.serverState.ToString());
            EditorGUILayout.LabelField("Client State:", _networkManager.clientState.ToString());
            EditorGUILayout.LabelField("Player Count:", _networkManager.playerCount.ToString());

            var players = _networkManager.players;
            if (players != null && players.Count > 0)
            {
                DrawPlayersSection(players);
            }
            else
            {
                EditorGUILayout.LabelField("No players connected.");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPlayersSection(IReadOnlyCollection<PlayerID> players)
        {
            _showPlayersFoldout = EditorGUILayout.Foldout(_showPlayersFoldout, $"Players ({players.Count})");
            if (!_showPlayersFoldout) return;

            foreach (var playerId in players)
            {
                EditorGUI.indentLevel++;
                if (!_playerFoldouts.ContainsKey(playerId))
                    _playerFoldouts[playerId] = false;

                _playerFoldouts[playerId] = EditorGUILayout.Foldout(_playerFoldouts[playerId], $"Player: {playerId}");
                if (_playerFoldouts[playerId])
                {
                    DrawPlayerDetails(playerId);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawPlayerDetails(PlayerID playerId)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Owned Objects:",
                _networkManager.GetAllPlayerOwnedIds(playerId, _networkManager.isServer).Count.ToString());

            if (!_networkManager.isServer)
            {
                EditorGUI.indentLevel--;
                return;
            }

            if (_networkManager.TryGetPlayerScenes(playerId, out var scenes) && scenes.Length > 0)
            {
                DrawPlayerScenes(scenes);
            }
            else
            {
                EditorGUILayout.LabelField("Scenes:", "None");
            }

            EditorGUI.indentLevel--;
        }

        private void DrawPlayerScenes(SceneID[] scenes)
        {
            EditorGUILayout.LabelField("Scenes (SceneId):");
            foreach (var sceneId in scenes)
            {
                if (!_networkManager.TryGetScene(sceneId, out var scene))
                    continue;

                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"- {scene.name} ({sceneId})");
                EditorGUI.indentLevel--;
            }
        }

        private void RenderTickSlider()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.IntSlider(_tickRate, 1, 128, new GUIContent("Tick Rate"));
        }

        public static void RenderStartStopButtons(INetworkManager manager)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;

            RenderServerButton(manager);
            RenderClientButton(manager);

            GUI.color = Color.white;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

#if PURRNET_CONNECTION_DEBUG
            GUILayout.BeginHorizontal();

            // draw buttons for all actions, independnt of state
            if (GUILayout.Button("Start Server", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                manager.StartServer();

            if (GUILayout.Button("Stop Server", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                manager.StopServer();

            if (GUILayout.Button("Start Client", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                manager.StartClient();

            if (GUILayout.Button("Stop Client", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                manager.StopClient();

            GUILayout.EndHorizontal();
#endif
        }

        private static void RenderServerButton(INetworkManager manager)
        {
            switch (manager.serverState)
            {
                case ConnectionState.Disconnected:
                    GUI.color = Color.white;
                    if (GUILayout.Button("Start Server", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                        manager.StartServer();
                    break;
                case ConnectionState.Disconnecting:
                    GUI.color = new Color(1f, 0.5f, 0f);
                    GUI.enabled = false;
                    GUILayout.Button("Stopping Server", GUILayout.Width(10), GUILayout.ExpandWidth(true));
                    break;
                case ConnectionState.Connecting:
                    GUI.color = Color.yellow;
                    GUI.enabled = false;
                    GUILayout.Button("Starting Server", GUILayout.Width(10), GUILayout.ExpandWidth(true));
                    break;
                case ConnectionState.Connected:
                    GUI.color = Color.green;
                    if (GUILayout.Button("Stop Server", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                        manager.StopServer();
                    break;
            }


        }

        private static void RenderClientButton(INetworkManager manager)
        {
            switch (manager.clientState)
            {
                case ConnectionState.Disconnected:
                    GUI.color = Color.white;
                    if (GUILayout.Button("Start Client", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                        manager.StartClient();
                    break;
                case ConnectionState.Disconnecting:
                    GUI.color = new Color(1f, 0.5f, 0f);
                    GUI.enabled = false;
                    GUILayout.Button("Stopping Client", GUILayout.Width(10), GUILayout.ExpandWidth(true));
                    break;
                case ConnectionState.Connecting:
                    GUI.color = Color.yellow;
                    GUI.enabled = false;
                    GUILayout.Button("Starting Client", GUILayout.Width(10), GUILayout.ExpandWidth(true));
                    break;
                case ConnectionState.Connected:
                    GUI.color = Color.green;
                    if (GUILayout.Button("Stop Client", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                        manager.StopClient();
                    break;
            }
        }

        private void CreateNewNetworkPrefabs()
        {
            string folderPath = "Assets";
            Object prefabsFolder = null;
            string[] prefabsFolders = AssetDatabase.FindAssets("t:Folder Prefabs");

            foreach (string guid in prefabsFolders)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if ((path.ToLower().EndsWith("/prefabs") || path.ToLower().EndsWith("/_prefabs")) &&
                    path.Split('/').Length == 2)
                {
                    folderPath = path;
                    prefabsFolder = AssetDatabase.LoadAssetAtPath<Object>(path);
                    break;
                }
            }

            var networkPrefabs = ScriptableObject.CreateInstance<NetworkPrefabs>();

            if (prefabsFolder != null)
            {
                networkPrefabs.folder = prefabsFolder;
            }

            string assetPath = $"{folderPath}/NetworkPrefabs.asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(networkPrefabs, assetPath);
            AssetDatabase.SaveAssets();

            _networkPrefabs.objectReferenceValue = networkPrefabs;
            serializedObject.ApplyModifiedProperties();

            EditorGUIUtility.PingObject(networkPrefabs);
        }

        private void CreateNewNetworkAssets()
        {
            string folderPath = "Assets";

            var networkAssets = ScriptableObject.CreateInstance<NetworkAssets>();

            string assetPath = $"{folderPath}/NetworkAssets.asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(networkAssets, assetPath);
            AssetDatabase.SaveAssets();

            _networkAssets.objectReferenceValue = networkAssets;
            serializedObject.ApplyModifiedProperties();

            EditorGUIUtility.PingObject(networkAssets);
        }
    }
}
