using System;
using UnityEngine;
using UnityEditor;

namespace PurrNet.Utils
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Property)]
    public class PurrDocsAttribute : PropertyAttribute
    {
        public readonly string url;

        public PurrDocsAttribute(string docsExtension)
        {
            url = docsExtension;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(PurrDocsAttribute))]
    public class PurrDocsDrawer : PropertyDrawer
    {
        private const float IconWidth = 20f;
        private static GUIContent iconContent;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            iconContent ??= EditorGUIUtility.IconContent("_Help");

            var iconRect = new Rect(position.x, position.y, IconWidth, EditorGUIUtility.singleLineHeight);
            var propertyRect = new Rect(position.x + IconWidth, position.y, position.width - IconWidth,
                position.height);

            if (GUI.Button(iconRect, iconContent, GUIStyle.none))
            {
                if (attribute is PurrDocsAttribute helpLink)
                    Application.OpenURL("https://purrnet.gitbook.io/docs/" + helpLink.url);
            }

            EditorGUI.PropertyField(propertyRect, property, label);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
#endif
}
