using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PurrNet.Editor.Tests
{
    public class NetworkIdentityInspectorTests
    {
        [Test]
        public void UitkPropertyDrawerOnNetworkBehaviourField_IsBuilt()
        {
            var go = new GameObject("uitk-drawer-probe");
            UnityEditor.Editor editor = null;
            try
            {
                var probe = go.AddComponent<UitkDrawerProbe>();
                editor = UnityEditor.Editor.CreateEditor(probe);
                Assert.IsInstanceOf<NetworkIdentityInspector>(editor);

                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "inspector fell back to IMGUI, UI Toolkit drawers would show 'No GUI Implemented'");

                Assert.IsNotNull(root.Query<PropertyField>().Where(f => f.bindingPath == nameof(UitkDrawerProbe.probe)).First());
                Assert.IsNotNull(root.Q<IMGUIContainer>());
            }
            finally
            {
                if (editor)
                    Object.DestroyImmediate(editor);
                Object.DestroyImmediate(go);
            }
        }

        class ProbeWindow : EditorWindow { }

        [UnityTest]
        public IEnumerator UitkPropertyDrawerOnNetworkBehaviourField_Renders()
        {
            var go = new GameObject("uitk-drawer-probe");
            var probe = go.AddComponent<UitkDrawerProbe>();
            probe.probe = new UitkDrawerProbeValue { value = 42 };
            var editor = UnityEditor.Editor.CreateEditor(probe);
            var window = ScriptableObject.CreateInstance<ProbeWindow>();
            try
            {
                window.Show();
                var inspector = new InspectorElement(editor);
                window.rootVisualElement.Add(inspector);
                inspector.Bind(editor.serializedObject);

                for (int i = 0; i < 10; i++)
                    yield return null;

                var drawn = inspector.Q<Label>(UitkDrawerProbeDrawer.ElementName);
                Assert.IsNotNull(drawn, "UITK drawer element was not rendered");
                Assert.AreEqual("probe:42", drawn.text);
                Assert.IsNotNull(inspector.Q<IMGUIContainer>(), "IMGUI extras container missing");
                Assert.IsNotNull(drawn.panel, "drawer element is not attached to a panel");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(go);
            }
        }
    }
}
