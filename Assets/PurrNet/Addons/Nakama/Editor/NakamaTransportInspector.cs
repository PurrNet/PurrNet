using PurrNet.Editor;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Nakama.Editor
{
    [CustomEditor(typeof(NakamaTransport), true)]
    public class NakamaTransportInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var generic = (GenericTransport)target;

            EditorGUILayout.HelpBox(
                "Nakama Transport is intended for slower-paced games and is not ideal for fast-paced, real-time gameplay.",
                MessageType.Warning);

            if (!generic.isSupported)
            {
                GUI.enabled = false;
                base.OnInspectorGUI();

                if (!EditorApplication.isCompiling)
                    GUI.enabled = true;

                GUILayout.Space(10);

                PurrPackageQuickInstall.DrawInstallControls(
                    "com.heroiclabs.nakama-unity",
                    "Nakama",
                    "Nakama SDK is not installed. Please install it to use this transport.");

                GUI.enabled = true;
            }
            else
            {
                base.OnInspectorGUI();
                TransportInspector.DrawTransportStatus(generic);
            }
        }

        private void OnEnable()
        {
            var generic = (NakamaTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState += OnDirty;
        }

        private void OnDisable()
        {
            var generic = (NakamaTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState -= OnDirty;
        }

        private void OnDirty(ConnectionState state, bool asServer)
        {
            Repaint();
        }
    }
}
