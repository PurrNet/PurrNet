using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(CompositeTransport), true)]
    public class CompositeInspector : OptionalInspector
    {
        private static readonly string[] _editorAttributesExcludedProperties =
            { "_transports", "_ensureAllServersStart" };

        protected override string[] editorAttributesExcludedProperties => _editorAttributesExcludedProperties;

        private SerializedProperty _ensureAllServersStart;
        private SerializedProperty _transportArray;

        private void OnEnable()
        {
            _transportArray = serializedObject.FindProperty("_transports");
            _ensureAllServersStart = serializedObject.FindProperty("_ensureAllServersStart");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            serializedObject.Update();

            var composite = (CompositeTransport)target;
            if (composite.clientState != ConnectionState.Disconnected ||
                composite.listenerState != ConnectionState.Disconnected)
                GUI.enabled = false;

            EditorGUILayout.PropertyField(_ensureAllServersStart);
            EditorGUILayout.PropertyField(_transportArray);
            GUI.enabled = true;

            TransportInspector.DrawTransportStatus(composite);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
