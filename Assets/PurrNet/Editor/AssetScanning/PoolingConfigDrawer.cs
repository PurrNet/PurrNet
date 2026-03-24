#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomPropertyDrawer(typeof(PoolingConfig))]
    public class PoolingConfigDrawer : PropertyDrawer
    {
        private const float TOGGLE_WIDTH = 20f;
        private const float SPACING = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var pooledProp = property.FindPropertyRelative("pooled");
            var warmupProp = property.FindPropertyRelative("warmupCount");

            float toggleX = position.x;
            float warmupWidth = 60f;

            pooledProp.boolValue = EditorGUI.Toggle(
                new Rect(toggleX, position.y, TOGGLE_WIDTH, position.height),
                pooledProp.boolValue);

            if (pooledProp.boolValue)
            {
                float warmupX = toggleX + TOGGLE_WIDTH + SPACING;
                float availableWidth = position.width - TOGGLE_WIDTH - SPACING;
                warmupWidth = Mathf.Min(warmupWidth, availableWidth);

                EditorGUI.PropertyField(
                    new Rect(warmupX, position.y, warmupWidth, position.height),
                    warmupProp, GUIContent.none);
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
