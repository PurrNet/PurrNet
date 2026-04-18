using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(NetworkReflection), true)]
    public class NetworkReflectionInspector : NetworkIdentityInspector
    {
        private SerializedProperty _trackedBehaviour;
        private SerializedProperty _trackedFields;
        private SerializedProperty _trackedMethods;
        private SerializedProperty _ownerAuth;

        internal static string GetCachePath()
        {
            var settings = PurrNetSettings.GetOrCreateSettings();
            return settings.reflectionCachePath;
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            _trackedBehaviour = serializedObject.FindProperty("_trackedBehaviour");
            _trackedFields = serializedObject.FindProperty("_trackedFields");
            _trackedMethods = serializedObject.FindProperty("_trackedMethods");
            _ownerAuth = serializedObject.FindProperty("_ownerAuth");
        }

        static readonly List<ReflectionData> _validFieldNames = new List<ReflectionData>();
        static readonly List<ReflectionMethodData> _validMethodNames = new List<ReflectionMethodData>();

        void GetAllValidFieldNames(List<ReflectionData> validNames)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var reflection = (NetworkReflection)target;
            var trackedType = reflection.trackedType;
            var fields = trackedType.GetFields(flags);
            var properties = trackedType.GetProperties(flags);

            foreach (var field in fields)
            {
                validNames.Add(new ReflectionData
                {
                    name = field.Name,
                    type = ReflectionType.Field
                });
            }

            foreach (var property in properties)
            {
                if (property.SetMethod == null || property.GetMethod == null)
                    continue;

                validNames.Add(new ReflectionData
                {
                    name = property.Name,
                    type = ReflectionType.Property
                });
            }
        }

        void GetAllValidMethodNames(List<ReflectionMethodData> validNames)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                       BindingFlags.DeclaredOnly;

            var reflection = (NetworkReflection)target;
            var trackedType = reflection.trackedType;
            var methods = trackedType.GetMethods(flags);

            foreach (var method in methods)
            {
                if (method.IsSpecialName) continue;
                if (method.IsAbstract) continue;
                if (method.ReturnType != typeof(void)) continue;
                if (method.IsGenericMethod) continue;

                validNames.Add(new ReflectionMethodData
                {
                    name = method.Name,
                    rpcType = ReflectionRPCType.ServerRpc
                });
            }
        }

        int GetFieldIndexOfName(string propName)
        {
            for (var i = 0; i < _validFieldNames.Count; i++)
            {
                if (_validFieldNames[i].name == propName)
                    return i;
            }

            return -1;
        }

        int GetMethodIndexOfName(string methodName)
        {
            for (var i = 0; i < _validMethodNames.Count; i++)
            {
                if (_validMethodNames[i].name == methodName)
                    return i;
            }

            return -1;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var reflection = (NetworkReflection)target;
            var previousType = reflection.trackedType;

            EditorGUILayout.PropertyField(_trackedBehaviour);
            EditorGUILayout.PropertyField(_ownerAuth);

            serializedObject.ApplyModifiedProperties();

            var currentType = reflection.trackedType;

            if (previousType != currentType)
                HandleTrackedBehaviourChanged(previousType, currentType);

            if (currentType == null)
            {
                EditorGUILayout.HelpBox("Tracked behaviour is null", MessageType.Error);
                return;
            }

            _validFieldNames.Clear();
            GetAllValidFieldNames(_validFieldNames);

            _validMethodNames.Clear();
            GetAllValidMethodNames(_validMethodNames);

            DrawFieldsSection(reflection);

            EditorGUILayout.Space(8);

            DrawMethodsSection(reflection);

            DrawIdentityInspector();

            serializedObject.ApplyModifiedProperties();
        }

        static void HandleTrackedBehaviourChanged(Type previousType, Type currentType)
        {
            bool hadTrackedType = previousType != null && !IsUnityEngineType(previousType);
            bool hasNewType = currentType != null && !IsUnityEngineType(currentType);

            if (hasNewType)
                EnsureTypeInCache(currentType.FullName);

            if (hadTrackedType && previousType != currentType)
                EditorApplication.delayCall += RefreshReflectionTargetsCacheFull;
        }

        internal static void EnsureTypeInCache(string typeFullName)
        {
            var cachePath = GetCachePath();

            var existing = new HashSet<string>();
            if (File.Exists(cachePath))
            {
                foreach (var line in File.ReadAllLines(cachePath))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        existing.Add(trimmed);
                }
            }

            if (existing.Contains(typeFullName))
                return;

            existing.Add(typeFullName);
            WriteCacheFile(existing);
        }

        internal static void RefreshReflectionTargetsCacheFull()
        {
            var targetTypes = new HashSet<string>();
            var prefabPaths = NetworkReflectionPrefabCache.Read();

            var allReflections = Resources.FindObjectsOfTypeAll<NetworkReflection>();
            foreach (var reflection in allReflections)
            {
                if (reflection == null) continue;
                if (reflection.trackedMethods == null || reflection.trackedMethods.Count == 0)
                    continue;

                var type = reflection.trackedType;
                if (type != null)
                    targetTypes.Add(type.FullName);
            }

            if (prefabPaths.Count == 0)
            {
                var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
                foreach (var guid in prefabGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!NetworkReflectionPrefabCache.PrefabContainsNetworkReflection(path))
                        continue;

                    prefabPaths.Add(path);
                    CollectTargetTypesFromPrefab(path, targetTypes);
                }
                NetworkReflectionPrefabCache.Write(prefabPaths);
            }
            else
            {
                var toRemove = new List<string>();
                foreach (var path in prefabPaths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        toRemove.Add(path);
                        continue;
                    }

                    var hadRelevantReflection = false;
                    var reflections = prefab.GetComponentsInChildren<NetworkReflection>(true);
                    foreach (var reflection in reflections)
                    {
                        if (reflection == null) continue;
                        if (reflection.trackedMethods == null || reflection.trackedMethods.Count == 0)
                            continue;

                        hadRelevantReflection = true;
                        var type = reflection.trackedType;
                        if (type != null)
                            targetTypes.Add(type.FullName);
                    }

                    if (!hadRelevantReflection)
                        toRemove.Add(path);
                }

                if (toRemove.Count > 0)
                {
                    foreach (var path in toRemove)
                        prefabPaths.Remove(path);
                    NetworkReflectionPrefabCache.Write(prefabPaths);
                }
            }

            WriteCacheFile(targetTypes);
        }

        static void CollectTargetTypesFromPrefab(string path, HashSet<string> targetTypes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            foreach (var reflection in prefab.GetComponentsInChildren<NetworkReflection>(true))
            {
                if (reflection == null) continue;
                if (reflection.trackedMethods == null || reflection.trackedMethods.Count == 0)
                    continue;

                var type = reflection.trackedType;
                if (type != null)
                    targetTypes.Add(type.FullName);
            }
        }

        void DrawFieldsSection(NetworkReflection reflection)
        {
            string[] fieldNames = new string[_validFieldNames.Count];
            for (var i = 0; i < _validFieldNames.Count; i++)
                fieldNames[i] = $"{_validFieldNames[i].type}/{_validFieldNames[i].name}";

            if (reflection.trackedFields == null)
            {
                reflection.trackedFields = new List<ReflectionData>();
                EditorUtility.SetDirty(reflection);
            }

            EditorGUILayout.LabelField("Synced Fields", EditorStyles.boldLabel);
            EditorGUI.BeginProperty(Rect.zero, GUIContent.none, _trackedFields);
            EditorGUILayout.BeginVertical("helpbox");

            for (var i = 0; i < reflection.trackedFields.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                int idx = GetFieldIndexOfName(reflection.trackedFields[i].name);

                GUI.color = idx == -1 ? Color.yellow : Color.white;
                int newIdx = EditorGUILayout.Popup(idx, fieldNames);

                if (newIdx != idx)
                {
                    Undo.RecordObject(reflection, "Change field");
                    reflection.trackedFields[i] = _validFieldNames[newIdx];
                    EditorUtility.SetDirty(reflection);
                }

                GUI.color = Color.white;

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Remove", GUILayout.ExpandWidth(false), GUILayout.Width(100)))
                {
                    Undo.RecordObject(reflection, "Remove field");
                    reflection.trackedFields.RemoveAt(i);
                    EditorUtility.SetDirty(reflection);
                    i--;
                }

                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Add", GUILayout.ExpandWidth(false), GUILayout.Width(100)))
            {
                Undo.RecordObject(reflection, "Add field");
                reflection.trackedFields.Add(new ReflectionData());
                EditorUtility.SetDirty(reflection);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUI.EndProperty();
        }

        static bool IsUnityEngineType(Type type)
        {
            if (type == null) return false;
            var ns = type.Namespace ?? "";
            return ns.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   ns.StartsWith("UnityEditor", StringComparison.Ordinal);
        }

        void DrawMethodsSection(NetworkReflection reflection)
        {
            if (IsUnityEngineType(reflection.trackedType))
            {
                EditorGUILayout.LabelField("RPC Methods", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "RPC methods cannot be used with UnityEngine or UnityEditor components. The IL post-processor cannot modify pre-compiled Unity assemblies. Use a custom MonoBehaviour instead.",
                    MessageType.Warning);
                return;
            }

            var allMethods = reflection.trackedType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            string[] methodDisplayNames = new string[_validMethodNames.Count];
            for (var i = 0; i < _validMethodNames.Count; i++)
            {
                var methodName = _validMethodNames[i].name;
                MethodInfo methodInfo = null;

                for (int m = 0; m < allMethods.Length; m++)
                {
                    if (allMethods[m].Name == methodName)
                    {
                        methodInfo = allMethods[m];
                        break;
                    }
                }

                string paramStr = "()";
                if (methodInfo != null)
                {
                    var parameters = methodInfo.GetParameters();
                    if (parameters.Length > 0)
                        paramStr = "(" + string.Join(", ", parameters.Select(p => p.ParameterType.Name)) + ")";
                }

                methodDisplayNames[i] = $"{methodName}{paramStr}";
            }

            if (reflection.trackedMethods == null)
            {
                reflection.trackedMethods = new List<ReflectionMethodData>();
                EditorUtility.SetDirty(reflection);
            }

            EditorGUILayout.LabelField("RPC Methods", EditorStyles.boldLabel);
            EditorGUI.BeginProperty(Rect.zero, GUIContent.none, _trackedMethods);
            EditorGUILayout.BeginVertical("helpbox");

            for (var i = 0; i < reflection.trackedMethods.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                int idx = GetMethodIndexOfName(reflection.trackedMethods[i].name);

                GUI.color = idx == -1 ? Color.yellow : Color.white;
                int newIdx = EditorGUILayout.Popup(idx, methodDisplayNames);

                if (newIdx != idx)
                {
                    Undo.RecordObject(reflection, "Change method");
                    var data = _validMethodNames[newIdx];
                    data.rpcType = reflection.trackedMethods[i].rpcType;
                    reflection.trackedMethods[i] = data;
                    EditorUtility.SetDirty(reflection);
                }

                GUI.color = Color.white;

                var currentRpcType = reflection.trackedMethods[i].rpcType;
                var newRpcType = (ReflectionRPCType)EditorGUILayout.EnumPopup(currentRpcType, GUILayout.Width(120));
                if (newRpcType != currentRpcType)
                {
                    Undo.RecordObject(reflection, "Change RPC type");
                    var data = reflection.trackedMethods[i];
                    data.rpcType = newRpcType;
                    reflection.trackedMethods[i] = data;
                    EditorUtility.SetDirty(reflection);
                }

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Remove", GUILayout.ExpandWidth(false), GUILayout.Width(100)))
                {
                    Undo.RecordObject(reflection, "Remove method");
                    reflection.trackedMethods.RemoveAt(i);
                    EditorUtility.SetDirty(reflection);
                    i--;
                }

                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Add", GUILayout.ExpandWidth(false), GUILayout.Width(100)))
            {
                Undo.RecordObject(reflection, "Add method");
                reflection.trackedMethods.Add(new ReflectionMethodData());
                EditorUtility.SetDirty(reflection);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUI.EndProperty();
        }

        static void WriteCacheFile(HashSet<string> targetTypeNames)
        {
            var cachePath = GetCachePath();
            var cacheDir = Path.GetDirectoryName(cachePath);

            if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);

            var sorted = targetTypeNames.OrderBy(n => n).ToArray();
            var newContent = string.Join("\n", sorted);

            if (File.Exists(cachePath))
            {
                var existing = File.ReadAllText(cachePath);
                if (existing == newContent)
                    return;
            }

            File.WriteAllText(cachePath, newContent);
            CompilationPipeline.RequestScriptCompilation();
        }
    }

    static class NetworkReflectionCacheMenu
    {
        [MenuItem("Tools/PurrNet/Misc/Rebuild Network Reflection Cache", false, 500)]
        static void RebuildCache()
        {
            if (File.Exists(NetworkReflectionPrefabCache.PREFAB_CACHE_FILE))
                File.Delete(NetworkReflectionPrefabCache.PREFAB_CACHE_FILE);

            NetworkReflectionInspector.RefreshReflectionTargetsCacheFull();
        }
    }
}
