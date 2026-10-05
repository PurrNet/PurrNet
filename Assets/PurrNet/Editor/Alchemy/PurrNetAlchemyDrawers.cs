using PurrNet.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using AlchemyAttributeDrawer = global::Alchemy.Editor.AlchemyAttributeDrawer;
using AlchemyPropertyField = global::Alchemy.Editor.Elements.AlchemyPropertyField;
using CustomAttributeDrawerAttribute = global::Alchemy.Editor.CustomAttributeDrawerAttribute;
using PropertyField = UnityEditor.UIElements.PropertyField;

namespace PurrNet.Editor.Alchemy
{
    internal static class DrawerElements
    {
        public static bool UsesNativeDrawer(VisualElement element)
        {
            return element is PropertyField || element is AlchemyPropertyField { FieldElement: PropertyField };
        }

        public static VisualElement Wrap(VisualElement element)
        {
            var parent = element.parent;
            var index = parent.IndexOf(element);
            var wrapper = new VisualElement();
            wrapper.style.width = Length.Percent(100);
            parent.Insert(index, wrapper);
            wrapper.Add(element);
            return wrapper;
        }
    }

    [CustomAttributeDrawer(typeof(PurrReadOnlyAttribute))]
    public sealed class PurrReadOnlyAlchemyDrawer : AlchemyAttributeDrawer
    {
        public override void OnCreateElement()
        {
            if (!DrawerElements.UsesNativeDrawer(TargetElement))
                // Conditional Alchemy drawers can re-enable the field itself; keep this ancestor disabled.
                DrawerElements.Wrap(TargetElement).SetEnabled(false);
        }
    }

    [CustomAttributeDrawer(typeof(PurrLockAttribute))]
    public sealed class PurrLockAlchemyDrawer : AlchemyAttributeDrawer
    {
        public override void OnCreateElement()
        {
            if (DrawerElements.UsesNativeDrawer(TargetElement))
                return;

            // Lock a separate ancestor so unlocking never overrides another attribute's disabled state.
            var wrapper = DrawerElements.Wrap(TargetElement);
            var target = SerializedObject.targetObject;
            void Refresh()
            {
                wrapper.SetEnabled(!Application.isPlaying || target && PrefabUtility.IsPartOfPrefabAsset(target));
            }
            Refresh();
            wrapper.schedule.Execute(Refresh).Every(100);
        }
    }

    [CustomAttributeDrawer(typeof(PurrDocsAttribute))]
    public sealed class PurrDocsAlchemyDrawer : AlchemyAttributeDrawer
    {
        public override void OnCreateElement()
        {
            // Primitive/type-drawer fields run the existing native docs drawer when they bind.
            if (DrawerElements.UsesNativeDrawer(TargetElement))
                return;

            var wrapper = DrawerElements.Wrap(TargetElement);
            wrapper.style.flexDirection = FlexDirection.Row;
            var docs = (PurrDocsAttribute)Attribute;
            var button = new Button(() => Application.OpenURL("https://purrnet.dev/docs/" + docs.url))
            {
                text = "?",
                tooltip = "Open documentation"
            };
            button.style.width = 20;
            wrapper.Insert(0, button);
            TargetElement.style.width = StyleKeyword.Auto;
            TargetElement.style.flexGrow = 1;
        }
    }
}
