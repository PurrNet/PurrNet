using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PurrNet.Utils
{
    public class PurrReadOnlyAttribute : PropertyAttribute
    {
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(SyncTimer))]
    public class SyncTimerDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var state = property.FindPropertyRelative("_state");
            var remaining = property.FindPropertyRelative("_remaining");
            var timerState = (TimerState) state.enumValueIndex;
            var remainingTime = remaining.floatValue;
            var labelContent = new GUIContent($"{timerState} ({remainingTime:0.00})");
            EditorGUI.LabelField(position, label, labelContent);
        }
    }
#endif

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(PurrReadOnlyAttribute))]
    public class PurrReadOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var old = GUI.enabled;
            if (old) GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            if (old) GUI.enabled = true;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
#endif
}
