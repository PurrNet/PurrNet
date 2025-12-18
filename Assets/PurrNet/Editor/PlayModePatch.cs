#if UNITY_PLAYMODE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    /// <summary>
    /// Patches the Unity Multiplayer PlayMode TopView toolbar to inject custom PurrNet UI.
    /// Uses reflection to wrap the parent window's OnGUI delegate and inject custom toolbar elements.
    /// </summary>
    public static class PlayModePatch
    {
        private static readonly HashSet<EditorWindow> _windows = new();

        private const BindingFlags BINDING_FLAGS = BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.Instance | BindingFlags.Static;

        private static Assembly _unityEditorAssembly;
        private static Type _topViewType;
        private static Type _hostViewType;
        private static Type _editorWindowDelegateType;
        private static MethodInfo _wrappedGUIMethod;
        private static FieldInfo _parentField;
        private static FieldInfo _onGUIField;
        private static MethodInfo _originalOnGUIMethod;

        [InitializeOnLoadMethod]
        private static void Init()
        {

            _topViewType = AppDomain.CurrentDomain.GetAssemblies()
#if UNITY_6000_3_OR_NEWER
                .FirstOrDefault(a => a.GetName().Name == "UnityEditor")
                ?.GetType("Unity.Multiplayer.PlayMode.Editor.TopView");
#else
                .FirstOrDefault(a => a.GetName().Name == "Unity.Multiplayer.Playmode.Workflow.Editor")
                ?.GetType("Unity.Multiplayer.Playmode.Workflow.Editor.TopView");
#endif

            _hostViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView");
            _editorWindowDelegateType = _hostViewType?.GetNestedType("EditorWindowDelegate", BINDING_FLAGS);
            _wrappedGUIMethod = typeof(PlayModePatch).GetMethod(nameof(WrappedGUI), BINDING_FLAGS);
            _parentField = typeof(EditorWindow).GetField("m_Parent", BINDING_FLAGS);
            _onGUIField = _hostViewType?.GetField("m_OnGUI", BINDING_FLAGS);
            _originalOnGUIMethod = _topViewType?.GetMethod("OnGUI", BINDING_FLAGS);

            if (_topViewType == null || _wrappedGUIMethod == null || _editorWindowDelegateType == null)
                return;

            EditorApplication.update -= TryPatchTopViewGUI;
            EditorApplication.update += TryPatchTopViewGUI;
        }

        private static void TryPatchTopViewGUI()
        {
            var window = Resources.FindObjectsOfTypeAll(_topViewType).FirstOrDefault() as EditorWindow;
            if (window == null)
                return;

            var hostView = _parentField?.GetValue(window);
            if (hostView == null)
                return;

            var currentDelegate = _onGUIField?.GetValue(hostView) as Delegate;
            var currentMethod = currentDelegate?.Method;

            if (currentMethod == _wrappedGUIMethod)
                return;

            var wrappedDelegate = Delegate.CreateDelegate(_editorWindowDelegateType, window, _wrappedGUIMethod);

            _onGUIField?.SetValue(hostView, wrappedDelegate);
            window.Repaint();

            // EditorApplication.update -= TryPatchTopViewGUI;
        }

        /// <summary>
        /// Wrapped OnGUI that calls original Unity GUI and adds custom PurrNet toolbar
        /// </summary>
        private static void WrappedGUI(EditorWindow window)
        {
            if (window == null)
                return;

            try
            {
                CallOriginalOnGUI(window);
                OnGUI(window);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayModePatch] Error in WrappedGUI: {ex}");
            }
        }

        private static void CallOriginalOnGUI(EditorWindow window)
        {
            _originalOnGUIMethod?.Invoke(window, null);
        }

        /// <summary>
        /// Custom GUI to inject into the TopView toolbar
        /// </summary>
        public static void OnGUI(EditorWindow window)
        {
            _windows.Add(window);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            PurrNetToolBarStatus.OnToolbarGUI();
            GUILayout.Space(120f);
            GUILayout.EndHorizontal();
        }

        public static void Repaint()
        {
            foreach (var window in _windows)
            {
                if (window)
                    window.Repaint();
            }
        }
    }
}
#endif
