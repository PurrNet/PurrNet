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

            DrawSoftParentInspector();

            var identity = target as NetworkIdentity;
            if (identity != null)
            {
                DrawIdentityInspector();
                GUI.enabled = true;
                DrawPurrButtons(identity);
            }
        }

        private void DrawSoftParentInspector()
        {
            NetworkIdentity parent = null;
            bool hasFirstParent = false;
            bool hasAnyParent = false;
            bool hasMixedParents = false;

            foreach (var currentTarget in targets)
            {
                if (currentTarget is not NetworkRigidbody rigidbody)
                    continue;

                var currentParent = rigidbody.softParentInstance;
                if (currentParent)
                    hasAnyParent = true;

                if (!hasFirstParent)
                {
                    hasFirstParent = true;
                    parent = currentParent;
                    continue;
                }

                if (parent != currentParent)
                    hasMixedParents = true;
            }

            if (!hasAnyParent && !hasMixedParents)
                return;

            GUILayout.Space(5);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Runtime Soft Parent", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.showMixedValue = hasMixedParents;
                        EditorGUILayout.ObjectField("Instance", parent, typeof(NetworkIdentity), true);
                        EditorGUI.showMixedValue = false;
                    }

                    using (new EditorGUI.DisabledScope(hasMixedParents || !parent))
                    {
                        if (GUILayout.Button("Ping", GUILayout.Width(45)))
                            EditorGUIUtility.PingObject(parent);

                        if (GUILayout.Button("Select", GUILayout.Width(55)))
                            Selection.activeObject = parent ? parent.gameObject : null;
                    }
                }

                if (!hasMixedParents && parent && !parent.isSpawned)
                    EditorGUILayout.HelpBox("Assigned, but not active until the soft parent is spawned.", MessageType.Info);
            }
        }
    }
}
