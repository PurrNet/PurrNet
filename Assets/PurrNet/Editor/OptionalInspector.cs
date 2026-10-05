using UnityEngine.UIElements;

namespace PurrNet.Editor
{
    /// <summary>Hosts optional attribute rendering while retaining specialised IMGUI controls.</summary>
    public class OptionalInspector : UnityEditor.Editor
    {
        protected bool drawingEditorAttributesExtras { get; private set; }
        protected virtual string[] editorAttributesExcludedProperties => null;
        protected virtual bool editorAttributesPropertiesEnabled => true;

        public override VisualElement CreateInspectorGUI()
        {
            var inspector = OptionalInspectorIntegration.CreateInspectorGUI(this, editorAttributesExcludedProperties);
            if (inspector == null)
                return null;

            var userProperties = new VisualElement();
            userProperties.Add(inspector);
            userProperties.SetEnabled(editorAttributesPropertiesEnabled);
            var root = new VisualElement();
            root.Add(userProperties);

            var extras = new IMGUIContainer(() =>
            {
                if (!target)
                    return;
                serializedObject.UpdateIfRequiredOrScript();
                drawingEditorAttributesExtras = true;
                try
                {
                    OnInspectorGUI();
                    serializedObject.ApplyModifiedProperties();
                }
                finally
                {
                    drawingEditorAttributesExtras = false;
                }
            });
            root.Add(extras);
            extras.schedule.Execute(() =>
            {
                if (!this || !target)
                    return;
                userProperties.SetEnabled(editorAttributesPropertiesEnabled);
                extras.MarkDirtyRepaint();
            }).Every(100);
            return root;
        }

        public override void OnInspectorGUI()
        {
            if (!drawingEditorAttributesExtras)
                base.OnInspectorGUI();
        }

        public new bool DrawDefaultInspector()
        {
            return !drawingEditorAttributesExtras && base.DrawDefaultInspector();
        }

        protected virtual void OnDisable()
        {
            OptionalInspectorIntegration.OnDisable(this);
        }

        protected virtual void OnSceneGUI()
        {
            OptionalInspectorIntegration.OnSceneGUI(this);
        }
    }
}
