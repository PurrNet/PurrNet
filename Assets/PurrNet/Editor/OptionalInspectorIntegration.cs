using UnityEngine.UIElements;

namespace PurrNet.Editor
{
    /// <summary>Selects an annotated inspector while keeping existing adapters as the fallback.</summary>
    public static class OptionalInspectorIntegration
    {
        public static VisualElement CreateInspectorGUI(UnityEditor.Editor owner, params string[] excludedProperties)
        {
            return EditorAttributesIntegration.CreateInspectorGUI(owner, excludedProperties) ??
                   AlchemyIntegration.CreateInspectorGUI(owner, excludedProperties);
        }

        public static void OnDisable(UnityEditor.Editor owner)
        {
            EditorAttributesIntegration.OnDisable(owner);
            AlchemyIntegration.OnDisable(owner);
        }

        public static void OnSceneGUI(UnityEditor.Editor owner)
        {
            EditorAttributesIntegration.OnSceneGUI(owner);
        }
    }
}
