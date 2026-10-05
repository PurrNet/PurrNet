using PurrNet.Editor;
using UnityEditor;
using UnityEngine;

namespace PurrNet.StateMachine.InspectorEditor
{
    [CustomEditor(typeof(StateMachine))]
    public class StateMachineEditor : NetworkIdentityInspector
    {
        private StateMachine _stateMachine;
        SerializedProperty _ownerAuthProperty;
        SerializedProperty _statesProperty;

        protected override void OnEnable()
        {
            base.OnEnable();
            _stateMachine = target as StateMachine;
            _statesProperty = serializedObject.FindProperty("_states");
            _ownerAuthProperty = serializedObject.FindProperty("_ownerAuth");

        }

        public override void OnInspectorGUI()
        {
            DrawInspectorExtras();
        }

        protected override string[] editorAttributesExcludedProperties => new[] { "_ownerAuth", "_states" };

        protected override void DrawInspectorExtras()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            if (Application.isPlaying)
                EditorGUI.BeginDisabledGroup(true);

            EditorGUILayout.PropertyField(_ownerAuthProperty, new GUIContent("Owner Auth"));
            EditorGUILayout.PropertyField(_statesProperty, new GUIContent("States"), true);

            if (Application.isPlaying)
                EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(target);
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("State information available during Play Mode", MessageType.Info);
                return;
            }

            if (Application.isPlaying && _stateMachine && _stateMachine.currentState.stateId >= 0 && _stateMachine.states.Count > _stateMachine.currentState.stateId)
            {
                DrawStateMachineInfo();
                DrawStateControls();
                DrawNetworkStatus();
            }

            DrawIdentityInspector();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStateMachineInfo()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("State Machine Status", EditorStyles.boldLabel);

                // Current State
                var currentState = _stateMachine.currentState;
                EditorGUILayout.LabelField("Current State:",
                    currentState.stateId >= 0 ? _stateMachine.states[currentState.stateId].GetType().Name : "None");

                // Draw state data if exists
                if (currentState.data != null)
                {
                    EditorGUI.indentLevel++;
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("State Data:", EditorStyles.boldLabel);
                        DrawReadOnlyObject(currentState.data);
                    }

                    EditorGUI.indentLevel--;
                }

                // Previous/Next State preview
                if (currentState.stateId >= 0)
                {
                    // Previous state - use actual previous state
                    string previousState = "None";
                    if (_stateMachine.previousStateId >= 0 &&
                        _stateMachine.previousStateId < _stateMachine.states.Count)
                        previousState = _stateMachine.states[_stateMachine.previousStateId].GetType().Name;
                    EditorGUILayout.LabelField("Previous State:", previousState);

                    // Next state
                    var nextIdx = currentState.stateId < _stateMachine.states.Count - 1 ? currentState.stateId + 1 : 0;
                    EditorGUILayout.LabelField("Next State:", _stateMachine.states[nextIdx].GetType().Name);
                }
            }
        }

        private void DrawStateControls()
        {
            if (!Application.isPlaying || !_stateMachine.IsController(_stateMachine.ownerAuth))
                return;

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("State Controls", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Previous"))
                        _stateMachine.Previous();

                    if (GUILayout.Button("Next"))
                        _stateMachine.Next();
                }
            }
        }

        private void DrawNetworkStatus()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Network Status", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Role:", _stateMachine.isServer ? "Server" : "Client");
            }
        }

        private void DrawReadOnlyObject(object obj)
        {
            if (obj == null) return;

            if (obj is Object unityObj)
            {
                var so = new SerializedObject(unityObj);
                var iterator = so.GetIterator();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(iterator, true);
                    }

                    enterChildren = false;
                }
            }
            else
            {
                if (obj.GetType().IsPrimitive || obj is string)
                {
                    EditorGUILayout.LabelField("Value", obj.ToString());
                }
                else
                {
                    foreach (var field in obj.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                                                                  System.Reflection.BindingFlags.Public))
                    {
                        EditorGUILayout.LabelField(field.Name, field.GetValue(obj)?.ToString() ?? "null");
                    }
                }
            }
        }
    }
}
