#if PURR_BUTTONS
using System.Collections.Generic;
using System.Reflection;
using PurrNet.Editor;
using PurrNet.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PurrNet
{
    [CustomEditor(typeof(MonoBehaviour), true)]
#if TRI_INSPECTOR_PACKAGE
    public class PurrButtonEditor : TriInspector.Editors.TriEditor
#elif ODIN_INSPECTOR
    public class PurrButtonEditor : Sirenix.OdinInspector.Editor.OdinEditor
#else
    public class PurrButtonEditor : UnityEditor.Editor
#endif
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = OptionalInspectorIntegration.CreateInspectorGUI(this);
            if (root == null)
                return null;
            root.Add(new IMGUIContainer(DrawButtons));
            return root;
        }

#if TRI_INSPECTOR_PACKAGE || ODIN_INSPECTOR
        protected override void OnDisable()
#else
        protected virtual void OnDisable()
#endif
        {
            OptionalInspectorIntegration.OnDisable(this);
#if TRI_INSPECTOR_PACKAGE || ODIN_INSPECTOR
            base.OnDisable();
#endif
        }

        protected virtual void OnSceneGUI()
        {
            OptionalInspectorIntegration.OnSceneGUI(this);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            DrawButtons();
        }

        private void DrawButtons()
        {
            if (!target)
                return;

            var allMethods = new List<MethodInfo>();
            var current = target.GetType();
            while (current != null && current != typeof(MonoBehaviour))
            {
                allMethods.AddRange(current.GetMethods(BindingFlags.Instance | BindingFlags.DeclaredOnly |
                                                       BindingFlags.NonPublic | BindingFlags.Public));
                current = current.BaseType;
            }

            foreach (var method in allMethods)
            {
                var attr = method.GetCustomAttribute<PurrButtonAttribute>();
                if (attr == null)
                    continue;

                var buttonName = !string.IsNullOrEmpty(attr.ButtonName)
                    ? attr.ButtonName : ObjectNames.NicifyVariableName(method.Name);
                if (!GUILayout.Button(buttonName))
                    continue;

                if (method.GetParameters().Length == 0)
                    method.Invoke(target, null);
                else
                    PurrLogger.LogWarning($"Cannot invoke method '{method.Name}' with PurrButton because it has parameters.");
            }
        }
    }
}
#endif
