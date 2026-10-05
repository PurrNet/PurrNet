using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(RawNetManager), true)]
    public class RawNetManagerInspector : OptionalInspector
    {
        private static readonly string[] _editorAttributesExcludedProperties = { "m_Script" };

        protected override string[] editorAttributesExcludedProperties => _editorAttributesExcludedProperties;

        private SerializedProperty _scriptProp;

        private void OnEnable()
        {
            _scriptProp = serializedObject.FindProperty("m_Script");
        }

        static void DoDrawDefaultInspector(SerializedObject obj)
        {
            EditorGUI.BeginChangeCheck();
            obj.UpdateIfRequiredOrScript();
            var iterator = obj.GetIterator();
            for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
            {
                if (iterator.name == "m_Script")
                    continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
            obj.ApplyModifiedProperties();
            EditorGUI.EndChangeCheck();
        }

        public override void OnInspectorGUI()
        {
            var _networkManager = (RawNetManager)target;
            NetworkManagerInspector.DrawHeaderSection(_networkManager, _scriptProp);
            if (Application.isPlaying)
                NetworkManagerInspector.RenderStartStopButtons(_networkManager);
            if (!drawingEditorAttributesExtras)
                DoDrawDefaultInspector(serializedObject);
        }
    }
}
