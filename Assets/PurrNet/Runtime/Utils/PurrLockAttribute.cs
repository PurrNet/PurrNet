using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace PurrNet.Utils
{
    public class PurrLockAttribute : PropertyAttribute
    {
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(PurrLockAttribute))]
    public class PurrLockDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var field = new PropertyField(property);
            field.BindProperty(property.serializedObject);
            root.Add(field);
            void RefreshLock()
            {
                root.SetEnabled(!Application.isPlaying ||
                    PrefabUtility.IsPartOfPrefabAsset(property.serializedObject.targetObject));
            }
            RefreshLock();
            root.schedule.Execute(RefreshLock).Every(100);
            return root;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool shouldLock = Application.isPlaying &&
                              !PrefabUtility.IsPartOfPrefabAsset(property.serializedObject.targetObject);

            if (shouldLock)
            {
                var old = GUI.enabled;
                if (old) GUI.enabled = false;
                EditorGUI.PropertyField(position, property, label);
                if (old) GUI.enabled = true;
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }
        }
    }
#endif
}
