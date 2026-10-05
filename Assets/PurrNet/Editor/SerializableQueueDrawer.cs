#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace PurrNet.Editor
{
    [CustomPropertyDrawer(typeof(SerializableQueue<>))]
    public class SerializableQueueDrawer : PropertyDrawer
    {
        private const float HeaderHeight = 10f;
        private const float ElementPadding = 2f;
        private const float BottomPadding = 8f;
        private const float ColumnHeaderHeight = 18f;
        private bool _foldout = true;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var foldout = new Foldout { text = property.displayName, value = _foldout };
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    _foldout = evt.newValue;
            });

            var rows = new VisualElement();
            foldout.Add(rows);
            var values = property.FindPropertyRelative("_values");
            var stringValues = property.FindPropertyRelative("_stringValues");
            int displayedCount = -1;
            bool displayedSerializableValues = false;

            void RefreshRows()
            {
                bool useSerializableValues = values != null && values.arraySize > 0;
                var displayValues = useSerializableValues ? values : stringValues;
                int count = displayValues?.arraySize ?? 0;
                if (count == displayedCount && useSerializableValues == displayedSerializableValues)
                    return;

                displayedCount = count;
                displayedSerializableValues = useSerializableValues;
                rows.Unbind();
                rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    var elementProperty = displayValues.GetArrayElementAtIndex(i);
                    var field = AlchemyIntegration.CreatePropertyGUI(elementProperty, string.Empty) ?? new PropertyField(elementProperty, string.Empty);
                    field.SetEnabled(false);
                    rows.Add(field);
                }
                rows.Bind(property.serializedObject);
            }

            RefreshRows();
            rows.schedule.Execute(RefreshRows).Every(100);
            return foldout;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var valuesProp = property.FindPropertyRelative("_values");
            var stringValuesProp = property.FindPropertyRelative("_stringValues");
            var displayValuesProp = (valuesProp != null && valuesProp.arraySize > 0) ? valuesProp : stringValuesProp;

            if (!_foldout) return HeaderHeight;

            float totalHeight = HeaderHeight + ColumnHeaderHeight;
            int count = displayValuesProp?.arraySize ?? 0;
            totalHeight += (EditorGUIUtility.singleLineHeight + ElementPadding) * count;
            totalHeight += BottomPadding;

            return totalHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var valuesProp = property.FindPropertyRelative("_values");
            var stringValuesProp = property.FindPropertyRelative("_stringValues");

            var useSerializableTypes = valuesProp != null && valuesProp.arraySize > 0;
            var displayValuesProp = useSerializableTypes ? valuesProp : stringValuesProp;

            Rect headerRect = new Rect(position.x, position.y, position.width, HeaderHeight);
            _foldout = EditorGUI.Foldout(headerRect, _foldout, label, true);

            if (_foldout)
            {
                EditorGUI.indentLevel++;
                float yOffset = HeaderHeight;

                //Rect headerBgRect = new Rect(position.x, position.y + yOffset, position.width, ColumnHeaderHeight);
                //EditorGUI.DrawRect(headerBgRect, new Color(0.7f, 0.7f, 0.7f, 0.1f));

                float headerLabelOffset = 2f;
                Rect valueHeaderRect = new Rect(position.x, position.y + yOffset + headerLabelOffset,
                    position.width * 0.9f, EditorGUIUtility.singleLineHeight);

                yOffset += ColumnHeaderHeight;

                if (displayValuesProp != null)
                {
                    int count = displayValuesProp.arraySize;
                    for (int i = 0; i < count; i++)
                    {
                        float elementHeight = EditorGUIUtility.singleLineHeight;
                        Rect valueRect = new Rect(position.x, position.y + yOffset, position.width * 0.9f,
                            elementHeight);

                        if (i % 2 == 1)
                        {
                            Rect rowBgRect = new Rect(position.x, position.y + yOffset, position.width, elementHeight);
                            EditorGUI.DrawRect(rowBgRect, new Color(0.7f, 0.7f, 0.7f, 0.05f));
                        }

                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUI.PropertyField(valueRect, displayValuesProp.GetArrayElementAtIndex(i),
                                GUIContent.none);
                        }

                        yOffset += elementHeight + ElementPadding;
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
