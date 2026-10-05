using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(GenericTransport), true)]
    public class TransportInspector : OptionalInspector
    {
        public static void DrawLed(ConnectionState? state)
        {
            var white = Texture2D.whiteTexture;
            var color = state switch
            {
                null => Color.gray,
                ConnectionState.Connecting => Color.yellow,
                ConnectionState.Connected => Color.green,
                ConnectionState.Disconnecting => new Color(1, 0.5f, 0),
                _ => Color.red
            };

            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            var rect = GUILayoutUtility.GetLastRect();
            rect.height = EditorGUIUtility.singleLineHeight;

            const float padding = 5;

            rect.x += padding;
            rect.y += padding;

            rect.width -= padding * 2;
            rect.height -= padding * 2;

            GUI.DrawTexture(rect, white, ScaleMode.StretchToFill, true, 1f, color, 0, 10f);
        }

        public static void DrawLed(ConnectionState state)
        {
            var white = Texture2D.whiteTexture;
            var color = state switch
            {
                ConnectionState.Connecting => Color.yellow,
                ConnectionState.Connected => Color.green,
                ConnectionState.Disconnecting => new Color(1, 0.5f, 0),
                _ => Color.red
            };

            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            var rect = GUILayoutUtility.GetLastRect();
            rect.height = EditorGUIUtility.singleLineHeight;

            const float padding = 5;

            rect.x += padding;
            rect.y += padding;

            rect.width -= padding * 2;
            rect.height -= padding * 2;

            GUI.DrawTexture(rect, white, ScaleMode.StretchToFill, true, 1f, color, 0, 10f);
        }

        private void OnEnable()
        {
            var generic = (GenericTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState += OnDirty;
        }

        protected override void OnDisable()
        {
            var generic = (GenericTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState -= OnDirty;
            base.OnDisable();
        }

        private void OnDirty(ConnectionState state, bool asServer)
        {
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var generic = (GenericTransport)target;
            DrawTransportStatus(generic);
        }

        public static void DrawTransportStatus(GenericTransport generic)
        {
            var transport = generic.transport;

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Protocol Status", EditorStyles.boldLabel);

            if (!generic.isSupported)
            {
                EditorGUILayout.HelpBox("Transport is not supported on this platform", MessageType.Info);
                return;
            }

            GUILayout.BeginHorizontal();
            DrawLed(transport.listenerState);
            EditorGUILayout.LabelField("Listening");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawLed(transport.clientState);
            EditorGUILayout.LabelField("Connected");
            GUILayout.EndHorizontal();

#if PURRNET_CONNECTION_DEBUG
            GUILayout.BeginHorizontal();

            // draw buttons for all actions, independnt of state
            if (GUILayout.Button("Start Server", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                generic.StartServer();

            if (GUILayout.Button("Stop Server", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                generic.StopServer();

            if (GUILayout.Button("Start Client", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                generic.StartClient();

            if (GUILayout.Button("Stop Client", GUILayout.Width(10), GUILayout.ExpandWidth(true)))
                generic.StopClient();

            GUILayout.EndHorizontal();
#endif

            GUILayout.Space(10);
        }
    }
}
