#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    /// <summary>
    /// Shared editor drawing methods used by NetworkPrefabs, NetworkAssets,
    /// and AddressableNetworkPrefabs custom editors.
    /// Centralizes layout patterns so all asset-provider inspectors
    /// share the same design language.
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
        /// Draws the bold title and word-wrapped description at the top of the inspector.
        /// </summary>
        public static void DrawHeader(string title, string description)
        {
            GUILayout.Label(title, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(description, DescriptionStyle());
            GUILayout.Space(10);
        }

        /// <summary>
        /// Draws the "Generation Settings" label and folder field.
        /// Call DrawToggleButtons and DrawGenerateButton after this.
        /// </summary>
        public static void DrawGenerationSettingsTop(SerializedProperty folderProp, Object dirtyTarget)
        {
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(folderProp, new GUIContent("Folder"));
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(dirtyTarget);
            }
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
        /// Draws a full-width Generate button on its own row.
        /// </summary>
        public static void DrawGenerateButton(Action onGenerate)
        {
            if (GUILayout.Button("Generate", GUILayout.Width(1), GUILayout.ExpandWidth(true)))
            {
                onGenerate?.Invoke();
            }
        }

        /// <summary>
        /// Draws a linked-assets list field with spacing around it.
        /// </summary>
        public static void DrawLinkedField(SerializedProperty linkedProp)
        {
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(linkedProp, true);
        }

        /// <summary>
        /// Draws a ReorderableList, disabling add/remove/reorder when auto-generate is on.
        /// The list itself is never fully disabled so per-entry fields (like pool toggles)
        /// remain editable.
        /// </summary>
        public static void DrawEntryList(ReorderableList list, bool autoGenerate)
        {
            GUILayout.Space(10);
            list.displayAdd = !autoGenerate;
            list.displayRemove = !autoGenerate;
            list.draggable = true;
            list.DoLayoutList();
        }

        /// <summary>
        /// Draws a folder object field.
        /// </summary>
        public static void DrawFolderField(SerializedProperty folderProp)
        {
            EditorGUILayout.PropertyField(folderProp, new GUIContent("Folder"));
        }
    }
}
#endif
