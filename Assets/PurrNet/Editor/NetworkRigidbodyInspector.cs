using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(NetworkRigidbody))]
    [CanEditMultipleObjects]
    public class NetworkRigidbodyInspector : NetworkIdentityInspector
    {
        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();

            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.name == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iterator, true);
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            serializedObject.ApplyModifiedProperties();

            var identity = target as NetworkIdentity;
            if (identity != null)
            {
                DrawIdentityInspector();
                GUI.enabled = true;
                DrawPurrButtons(identity);
            }
        }
    }
}
