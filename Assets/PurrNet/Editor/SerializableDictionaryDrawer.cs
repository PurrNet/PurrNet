#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace PurrNet.Editor
{
    [CustomPropertyDrawer(typeof(SerializableDictionary<,>))]
    public class SerializableDictionaryDrawer : PropertyDrawer
    {
        private const float HeaderHeight = 20f;
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

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            var keyHeader = new Label("Key");
            var valueHeader = new Label("Value");
            keyHeader.style.flexGrow = valueHeader.style.flexGrow = 1;
            keyHeader.style.flexBasis = valueHeader.style.flexBasis = 0;
            header.Add(keyHeader);
            header.Add(valueHeader);
            foldout.Add(header);

            var rows = new VisualElement();
            foldout.Add(rows);
            var keys = property.FindPropertyRelative("keys");
            var values = property.FindPropertyRelative("values");
            var stringKeys = property.FindPropertyRelative("stringKeys");
            var stringValues = property.FindPropertyRelative("stringValues");
            int displayedCount = -1;
            bool displayedSerializableKeys = false;

            void RefreshRows()
            {
                bool useSerializableKeys = keys != null && keys.arraySize > 0;
                var displayKeys = useSerializableKeys ? keys : stringKeys;
                var displayValues = useSerializableKeys ? values : stringValues;
                int count = Mathf.Min(displayKeys?.arraySize ?? 0, displayValues?.arraySize ?? 0);
                if (count == displayedCount && useSerializableKeys == displayedSerializableKeys)
                    return;

                displayedCount = count;
                displayedSerializableKeys = useSerializableKeys;
                rows.Unbind();
                rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    var keyProperty = displayKeys.GetArrayElementAtIndex(i);
                    var valueProperty = displayValues.GetArrayElementAtIndex(i);
                    var key = AlchemyIntegration.CreatePropertyGUI(keyProperty, string.Empty) ?? new PropertyField(keyProperty, string.Empty);
                    var value = AlchemyIntegration.CreatePropertyGUI(valueProperty, string.Empty) ?? new PropertyField(valueProperty, string.Empty);
                    key.style.flexGrow = value.style.flexGrow = 1;
                    key.style.flexBasis = value.style.flexBasis = 0;
                    row.Add(key);
                    row.Add(value);
                    row.SetEnabled(false);
                    rows.Add(row);
                }
                rows.Bind(property.serializedObject);
            }

            RefreshRows();
            rows.schedule.Execute(RefreshRows).Every(100);
            return foldout;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!_foldout) return HeaderHeight;

            var keysProp = property.FindPropertyRelative("keys");
            var stringKeysProp = property.FindPropertyRelative("stringKeys");
            var hasKeys = keysProp != null && keysProp.arraySize > 0;
            var displayKeysProp = hasKeys ? keysProp : stringKeysProp;

            float totalHeight = HeaderHeight + ColumnHeaderHeight;

            int count = displayKeysProp?.arraySize ?? 0;
            if (count > 0)
            {
                var valuesProp = property.FindPropertyRelative("values");
                var stringValuesProp = property.FindPropertyRelative("stringValues");
                var displayValuesProp = hasKeys ? valuesProp : stringValuesProp;

                for (int i = 0; i < count; i++)
                {
                    float keyHeight = EditorGUI.GetPropertyHeight(displayKeysProp.GetArrayElementAtIndex(i), true);
                    float valHeight = EditorGUI.GetPropertyHeight(displayValuesProp.GetArrayElementAtIndex(i), true);
                    
                    totalHeight += Mathf.Max(keyHeight, valHeight) + ElementPadding;
                }
            }
            else
            {
                totalHeight += EditorGUIUtility.singleLineHeight + ElementPadding;
            }

            return totalHeight + BottomPadding;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var keysProp = property.FindPropertyRelative("keys");
            var valuesProp = property.FindPropertyRelative("values");
            var stringKeysProp = property.FindPropertyRelative("stringKeys");
            var stringValuesProp = property.FindPropertyRelative("stringValues");

            bool hasKeys = keysProp != null && keysProp.arraySize > 0;
            var displayKeysProp = hasKeys ? keysProp : stringKeysProp;
            var displayValuesProp = hasKeys ? valuesProp : stringValuesProp;

            Rect headerRect = new Rect(position.x, position.y, position.width, HeaderHeight);
            _foldout = EditorGUI.Foldout(headerRect, _foldout, label, true);

            if (_foldout)
            {
                EditorGUI.indentLevel++;
                float yOffset = HeaderHeight;

                Rect headerBgRect = new Rect(position.x, position.y + yOffset, position.width, ColumnHeaderHeight);
                EditorGUI.DrawRect(headerBgRect, new Color(0.7f, 0.7f, 0.7f, 0.1f));

                float labelWidth = position.width * 0.45f;
                Rect keyHeaderRect = new Rect(position.x, position.y + yOffset, labelWidth, ColumnHeaderHeight);
                Rect valueHeaderRect = new Rect(position.x + position.width * 0.5f, position.y + yOffset, labelWidth, ColumnHeaderHeight);

                EditorGUI.LabelField(keyHeaderRect, "Key", EditorStyles.miniBoldLabel);
                EditorGUI.LabelField(valueHeaderRect, "Value", EditorStyles.miniBoldLabel);

                yOffset += ColumnHeaderHeight;

                int count = displayKeysProp?.arraySize ?? 0;
                for (int i = 0; i < count; i++)
                {
                    var keyElem = displayKeysProp.GetArrayElementAtIndex(i);
                    var valElem = displayValuesProp.GetArrayElementAtIndex(i);

                    float keyHeight = EditorGUI.GetPropertyHeight(keyElem, true);
                    float valHeight = EditorGUI.GetPropertyHeight(valElem, true);
                    float rowHeight = Mathf.Max(keyHeight, valHeight);

                    Rect keyRect = new Rect(position.x, position.y + yOffset, labelWidth, keyHeight);
                    Rect valueRect = new Rect(position.x + position.width * 0.5f, position.y + yOffset, labelWidth, valHeight);

                    if (i % 2 == 1)
                    {
                        Rect rowBgRect = new Rect(position.x, position.y + yOffset, position.width, rowHeight);
                        EditorGUI.DrawRect(rowBgRect, new Color(0.7f, 0.7f, 0.7f, 0.05f));
                    }

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.PropertyField(keyRect, keyElem, GUIContent.none, true);
                        EditorGUI.PropertyField(valueRect, valElem, GUIContent.none, true);
                    }

                    yOffset += rowHeight + ElementPadding;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
