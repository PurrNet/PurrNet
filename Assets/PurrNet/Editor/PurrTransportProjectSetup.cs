using System;
using System.IO;
using System.Threading.Tasks;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    internal static class PurrTransportProjectSetup
    {
        private const string PackageName = "dev.purrnet.services";
        private const string SetupMenuPath = "Tools/PurrNet/PurrServices";
        private const string SetupWindowTypeName = "PurrNet.Services.Editor.PurrServicesSetupWindow, PurrServices.Editor";

        private static bool _busy;

        public static void Draw()
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Production use", EditorStyles.boldLabel);

            var editorProfile = PurrServicesProjectLink.isEditorProfile;
            var profileName = editorProfile ? "Unity Editor override" : "Player Builds";

            if (PurrServicesProjectLink.isLinked)
            {
                var name = PurrServicesProjectLink.projectName ?? PurrServicesProjectLink.projectId;
                var where = editorProfile ? "Editor" : "Editor & builds";
                var buildName = PurrServicesProjectLink.buildProjectName ?? PurrServicesProjectLink.buildProjectId;
                var tooltip = $"Relay rooms belong to this PurrNet project: isolated from every other project, " +
                              "metered, and shown live on its dashboard.";
                if (editorProfile)
                    tooltip += PurrServicesProjectLink.isBuildLinked
                        ? $" Builds use \"{buildName}\"."
                        : " Builds have no project linked yet.";
                ProjectRow(new GUIContent("Project", tooltip), $"{name}  ({where})");
                return;
            }

            if (editorProfile && PurrServicesProjectLink.isBuildLinked)
            {
                var buildName = PurrServicesProjectLink.buildProjectName ?? PurrServicesProjectLink.buildProjectId;
                ProjectRow(new GUIContent("Project",
                        "Play Mode uses the shared development relay (Unity Editor override without a project). " +
                        $"Builds use \"{buildName}\"."),
                    $"Development relay (Editor)  ·  builds: {buildName}");
                return;
            }

            EditorGUILayout.HelpBox(
                "NOT FOR PRODUCTION. No PurrNet project is linked, so this transport uses the shared " +
                "development relay: rooms are anonymous, unmetered and may be dropped at any time. " +
                "It works in builds, but shipping a game on it is not allowed. Link your project to " +
                "move to production: isolated rooms, live usage and support on your dashboard.",
                MessageType.Warning);

            var installed = PurrPackageQuickInstall.IsInstalled(PackageName);
            var blocked = _busy || PurrPackageQuickInstall.isInstalling || EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(blocked))
            {
                if (!installed)
                {
                    if (GUILayout.Button(PurrPackageQuickInstall.isInstalling ? "Installing PurrServices…" : "Install PurrServices"))
                        _ = PurrPackageQuickInstall.InstallByUpmName(PackageName);
                }
                else
                {
                    var unityProjectName = UnityProjectName();
                    if (GUILayout.Button(_busy ? $"Creating {unityProjectName}…" : $"Create & Link ({unityProjectName})"))
                        CreateAndLink(unityProjectName, editorProfile);
                    if (GUILayout.Button("Open PurrServices", GUILayout.ExpandWidth(false)))
                        OpenSetupWindow();
                }
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Exit Play Mode before changing the linked project.", MessageType.None);
        }

        private static void ProjectRow(GUIContent label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                GUILayout.Label(new GUIContent(value, label.tooltip), EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.MinWidth(0));
                if (GUILayout.Button("Open PurrServices", GUILayout.ExpandWidth(false)))
                    OpenSetupWindow();
            }
        }

        private static string UnityProjectName()
        {
            var directory = Directory.GetParent(Application.dataPath);
            return directory?.Name ?? Application.productName ?? "Unity Project";
        }

        private static void OpenSetupWindow()
        {
            if (!EditorApplication.ExecuteMenuItem(SetupMenuPath))
                Debug.LogError($"Could not open {SetupMenuPath}. Install or update PurrServices.");
        }

        private static async void CreateAndLink(string projectName, bool editorProfile)
        {
            if (_busy) return;

            var windowType = Type.GetType(SetupWindowTypeName);
            var method = windowType?.GetMethod("CreateAndLinkProject", new[] { typeof(string), typeof(bool) });
            if (method == null)
            {
                EditorUtility.DisplayDialog("PurrServices", "Update PurrServices to use one-click project setup.", "OK");
                return;
            }

            if (!PurrPackageManagerAuth.HasApiKey())
            {
                EditorUtility.DisplayDialog("PurrServices", "Sign in to PurrNet (Tools → PurrNet) before creating a project.", "OK");
                return;
            }

            _busy = true;
            RepaintInspectors();
            try
            {
                var invocation = method.Invoke(null, new object[] { projectName, editorProfile });
                var result = invocation is Task<Result<bool>> task
                    ? await task
                    : Result<bool>.Fail("The installed PurrServices version returned an unsupported result.");
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Could not create project", result.Error ?? "Unknown error.", "OK");
                    return;
                }

                Debug.Log($"[PurrTransport] Created PurrNet project '{projectName}' and linked it to " +
                          (editorProfile ? "the Unity Editor override." : "Player Builds."));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not create project", exception.GetBaseException().Message, "OK");
            }
            finally
            {
                _busy = false;
                RepaintInspectors();
            }
        }

        private static void RepaintInspectors()
        {
            foreach (var editor in Resources.FindObjectsOfTypeAll<UnityEditor.Editor>())
                editor.Repaint();
        }
    }
}
