using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using AlchemyPropertyField = global::Alchemy.Editor.Elements.AlchemyPropertyField;
using DisableAlchemyEditorAttribute = global::Alchemy.Inspector.DisableAlchemyEditorAttribute;
using HideScriptFieldAttribute = global::Alchemy.Inspector.HideScriptFieldAttribute;

namespace PurrNet.Editor.Alchemy
{
    /// <summary>Optional Alchemy editor backend, preserving its native editor lifecycle.</summary>
    [CanEditMultipleObjects]
    public sealed class AlchemyEditorBridge : global::Alchemy.Editor.AlchemyEditor
    {
        private static readonly Assembly _assembly = typeof(global::Alchemy.Editor.AlchemyEditor).Assembly;
        private static readonly Type _inspectorHelper = _assembly.GetType("Alchemy.Editor.InspectorHelper");
        private static readonly Type _propertyExtensions = _assembly.GetType("Alchemy.Editor.SerializedPropertyExtensions");
        private static readonly MethodInfo _buildElements = _inspectorHelper?.GetMethod("BuildElements",
            BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(SerializedObject), typeof(VisualElement), typeof(object), typeof(Func<string, SerializedProperty>) }, null);
        private static readonly MethodInfo _getPropertyType = _propertyExtensions?.GetMethod("GetPropertyType",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(SerializedProperty), typeof(bool) }, null);
        private static readonly MethodInfo _getFieldInfo = _propertyExtensions?.GetMethod("GetFieldInfo",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(SerializedProperty) }, null);
        private static readonly MethodInfo _getValue = _propertyExtensions?.GetMethod("GetValue",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(SerializedProperty) }, null)?.MakeGenericMethod(typeof(object));

        public string[] excludedProperties;

        public override VisualElement CreateInspectorGUI()
        {
            if (_buildElements == null || !target)
                return null;
            if (excludedProperties == null || excludedProperties.Length == 0)
                return base.CreateInspectorGUI();

            var targetType = target.GetType();
            if (targetType.IsDefined(typeof(DisableAlchemyEditorAttribute), true))
                return null;

            var root = new VisualElement();
            var excluded = new HashSet<string>(excludedProperties);
            if (targets.Length > 1 && targetType.GetCustomAttributes(true).Any(attribute =>
                    attribute.GetType().FullName == "Alchemy.Serialization.AlchemySerializeAttribute"))
                root.Add(new HelpBox("In the current version, fields with the [AlchemySerializedField] attribute do not support editing multiple objects.",
                    HelpBoxMessageType.Error));

            if (!excluded.Contains("m_Script") && !targetType.IsDefined(typeof(HideScriptFieldAttribute), true))
            {
                var script = serializedObject.FindProperty("m_Script");
                if (script != null)
                {
                    var scriptField = new PropertyField(script);
                    scriptField.SetEnabled(false);
                    root.Add(scriptField);
                    root.Add(new VisualElement { style = { height = EditorGUIUtility.standardVerticalSpacing * 0.5f } });
                }
            }

            Func<string, SerializedProperty> findProperty = name => excluded.Contains(name) ? null : serializedObject.FindProperty(name);
            BuildElements(serializedObject, root, target, findProperty);
            return root;
        }

        /// <summary>Draws nested Alchemy members inside a native PurrNet property drawer.</summary>
        public static VisualElement CreatePropertyGUI(SerializedProperty property, string label, bool flatten)
        {
            if (property == null || _buildElements == null ||
                property.propertyType is not (SerializedPropertyType.Generic or SerializedPropertyType.ManagedReference))
                return null;
            var type = GetPropertyType(property);
            if (type == null)
                return null;

            if (flatten && property.propertyType == SerializedPropertyType.Generic && !property.isArray)
            {
                if (_getValue == null)
                    return null;
                var value = _getValue.Invoke(null, new object[] { property });
                if (value == null)
                    return null;
                var root = new VisualElement();
                Func<string, SerializedProperty> findProperty = property.FindPropertyRelative;
                BuildElements(property.serializedObject, root, value, findProperty);
                return root;
            }

            var isCollectionElement = property.propertyPath.EndsWith("]", StringComparison.Ordinal);
            var field = new AlchemyPropertyField(property, type, isCollectionElement);
            if (label != null)
                field.Label = label;
            return field;
        }

        /// <summary>Resolves the actual managed-reference type or the serialized field's declared type.</summary>
        public static Type GetPropertyType(SerializedProperty property)
        {
            if (property == null)
                return null;
            if (property.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceValue != null)
                return property.managedReferenceValue.GetType();
            if (_getFieldInfo == null || _getPropertyType == null)
                return null;
            try
            {
                // Built-in Unity fields can have no reflected field; Alchemy's getter assumes one exists.
                if (_getFieldInfo.Invoke(null, new object[] { property }) is not FieldInfo)
                    return null;
                var isCollectionElement = property.propertyPath.EndsWith("]", StringComparison.Ordinal);
                return _getPropertyType.Invoke(null, new object[] { property, isCollectionElement }) as Type;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private static void BuildElements(SerializedObject serializedObject, VisualElement root, object value,
            Func<string, SerializedProperty> findProperty)
        {
            _buildElements.Invoke(null, new object[] { serializedObject, root, value, findProperty });
        }
    }
}
