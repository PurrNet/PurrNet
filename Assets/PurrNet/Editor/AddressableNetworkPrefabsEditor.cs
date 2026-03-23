#if UNITY_EDITOR && ADDRESSABLES_PURRNET_SUPPORT
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PurrNet
{
    [CustomEditor(typeof(AddressableNetworkPrefabs))]
    public class AddressableNetworkPrefabsEditor : UnityEditor.Editor
    {
        private AddressableNetworkPrefabs _target;
        private SerializedProperty _preloadAtStartupProp;
        private SerializedProperty _entriesProp;
        private SerializedProperty _linkedProp;
        private SerializedProperty _folderProp;
        private ReorderableList _reorderableList;

        private static bool _generating;

        [InitializeOnLoadMethod]
        private static void SubscribeAutoGenerate()
        {
            AddressableNetworkPrefabs.onAutoGenerateRequested += OnAutoGenerateRequested;
        }

        private static void OnAutoGenerateRequested(AddressableNetworkPrefabs target)
        {
            if (target && target.autoGenerate)
                Generate(target);
        }

        private void OnEnable()
        {
            _target = (AddressableNetworkPrefabs)target;
            _preloadAtStartupProp = serializedObject.FindProperty("_preloadAtStartup");
            _entriesProp = serializedObject.FindProperty("_entries");
            _linkedProp = serializedObject.FindProperty("linkedAddressablePrefabs");
            _folderProp = serializedObject.FindProperty("folder");

            if (_target.autoGenerate)
                Generate(_target);

            SetupReorderableList();
        }

        private void SetupReorderableList()
        {
            _reorderableList = new ReorderableList(serializedObject, _entriesProp, true, true, true, true);
            _reorderableList.elementHeight = EditorGUIUtility.singleLineHeight;

            _reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Asset Reference");
            };

            _reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _entriesProp.GetArrayElementAtIndex(index);
                var assetProp = element.FindPropertyRelative("asset");
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, rect.height), assetProp, GUIContent.none);
            };

            _reorderableList.onAddCallback = (ReorderableList list) =>
            {
                int index = list.count;
                list.serializedProperty.arraySize++;
                var element = list.serializedProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("asset").boxedValue = null;
                serializedObject.ApplyModifiedProperties();
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUILayout.Label("Addressable Network Prefabs", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(
                "This asset stores Addressable prefab references for network spawning. " +
                "Prefabs can be added manually or auto-generated from a folder containing Addressable assets.",
                SharedAssetEditorUI.DescriptionStyle());

            GUILayout.Space(10);

            EditorGUILayout.PropertyField(_preloadAtStartupProp, new GUIContent("Preload At Startup"));

            GUILayout.Space(5);

            // Generation Settings
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_folderProp, new GUIContent("Folder"));
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
            }

            // Toggle buttons row
            GUILayout.BeginHorizontal();

            DrawToggleButton("Auto generate", ref _target.autoGenerate);

            if (GUILayout.Button("Generate", GUILayout.Width(1), GUILayout.ExpandWidth(true)))
            {
                Generate(_target);
                serializedObject.Update();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_linkedProp, true);

            GUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(_target.autoGenerate);
            _reorderableList.DoLayoutList();
            EditorGUI.EndDisabledGroup();

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                _target.Refresh();
                EditorUtility.SetDirty(_target);
            }
        }

        private void DrawToggleButton(string label, ref bool value)
        {
            value = SharedAssetEditorUI.DrawToggleButton(label, value, _target, () =>
            {
                if (_target.autoGenerate)
                {
                    Generate(_target);
                    serializedObject.Update();
                }
            });
        }

        /// <summary>
        /// Scans the configured folder for Addressable prefabs and adds them as entries.
        /// Only prefabs that are marked as Addressable will be added.
        /// </summary>
        public static void Generate(AddressableNetworkPrefabs target)
        {
            if (!target) return;
            if (_generating) return;

            _generating = true;
            try
            {
                var found = AssetScannerUtility.ScanPrefabs(target.folder, true, target.searchAllIfNoFolder);

                if (found.Count == 0 && target.folder == null && !target.searchAllIfNoFolder)
                    return;

                var existingGuids = target.GetExistingGuids();
                bool changed = false;

                foreach (var scan in found)
                {
                    if (existingGuids.Contains(scan.guid)) continue;

                    // Only add if the asset is actually an Addressable
                    var settings = AddressableAssetSettingsDefaultObject.Settings;
                    if (settings == null) continue;

                    var addressableEntry = settings.FindAssetEntry(scan.guid);
                    if (addressableEntry == null) continue;

                    var assetRef = new AssetReferenceGameObject(scan.guid);
                    target.AddEntry(assetRef);
                    changed = true;
                }

                if (changed)
                {
                    target.Refresh();
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception e)
            {
                PurrNet.Logging.PurrLogger.LogError($"An error occurred during addressable prefab generation: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _generating = false;
            }
        }
    }
}
#endif
