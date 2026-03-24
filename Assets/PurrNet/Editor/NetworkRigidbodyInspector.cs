using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(NetworkRigidbody))]
    [CanEditMultipleObjects]
    public class NetworkRigidbodyInspector : NetworkIdentityInspector
    {
        private static readonly HashSet<string> _advancedOverriddenFields = new()
        {
            "_positionStrength",
            "_correctionRange",
            "_rotationStrength",
            "_acceptableRotationError"
        };

        private SerializedProperty _advancedSettingsProp;

#if TRI_INSPECTOR_PACKAGE || ODIN_INSPECTOR
        protected override void OnEnable()
#else
        protected override void OnEnable()
#endif
        {
            base.OnEnable();
            _advancedSettingsProp = serializedObject.FindProperty("_advancedSettings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();

            bool hasAdvanced = _advancedSettingsProp != null
                            && !_advancedSettingsProp.hasMultipleDifferentValues
                            && _advancedSettingsProp.objectReferenceValue != null;

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

                if (hasAdvanced && _advancedOverriddenFields.Contains(iterator.name))
                    continue;

                EditorGUILayout.PropertyField(iterator, true);

                if (hasAdvanced && iterator.name == "_advancedSettings")
                {
                    EditorGUILayout.HelpBox(
                        "Per-axis correction settings are managed by the advanced settings asset. " +
                        "The scalar correction fields are hidden.",
                        MessageType.Info);
                }
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
