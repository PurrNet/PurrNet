using UnityEngine.UIElements;
#if ALCHEMY_PACKAGE
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace PurrNet.Editor
{
    /// <summary>Optional Alchemy renderer; its implementation assembly is compiled only when Alchemy is installed.</summary>
    public static class AlchemyIntegration
    {
#if ALCHEMY_PACKAGE
        private static readonly Type _editorType = Type.GetType("PurrNet.Editor.Alchemy.AlchemyEditorBridge, PurrNet.Editor.Alchemy");
        private static readonly FieldInfo _excludedProperties = _editorType?.GetField("excludedProperties");
        private static readonly MethodInfo _getPropertyType = _editorType?.GetMethod("GetPropertyType", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo _createPropertyGUI = _editorType?.GetMethod("CreatePropertyGUI", BindingFlags.Public | BindingFlags.Static);
        private static readonly Dictionary<UnityEditor.Editor, UnityEditor.Editor> _editors = new();
        private static readonly Dictionary<Type, bool> _annotatedTypes = new();

        private static bool IsInspectorAttribute(Type type, bool editorAttributes)
        {
            if (editorAttributes)
                return type.Namespace == "EditorAttributes";
            for (var current = type; current != null; current = current.BaseType)
                if (current.Namespace == "Alchemy.Inspector" || current.Namespace == "Alchemy.Serialization")
                    return true;
            return false;
        }

        private static bool IsDisabled(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
                foreach (var attribute in CustomAttributeData.GetCustomAttributes(current))
                    if (attribute.AttributeType.FullName == "Alchemy.Inspector.DisableAlchemyEditorAttribute")
                        return true;
            return false;
        }

        private static bool HasAttributes(Type type, HashSet<Type> visited, bool root = false, bool editorAttributes = false)
        {
            if (type == null || !visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string))
                return false;
            if (!root && typeof(Object).IsAssignableFrom(type))
                return false;
            if (type.IsArray)
                return HasAttributes(type.GetElementType(), visited, false, editorAttributes);
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments())
                    if (HasAttributes(argument, visited, false, editorAttributes))
                        return true;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var current = type; current != null && current != typeof(Object); current = current.BaseType)
            {
                if (!editorAttributes)
                    foreach (var attribute in CustomAttributeData.GetCustomAttributes(current))
                        if (IsInspectorAttribute(attribute.AttributeType, false))
                            return true;
                foreach (var member in current.GetMembers(flags))
                {
                    foreach (var attribute in CustomAttributeData.GetCustomAttributes(member))
                        if (IsInspectorAttribute(attribute.AttributeType, editorAttributes))
                            return true;
                    if (member is FieldInfo field && !field.IsStatic && !field.IsNotSerialized &&
                        (field.IsPublic || field.IsDefined(typeof(SerializeField), false) ||
                         field.IsDefined(typeof(SerializeReference), false)) &&
                        HasAttributes(field.FieldType, visited, false, editorAttributes))
                        return true;
                }
            }
            return false;
        }

        private static bool HasPropertyAttributes(SerializedProperty property)
        {
            var type = (Type)_getPropertyType.Invoke(null, new object[] { property });
            bool annotated = HasAttributes(type, new HashSet<Type>());
#if EDITOR_ATTRIBUTES_PACKAGE
            if (HasAttributes(type, new HashSet<Type>(), false, true))
                return false;
#endif
            using var child = property.Copy();
            using var end = property.GetEndProperty();
            var visited = new HashSet<long>();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = child.propertyType != SerializedPropertyType.String;
                if (child.propertyType != SerializedPropertyType.ManagedReference)
                    continue;
                enterChildren = visited.Add(child.managedReferenceId);
                if (!enterChildren || child.managedReferenceValue == null)
                    continue;
                var valueType = child.managedReferenceValue.GetType();
#if EDITOR_ATTRIBUTES_PACKAGE
                if (HasAttributes(valueType, new HashSet<Type>(), false, true))
                    return false;
#endif
                annotated |= HasAttributes(valueType, new HashSet<Type>());
            }
            return annotated;
        }

        private static bool CanRender(SerializedObject serializedObject)
        {
            bool annotated = false;
            foreach (var target in serializedObject.targetObjects)
            {
                if (!target || IsDisabled(target.GetType()))
                    return false;
                var type = target.GetType();
                if (!_annotatedTypes.TryGetValue(type, out var hasAttributes))
                {
                    hasAttributes = HasAttributes(type, new HashSet<Type>(), true);
                    _annotatedTypes.Add(type, hasAttributes);
                }
                annotated |= hasAttributes;

                // Alchemy eagerly expands managed references. Its renderer cannot traverse cycles safely.
                using var individual = new SerializedObject(target);
                using var property = individual.GetIterator();
                var visited = new Dictionary<long, string>();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = property.propertyType != SerializedPropertyType.String;
                    if (property.propertyType != SerializedPropertyType.ManagedReference)
                        continue;
                    if (property.managedReferenceValue == null)
                    {
                        enterChildren = false;
                        continue;
                    }
                    var id = property.managedReferenceId;
                    if (visited.TryGetValue(id, out var firstPath))
                    {
                        if (property.propertyPath.StartsWith(firstPath + ".", StringComparison.Ordinal))
                            return false;
                        enterChildren = false;
                        continue;
                    }
                    visited.Add(id, property.propertyPath);
                    annotated |= HasAttributes(property.managedReferenceValue.GetType(), new HashSet<Type>());
                }
            }
            return annotated;
        }
#endif

        public static VisualElement CreateInspectorGUI(UnityEditor.Editor owner, params string[] excludedProperties)
        {
#if ALCHEMY_PACKAGE
            if (_editorType == null || _excludedProperties == null || !owner || !owner.target)
                return null;
            try
            {
                if (!CanRender(owner.serializedObject))
                    return null;
            }
            catch (System.IO.FileNotFoundException)
            {
                return null;
            }
            catch (ReflectionTypeLoadException)
            {
                return null;
            }

            if (!_editors.TryGetValue(owner, out var editor) || !editor)
            {
                editor = UnityEditor.Editor.CreateEditor(owner.targets, _editorType);
                _editors[owner] = editor;
            }
            _excludedProperties.SetValue(editor, excludedProperties);
            var root = editor.CreateInspectorGUI();
            if (root == null)
            {
                OnDisable(owner);
                return null;
            }
            root.Bind(editor.serializedObject);
            return root;
#else
            return null;
#endif
        }

        public static VisualElement CreatePropertyGUI(UnityEditor.SerializedProperty property, string label = null, bool flatten = false)
        {
#if ALCHEMY_PACKAGE
            if (_createPropertyGUI == null || _getPropertyType == null || property == null ||
                property.propertyType is not (SerializedPropertyType.Generic or SerializedPropertyType.ManagedReference))
                return null;
            try
            {
                if (!HasPropertyAttributes(property) || !CanRender(property.serializedObject))
                    return null;
            }
            catch (System.IO.FileNotFoundException)
            {
                return null;
            }
            catch (ReflectionTypeLoadException)
            {
                return null;
            }
            return (VisualElement)_createPropertyGUI.Invoke(null, new object[] { property, label, flatten });
#else
            return null;
#endif
        }

        public static void OnDisable(UnityEditor.Editor owner)
        {
#if ALCHEMY_PACKAGE
            if (_editors.Remove(owner, out var editor) && editor)
                Object.DestroyImmediate(editor);
#endif
        }
    }
}
