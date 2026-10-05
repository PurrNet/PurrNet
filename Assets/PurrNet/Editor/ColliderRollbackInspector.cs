using UnityEditor;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(ColliderRollback), true)]
    public class ColliderRollbackInspector : OptionalInspector
    {
        private static readonly string[] _editorAttributesExcludedProperties =
            { "_storeHistoryInSeconds", "_autoAddAllChildren", "_colliders3D", "_colliders2D" };

        protected override string[] editorAttributesExcludedProperties => _editorAttributesExcludedProperties;

        private SerializedProperty _storeHistoryInSeconds;
        private SerializedProperty _autoAddAllChildren;
        private SerializedProperty _colliders3D;
        private SerializedProperty _colliders2D;

        private void OnEnable()
        {
            _storeHistoryInSeconds = serializedObject.FindProperty("_storeHistoryInSeconds");
            _autoAddAllChildren = serializedObject.FindProperty("_autoAddAllChildren");
            _colliders3D = serializedObject.FindProperty("_colliders3D");
            _colliders2D = serializedObject.FindProperty("_colliders2D");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            EditorGUILayout.PropertyField(_storeHistoryInSeconds);
            EditorGUILayout.PropertyField(_autoAddAllChildren);

            if (!_autoAddAllChildren.boolValue)
            {
                EditorGUILayout.PropertyField(_colliders3D);
                EditorGUILayout.PropertyField(_colliders2D);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
