#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Editor;
using PurrNet.Transports;
using PurrConnectionState = PurrNet.Transports.ConnectionState;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using Steamworks;
#endif
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PurrNet.Steam.Editor
{
    [CustomEditor(typeof(SteamTransport), true)]
    public class SteamTransportInspector : UnityEditor.Editor
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

#if STEAMWORKS_NET_PACKAGE && DISABLESTEAMWORKS
                EditorGUILayout.HelpBox("SteamWorks.NET is disabled. Please enable it to use this transport.", MessageType.Warning);
                if (GUILayout.Button("Enable SteamWorks.NET"))
                {
                    RemoveDefineSymbols("DISABLESTEAMWORKS");
                }
#else
                EditorGUILayout.HelpBox("SteamWorks.NET is not installed. Please install it to use this transport.",
                    MessageType.Warning);
                if (GUILayout.Button("Add SteamWorks.NET to Package Manager"))
                {
                    if (GitHelper.CheckGit())
                    {
                        Client.Add(
                            "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2024.8.0");
                        Client.Resolve();
                    }
                }
#endif
                GUI.enabled = true;
            }
            else
            {
                base.OnInspectorGUI();
                TransportInspector.DrawTransportStatus(generic);
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
                if (Application.isPlaying)
                {
                    if (GUILayout.Button("Copy my SteamID to Clipboard"))
                    {
                        string content = SteamUser.GetSteamID().ToString();
                        EditorGUIUtility.systemCopyBuffer = content;
                    }
                }
#endif
            }
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
 PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
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
            var generic = (SteamTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState += OnDirty;
        }

        private void OnDisable()
        {
            var generic = (SteamTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState -= OnDirty;
        }

        private void OnDirty(PurrConnectionState state, bool asServer)
        {
            Repaint();
        }
    }
}
