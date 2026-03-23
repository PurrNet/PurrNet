#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    /// <summary>
    /// Shared editor drawing methods used by NetworkPrefabs, NetworkAssets,
    /// and AddressableNetworkPrefabs custom editors.
    /// </summary>
    public static class SharedAssetEditorUI
    {
        private static GUIStyle _descriptionStyle;

        public static GUIStyle DescriptionStyle()
        {
            return _descriptionStyle ??= new GUIStyle(GUI.skin.label)
            {
                wordWrap = true
            };
        }

        /// <summary>
        /// Draws a colored toggle button (green when active, white when inactive).
        /// </summary>
        public static bool DrawToggleButton(string label, bool value, Object dirtyTarget, Action onChanged = null)
        {
            GUI.color = value ? Color.green : Color.white;
            if (GUILayout.Button(label, GUILayout.Width(1), GUILayout.ExpandWidth(true)))
            {
                value = !value;
                if (dirtyTarget)
                    EditorUtility.SetDirty(dirtyTarget);
                onChanged?.Invoke();
            }
            GUI.color = Color.white;
            return value;
        }

        /// <summary>
        /// Draws a folder object field.
        /// </summary>
        public static void DrawFolderField(SerializedProperty folderProp)
        {
            EditorGUILayout.PropertyField(folderProp, new GUIContent("Folder"));
        }

        /// <summary>
        /// Draws a Generate button that calls the provided action.
        /// </summary>
        public static void DrawGenerateButton(Action onGenerate)
        {
            if (GUILayout.Button("Generate", GUILayout.Width(1), GUILayout.ExpandWidth(true)))
            {
                onGenerate?.Invoke();
            }
        }
    }
}
#endif
