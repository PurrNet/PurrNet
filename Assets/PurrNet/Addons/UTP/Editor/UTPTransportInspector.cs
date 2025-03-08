using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PurrNet.Editor;
using PurrNet.Transports;

namespace PurrNet.UTP.Editor {
    [CustomEditor(typeof(UTPTransport), true)]
    public class UTPTransportInspector : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            var generic = (GenericTransport)target;

#if !UTP_AUTH || !UTP_TRANSPORT || !UTP_RELAY
            GUI.enabled = false;
            base.OnInspectorGUI();

            if(!EditorApplication.isCompiling)
                GUI.enabled = true;

            GUILayout.Space(10);

#if UTP_TRANSPORT
            EditorGUILayout.HelpBox("UnityServices are not fully installed, only P2P connection will be supported. Please install if you wish to use Unity Relay with this transport.", MessageType.Warning);
#else
            EditorGUILayout.HelpBox("UnityServices are not installed. Please install to use this transport.", MessageType.Warning);
#endif
            if(GUILayout.Button("Add UnityServices to Package Manager")) {
                Client.Add("com.unity.services.multiplayer");
                Client.Add("com.unity.transport");

                //These auto install as dependencies of com.unity.services.multiplayer
                //Client.Add("com.unity.services.core");
                //Client.Add("com.unity.services.authentication");

                Client.Resolve();
            }

            GUI.enabled = true;
#else
            base.OnInspectorGUI();
            TransportInspector.DrawTransportStatus(generic);
#endif
        }

        private void OnEnable() {
            var generic = (UTPTransport)target;
            if(generic && generic.transform != null)
                generic.transport.onConnectionState += OnDirty;
        }

        private void OnDisable() {
            var generic = (UTPTransport)target;
            if(generic && generic.transform != null)
                generic.transport.onConnectionState -= OnDirty;
        }

        private void OnDirty(ConnectionState state, bool asServer) {
            Repaint();
        }
    }
}
