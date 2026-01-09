using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrNetEditorSettings
    {
        static readonly HashSet<string> _keywords =
            new HashSet<string>(new[] { "PurrNet", "Networking", "Strip", "Multiplayer" });

        [SettingsProvider]
        public static SettingsProvider CreatePurrNetSettingsProvider()
        {
            var provider = new SettingsProvider("Project/Multiplayer/PurrNet", SettingsScope.Project)
            {
                keywords = _keywords,
                label = "PurrNet",
                guiHandler = GUIHandler
            };
            return provider;
        }

        private static void GUIHandler(string searchContext)
        {
            GUILayout.BeginVertical("helpbox");

            var settings = PurrNetSettings.GetOrCreateSettings();

            EditorGUI.BeginChangeCheck();

#if !UNITY_6000_3_OR_NEWER
            var toolbarResult = EditorGUILayout.EnumPopup(
                new GUIContent("Toolbar Mode",
                    "Defines how the PurrNet toolbar will be displayed in the Unity Editor. " +
                    "This can help customize your workflow."),
                settings.toolbarMode);
            settings.toolbarMode = (ToolbarMode)toolbarResult;

            settings.toolbarTransportDropDown = EditorGUILayout.Toggle(
                new GUIContent("Toolbar Transport"),
                settings.toolbarTransportDropDown);

            GUILayout.Space(10f);
#endif

            var stripCodeModeResult = EditorGUILayout.EnumPopup(
                new GUIContent("Strip Code Mode",
                    "Defines how PurrNet will handle unused RPCs and SyncVars in builds. " +
                    "This can help reduce build size and improve performance."),
                settings.stripCodeMode);
            settings.stripCodeMode = (StripCodeMode)stripCodeModeResult;

            var guardFailureActionResult = EditorGUILayout.EnumPopup(
                new GUIContent("Guard Failure Action",
                    "Defines how PurrNet will handle run context guarded methods. " +
                    "This can help organize your code."),
                settings.guardFailureAction);
            settings.guardFailureAction = (GuardFailureAction)guardFailureActionResult;

            if (EditorGUI.EndChangeCheck())
                PurrNetSettings.SaveSettings(settings);

            GUILayout.EndVertical();
        }
    }
}
