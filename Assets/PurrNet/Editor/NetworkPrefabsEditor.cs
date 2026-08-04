#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;

namespace PurrNet
{
    [CustomEditor(typeof(NetworkPrefabs))]
    public class NetworkPrefabsEditor : UnityEditor.Editor
    {
        private NetworkPrefabs networkPrefabs;
        private SerializedProperty linkedNetworkPrefabs;
        private SerializedProperty prefabs;
        private SerializedProperty folderProp;
        private bool? allPoolingState = null;
        private ReorderableList reorderableList;
        private string _searchFilter = "";

        private const float SPACING = 8f;
        private const float REORDERABLE_LIST_BUTTON_WIDTH = 25f;
        private const float INDEX_WIDTH = 30f;

        private void OnEnable()
        {
            networkPrefabs = (NetworkPrefabs)target;
            linkedNetworkPrefabs = serializedObject.FindProperty("linkedNetworkPrefabs");
            prefabs = serializedObject.FindProperty("prefabs");
            folderProp = serializedObject.FindProperty("folder");

            if (networkPrefabs.autoGenerate)
                networkPrefabs.Generate();

            UpdateAllPoolingState();
            SetupReorderableList();
        }

        private void SetupReorderableList()
        {
            reorderableList = new ReorderableList(serializedObject, prefabs, true, true, true, true);
            reorderableList.elementHeight = EditorGUIUtility.singleLineHeight;

            reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                float fullWidth = rect.width - REORDERABLE_LIST_BUTTON_WIDTH;
                CalculateWidths(fullWidth, out float prefabWidth, out float poolWidth, out float warmupWidth);

                float x = rect.x;
                EditorGUI.LabelField(new Rect(x, rect.y, INDEX_WIDTH, rect.height), "ID");
                x += INDEX_WIDTH + SPACING;
                EditorGUI.LabelField(new Rect(x, rect.y, prefabWidth, rect.height), "Prefab");
                EditorGUI.LabelField(
                    new Rect(x + prefabWidth + SPACING, rect.y, poolWidth + warmupWidth, rect.height), "Pool");
            };

            reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                SerializedProperty element = prefabs.GetArrayElementAtIndex(index);
                SerializedProperty prefabProp = element.FindPropertyRelative("prefab");
                SerializedProperty poolProp = element.FindPropertyRelative("pooled");
                SerializedProperty warmupCountProp = element.FindPropertyRelative("warmupCount");

                float fullWidth = rect.width - REORDERABLE_LIST_BUTTON_WIDTH;
                CalculateWidths(fullWidth, out float prefabWidth, out float poolWidth, out float warmupWidth);

                float x = rect.x;
                EditorGUI.LabelField(new Rect(x, rect.y, INDEX_WIDTH, rect.height), index.ToString());
                x += INDEX_WIDTH + SPACING;

                // Disable the prefab field when auto-generate is on (it manages prefabs),
                // but always allow editing pool and warmup settings.
                EditorGUI.BeginDisabledGroup(networkPrefabs.autoGenerate);
                EditorGUI.PropertyField(new Rect(x, rect.y, prefabWidth, rect.height), prefabProp,
                    GUIContent.none);
                EditorGUI.EndDisabledGroup();

                poolProp.boolValue =
                    EditorGUI.Toggle(new Rect(x + prefabWidth + SPACING, rect.y, poolWidth, rect.height),
                        poolProp.boolValue);

                if (poolProp.boolValue)
                {
                    EditorGUI.PropertyField(
                        new Rect(x + prefabWidth + poolWidth + (SPACING * 2), rect.y, warmupWidth, rect.height),
                        warmupCountProp, GUIContent.none);
                }
            };

            reorderableList.onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Empty Entry"), false, () =>
                {
                    int index = list.count;
                    list.serializedProperty.arraySize++;
                    var element = list.serializedProperty.GetArrayElementAtIndex(index);
                    element.FindPropertyRelative("guid").stringValue = string.Empty;
                    element.FindPropertyRelative("prefab").objectReferenceValue = null;
                    element.FindPropertyRelative("pooled").boolValue = networkPrefabs.poolByDefault;
                    element.FindPropertyRelative("warmupCount").intValue = 5;
                    serializedObject.ApplyModifiedProperties();
                    UpdateAllPoolingState();
                });

                menu.AddItem(new GUIContent("Add Selected Prefabs"), false, () =>
                {
                    bool addedAny = false;
                    foreach (var obj in Selection.gameObjects)
                    {
                        if (PrefabUtility.IsPartOfPrefabAsset(obj))
                        {
                            addedAny = true;
                            int index = list.count;
                            list.serializedProperty.arraySize++;
                            var element = list.serializedProperty.GetArrayElementAtIndex(index);
                            string path = AssetDatabase.GetAssetPath(obj);
                            element.FindPropertyRelative("guid").stringValue = AssetDatabase.AssetPathToGUID(path);
                            element.FindPropertyRelative("prefab").objectReferenceValue = obj;
                            element.FindPropertyRelative("pooled").boolValue = networkPrefabs.poolByDefault;
                            element.FindPropertyRelative("warmupCount").intValue = 5;
                        }
                    }

                    if (addedAny)
                    {
                        serializedObject.ApplyModifiedProperties();
                        UpdateAllPoolingState();
                    }
                });

                menu.ShowAsContext();
            };
        }

        private void CalculateWidths(float fullWidth, out float prefabWidth, out float poolWidth, out float warmupWidth)
        {
            poolWidth = 20f;
            warmupWidth = 60f;
            prefabWidth = fullWidth - poolWidth - warmupWidth - INDEX_WIDTH - (SPACING * 3);
        }

        private void UpdateAllPoolingState()
        {
            if (prefabs.arraySize == 0)
            {
                allPoolingState = null;
                return;
            }

            bool firstState = prefabs.GetArrayElementAtIndex(0).FindPropertyRelative("pooled").boolValue;
            allPoolingState = firstState;

            for (int i = 1; i < prefabs.arraySize; i++)
            {
                if (prefabs.GetArrayElementAtIndex(i).FindPropertyRelative("pooled").boolValue != firstState)
                {
                    allPoolingState = null;
                    return;
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SharedAssetEditorUI.DrawHeader(
                "Network Prefabs",
                "This asset is used to store any prefabs containing a Network Behaviour. " +
                "You can add prefabs to this asset manually or auto generate the references. " +
                "This list is used by the NetworkManager to spawn network prefabs.");

            SharedAssetEditorUI.DrawGenerationSettingsTop(folderProp, networkPrefabs);

            GUILayout.BeginHorizontal();
            DrawToggleButton("Auto generate", ref networkPrefabs.autoGenerate);
            DrawToggleButton("Networked only", ref networkPrefabs.networkOnly);
            DrawToggleButton("Default pooling", ref networkPrefabs.poolByDefault);
            GUILayout.EndHorizontal();

            SharedAssetEditorUI.DrawGenerateButton(() =>
            {
                networkPrefabs.Generate();
                serializedObject.Update();
                prefabs = serializedObject.FindProperty("prefabs");
                reorderableList.serializedProperty = prefabs;
                UpdateAllPoolingState();
            });

            SharedAssetEditorUI.DrawLinkedField(linkedNetworkPrefabs);

            SharedAssetEditorUI.DrawEntryList(reorderableList, networkPrefabs.autoGenerate,
                ref _searchFilter, i =>
                {
                    if (i >= prefabs.arraySize) return null;
                    var obj = prefabs.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue;
                    return obj ? obj.name : null;
                });

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                networkPrefabs.Refresh();
                EditorUtility.SetDirty(networkPrefabs);
            }
        }

        private void DrawToggleButton(string label, ref bool value)
        {
            value = SharedAssetEditorUI.DrawToggleButton(label, value, networkPrefabs, () =>
            {
                if (networkPrefabs.autoGenerate)
                {
                    networkPrefabs.Generate();
                    serializedObject.Update();
                    prefabs = serializedObject.FindProperty("prefabs");
                    reorderableList.serializedProperty = prefabs;
                    UpdateAllPoolingState();
                }
            });
        }
    }
}
#endif
