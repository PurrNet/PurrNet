using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomPropertyDrawer(typeof(SyncStatus))]
    public class SyncStatusInspector : PropertyDrawer
    {
        const float PROGRESS_HEIGHT = 16f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return PROGRESS_HEIGHT;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var percent = property.FindPropertyRelative("percent").floatValue;

            if (percent <= 0f)
                 EditorGUI.ProgressBar(position, percent, "Idle");
            else EditorGUI.ProgressBar(position, percent, percent >= 1f ? "Done!" : "Downloading");
            EditorGUI.EndProperty();
        }
    }
}
