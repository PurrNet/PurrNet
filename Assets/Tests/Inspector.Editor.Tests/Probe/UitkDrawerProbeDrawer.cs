#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;

namespace PurrNet.Editor.Tests
{
    [CustomPropertyDrawer(typeof(UitkDrawerProbeValue))]
    public class UitkDrawerProbeDrawer : PropertyDrawer
    {
        public const string ElementName = "uitk-drawer-probe-element";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            return new Label("probe:" + property.FindPropertyRelative(nameof(UitkDrawerProbeValue.value)).intValue)
            {
                name = ElementName
            };
        }
    }
}
#endif
