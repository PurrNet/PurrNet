using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;
using UnityToolbarExtender;

namespace PurrNet.Editor
{
    public static class PurrNetToolBarStatus
    {
        private static GUIContent _pebblesIcon;

        [InitializeOnLoadMethod]
        static void Init()
        {
#if !UNITY_6000_3_OR_NEWER
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
#else
            ToolbarExtender.toolbarGUI += OnToolbarGUI;
#endif

            PurrNetSettings.onSettingsChanged += OnSettingsChanged;
            NetworkManager.onAnyServerConnectionState += OnConnectionStateChanged;
            NetworkManager.onAnyClientConnectionState += OnConnectionStateChanged;

            _pebblesIcon = new GUIContent(Resources.Load<Texture2D>("purrlogo"));
        }

        private static void OnSettingsChanged(PurrNetSettings obj)
        {
            ToolbarExtender.RequestToolbarRepaint();

#if UNITY_PLAYMODE
            PlayModePatch.Repaint();
#endif
        }

        private static void OnConnectionStateChanged(ConnectionState state)
        {
            ToolbarExtender.RequestToolbarRepaint();
#if UNITY_PLAYMODE
            PlayModePatch.Repaint();
#endif
        }

        static string TryFindVersion()
        {
            var packagePath = AssetDatabase.GUIDToAssetPath("0ec978dbed50a6f4b9a57580867f1fae");

            if (string.IsNullOrEmpty(packagePath))
                return "v?";

            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(packagePath);

            if (textAsset == null)
                return "v?";

            var json = JObject.Parse(textAsset.text);
            return 'v' + (json["version"]?.ToString() ?? "?");
        }

        static string _version;

        static readonly List<GenericTransport> _transports = new ();

        static string[] _transportNames = Array.Empty<string>();

        static int _transportIndex;

        static void RefreshPossibleTransports()
        {
            _transports.Clear();

            if (NetworkManager.main == null)
                return;

            NetworkManager.main.GetComponentsInChildren<GenericTransport>(true, _transports);

            if (_transportNames.Length != _transports.Count)
                _transportNames = new string[_transports.Count];

            for (var i = 0; i < _transports.Count; i++)
            {
                if (NetworkManager.main.transport == _transports[i])
                    _transportIndex = i;
                _transportNames[i] = $"{i}: {_transports[i].GetType().Name}";
            }
        }

        public static void OnToolbarGUI()
        {
            var settings = PurrNetSettings.GetOrCreateSettings();
#if !UNITY_6000_3_OR_NEWER
            if (settings.toolbarMode == ToolbarMode.None)
                return;
#endif
            _version ??= TryFindVersion();

            GUILayout.FlexibleSpace();

            var manager = NetworkManager.main;

            GUILayout.BeginHorizontal();

            if (settings.toolbarMode == ToolbarMode.Compact)
            {
                GUILayout.Label(_pebblesIcon, GUILayout.Width(22), GUILayout.Height(22));
                GUILayout.Label(_version, GUILayout.ExpandWidth(false));
            }
            else
            {
                GUILayout.Label(_pebblesIcon, GUILayout.Width(22), GUILayout.Height(22));
                GUILayout.Label("PurrNet " + _version, GUILayout.ExpandWidth(false));
            }

            DrawConnectionButton(settings, manager, true);  // Server
            DrawConnectionButton(settings, manager, false); // Client

            if (settings.toolbarTransportDropDown)
            {
                bool valid = Application.isPlaying && NetworkManager.main;

                bool wasEnabled = GUI.enabled;
                if (!valid)
                    GUI.enabled = false;
                RefreshPossibleTransports();

                var newidx = EditorGUILayout.Popup(_transportIndex, _transportNames, GUILayout.Width(130));
                if (_transportIndex != newidx)
                {
                    var transport = _transports[newidx];
                    if (transport)
                    {
                        ChangeTransport(transport);
                        _transportIndex = newidx;
                    }
                }

                GUI.enabled = wasEnabled;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(20);

            if (IsClientOrServerTransitioning(manager)) {
                ToolbarExtender.RequestToolbarRepaint();
#if UNITY_PLAYMODE
                PlayModePatch.Repaint();
#endif
            }
        }

        private static async void ChangeTransport(GenericTransport transport)
        {
            try
            {
                if (!NetworkManager.main.isOffline)
                    NetworkManager.main.StopServer();
                while (NetworkManager.main.serverState != ConnectionState.Disconnected ||
                       NetworkManager.main.clientState != ConnectionState.Disconnected)
                    await Task.Yield();
                NetworkManager.main.transport = transport;
                NetworkManager.main.StartHost();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void DrawConnectionButton(PurrNetSettings settings, NetworkManager manager, bool isServer)
        {
            ConnectionState? state = manager != null ? (isServer ? manager.serverState : manager.clientState) : null;
            var isActive = manager != null && (isServer ? manager.isServer : manager.isClient);
            var isTransitioning = state is ConnectionState.Connecting or ConnectionState.Disconnecting;

            var color = state switch
            {
                ConnectionState.Connecting => Color.yellow,
                ConnectionState.Connected => Color.green,
                ConnectionState.Disconnecting => new Color(1, 0.5f, 0),
                _ => Color.white
            };

            if (settings.toolbarMode == ToolbarMode.Compact)
            {
            }

            string buttonText;

            if (settings.toolbarMode == ToolbarMode.Compact)
            {
                buttonText = isServer ? "S" : "C";
            }
            else
            {
                buttonText = isTransitioning ? state.ToString() :
                               isActive ? $"Stop {(isServer ? "Server" : "Client")}" :
                               $"Start {(isServer ? "Server" : "Client")}";
            }

            GUI.enabled = manager != null && !isTransitioning;
            GUI.color = color;
            if (GUILayout.Button(buttonText, settings.toolbarMode == ToolbarMode.Compact ? GUILayout.Width(25) : GUILayout.Width(100)))
            {
                if (isServer)
                {
                    if (isActive) manager.StopServer();
                    else manager?.StartServer();
                }
                else
                {
                    if (isActive) manager.StopClient();
                    else manager?.StartClient();
                }
            }
            GUI.color = Color.white;
            GUI.enabled = true;
        }

        private static bool IsClientOrServerTransitioning(NetworkManager manager)
        {
            if (manager == null) return false;

            return manager.serverState is ConnectionState.Connecting or ConnectionState.Disconnecting ||
                   manager.clientState is ConnectionState.Connecting or ConnectionState.Disconnecting;
        }
    }
}
