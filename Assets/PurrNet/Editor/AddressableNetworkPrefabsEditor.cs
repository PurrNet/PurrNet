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
        private SerializedProperty _entriesProp;
        private SerializedProperty _linkedProp;
        private SerializedProperty _folderProp;
        private ReorderableList _reorderableList;

        private const float SPACING = 8f;
        private const float INDEX_WIDTH = 30f;

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
                EditorGUI.LabelField(new Rect(rect.x, rect.y, INDEX_WIDTH, rect.height), "ID");
                EditorGUI.LabelField(new Rect(rect.x + INDEX_WIDTH + SPACING, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), "Prefab");
            };

            _reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _entriesProp.GetArrayElementAtIndex(index);
                var assetProp = element.FindPropertyRelative("asset");

                float x = rect.x;
                EditorGUI.LabelField(new Rect(x, rect.y, INDEX_WIDTH, rect.height), index.ToString());
                x += INDEX_WIDTH + SPACING;

                EditorGUI.BeginDisabledGroup(_target.autoGenerate);
                EditorGUI.PropertyField(new Rect(x, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), assetProp, GUIContent.none);
                EditorGUI.EndDisabledGroup();
            };

            _reorderableList.onAddCallback = (ReorderableList list) =>
            {
                int index = list.count;
                list.serializedProperty.arraySize++;
                serializedObject.ApplyModifiedProperties();
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. Header
            SharedAssetEditorUI.DrawHeader(
                "Addressable Network Prefabs",
                "This asset stores Addressable prefab references for network spawning. " +
                "Prefabs can be added manually or auto-generated from a folder containing Addressable assets.");

            // 2. Generation Settings
            SharedAssetEditorUI.DrawGenerationSettingsTop(_folderProp, _target);

            // 3. Toggle buttons row
            GUILayout.BeginHorizontal();
            DrawToggleButton("Auto generate", ref _target.autoGenerate);
            _target.preloadAtStartup = SharedAssetEditorUI.DrawToggleButton("Preload at startup", _target.preloadAtStartup, _target);
            GUILayout.EndHorizontal();

            // 4. Generate button (full-width, own row)
            SharedAssetEditorUI.DrawGenerateButton(() =>
            {
                Generate(_target);
                serializedObject.Update();
                _entriesProp = serializedObject.FindProperty("_entries");
            });

            // 5. Linked prefabs
            SharedAssetEditorUI.DrawLinkedField(_linkedProp);

            // 6. Entry list
            SharedAssetEditorUI.DrawEntryList(_reorderableList, _target.autoGenerate);

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
                    _entriesProp = serializedObject.FindProperty("_entries");
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
