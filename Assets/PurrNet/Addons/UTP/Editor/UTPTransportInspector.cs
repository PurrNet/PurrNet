#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID)
#define DISABLEUTPWORKS
#endif

#if UTP_LOBBYRELAY
#define UTP_NET_PACKAGE
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using PurrNet.Editor;
using PurrNet.Transports;

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
// using Unity Transport with Relay for P2P connectivity
#endif

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PurrNet.UTP.Editor
{
    /// <summary>
    /// Custom inspector for the UTP Transport component in the Unity Editor.
    /// Provides UI for configuring the transport, checking package dependencies,
    /// and displaying runtime connection status.
    /// </summary>
    [CustomEditor(typeof(UTPTransport), true)]
    public class UTPTransportInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var generic = (GenericTransport)target;
            if (!generic.isSupported)
            {
                GUI.enabled = false;
                base.OnInspectorGUI();

                if (!EditorApplication.isCompiling)
                    GUI.enabled = true;

                GUILayout.Space(10);
#if UTP_NET_PACKAGE && DISABLEUTPWORKS
                EditorGUILayout.HelpBox("Unity UTP is disabled. Please enable it to use this transport.", MessageType.Warning);
                if (GUILayout.Button("Enable Unity UTP"))
                {
                    RemoveDefineSymbols("DISABLEUTPWORKS");
                }
#else
                EditorGUILayout.HelpBox("Unity UTP dependencies are not installed. Please install it to use this transport.",
                    MessageType.Warning);
                if (GUILayout.Button("Add Unity UTP dependencies to Package Manager") && !IsPackageInstalled("com.unity.transport"))
                {
                    Client.Add("com.unity.transport");
                    Client.Resolve();
                }
#endif
                GUI.enabled = true;
            }
            else
            {
                base.OnInspectorGUI();
                TransportInspector.DrawTransportStatus(generic);
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
                if (Application.isPlaying)
                {
                    var utpTransport = (UTPTransport)target;
                    EditorGUILayout.Space(10);
                    EditorGUILayout.LabelField("Connection Info", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Client State: {utpTransport.clientState}");
                    EditorGUILayout.LabelField($"Listener State: {utpTransport.listenerState}");
                    EditorGUILayout.LabelField($"Connections: {utpTransport.connections.Count}");
                }
#endif
            }
        }
        
        bool IsPackageInstalled(string packageName)
        {
            var allPackages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            return allPackages.Any(p => p.name == packageName);
        }

        [UsedImplicitly]
        static void RemoveDefineSymbols(string symbol)
        {
            string currentDefines;
            HashSet<string> defines;
            
#if UNITY_2021_1_OR_NEWER
            currentDefines =
                PlayerSettings.GetScriptingDefineSymbols(
                    NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
#else
            currentDefines =
                PlayerSettings.GetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
            defines = new HashSet<string>(currentDefines.Split(';'));
            defines.Remove(symbol);
            
            string newDefines = string.Join(";", defines);
            if (newDefines != currentDefines)
            {
#if UNITY_2021_1_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(
                    NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup),
                    newDefines);
#else
			    PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, newDefines);
#endif
            }
        }

        private void OnEnable()
        {
            var generic = (UTPTransport)target;
            if (generic && generic.transport != null)
                generic.transport.onConnectionState += OnDirty;
        }

        private void OnDisable()
        {
            var generic = (UTPTransport)target;
            if (generic && generic.transport != null)
                generic.transport.onConnectionState -= OnDirty;
        }
        
        private void OnDirty(ConnectionState state, bool asServer)
        {
            Repaint();
        }
    }
}
    
