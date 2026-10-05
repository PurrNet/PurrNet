using UnityEngine.UIElements;
#if EDITOR_ATTRIBUTES_PACKAGE
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using Object = UnityEngine.Object;
#endif

namespace PurrNet.Editor
{
    /// <summary>Optional EditorAttributes renderer, leaving other inspector adapters in control of unannotated types.</summary>
    public static class EditorAttributesIntegration
    {
#if EDITOR_ATTRIBUTES_PACKAGE
        private static readonly Type _editorType = Type.GetType("EditorAttributes.Editor.EditorExtension, EditorAttributes.Editor");
        private static readonly MethodInfo _sceneGUI = _editorType?.GetMethod("OnSceneGUI", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<UnityEditor.Editor, UnityEditor.Editor> _editors = new();
        private static readonly Dictionary<Type, bool> _annotatedTypes = new();

        private static bool HasAttributes(Type type, HashSet<Type> visited, bool root = false)
        {
            if (type == null || !visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string))
                return false;

            // Referenced Unity objects have their own inspectors. Inspect only the root component/asset here.
            if (!root && typeof(Object).IsAssignableFrom(type))
                return false;

            if (type.IsArray)
                return HasAttributes(type.GetElementType(), visited);

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    if (HasAttributes(argument, visited))
                        return true;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var current = type; current != null && current != typeof(Object); current = current.BaseType)
            {
                foreach (var member in current.GetMembers(flags))
                {
                    foreach (var attribute in CustomAttributeData.GetCustomAttributes(member))
                        if (attribute.AttributeType.Namespace == "EditorAttributes")
                            return true;

                    if (member is FieldInfo field && !field.IsStatic && !field.IsNotSerialized &&
                        (field.IsPublic || field.IsDefined(typeof(UnityEngine.SerializeField), false) ||
                         field.IsDefined(typeof(UnityEngine.SerializeReference), false)) &&
                        HasAttributes(field.FieldType, visited))
                        return true;
                }
            }

            return false;
        }

        private static bool HasManagedReferenceAttributes(SerializedObject serializedObject)
        {
            var visited = new HashSet<long>();
            using var property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = property.propertyType != SerializedPropertyType.String;
                if (property.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                enterChildren = visited.Add(property.managedReferenceId);
                if (enterChildren && property.managedReferenceValue is { } value &&
                    HasAttributes(value.GetType(), new HashSet<Type>()))
                    return true;
            }

            return false;
        }
#endif

        public static VisualElement CreateInspectorGUI(UnityEditor.Editor owner, params string[] excludedProperties)
        {
#if EDITOR_ATTRIBUTES_PACKAGE
            if (_editorType == null || !owner || !owner.target)
                return null;

            var targetType = owner.target.GetType();
            if (!_annotatedTypes.TryGetValue(targetType, out var annotated))
            {
                try
                {
                    annotated = HasAttributes(targetType, new HashSet<Type>(), true);
                }
                catch (System.IO.FileNotFoundException)
                {
                    return null;
                }
                catch (ReflectionTypeLoadException)
                {
                    return null;
                }
                _annotatedTypes.Add(targetType, annotated);
            }

            if (!annotated)
            {
                if (owner.targets.Length == 1)
                {
                    annotated = HasManagedReferenceAttributes(owner.serializedObject);
                }
                else
                {
                    foreach (var target in owner.targets)
                    {
                        if (!target)
                            continue;
                        using var serializedObject = new SerializedObject(target);
                        if (!HasManagedReferenceAttributes(serializedObject))
                            continue;
                        annotated = true;
                        break;
                    }
                }
                if (!annotated)
                    return null;
            }

            if (!_editors.TryGetValue(owner, out var editor) || !editor)
            {
                editor = UnityEditor.Editor.CreateEditor(owner.targets, _editorType);
                _editors[owner] = editor;
            }

            var root = editor.CreateInspectorGUI();
            if (root == null)
            {
                OnDisable(owner);
                return null;
            }

            if (excludedProperties != null && excludedProperties.Length > 0 && root.childCount > 0)
            {
                var excluded = new HashSet<string>(excludedProperties);
                var remove = new List<VisualElement>();
                foreach (var child in root[0].Children())
                    if (child is PropertyField field && excluded.Contains(field.bindingPath))
                        remove.Add(child);
                foreach (var child in remove)
                    child.RemoveFromHierarchy();
            }

            root.Bind(editor.serializedObject);
            return root;
#else
            return null;
#endif
        }

        public static void OnDisable(UnityEditor.Editor owner)
        {
#if EDITOR_ATTRIBUTES_PACKAGE
            if (_editors.Remove(owner, out var editor))
            {
                if (editor)
                    Object.DestroyImmediate(editor);
            }
#endif
        }

        public static void OnSceneGUI(UnityEditor.Editor owner)
        {
#if EDITOR_ATTRIBUTES_PACKAGE
            if (_sceneGUI != null && _editors.TryGetValue(owner, out var editor) && editor)
                _sceneGUI.Invoke(editor, null);
#endif
        }
    }
}
