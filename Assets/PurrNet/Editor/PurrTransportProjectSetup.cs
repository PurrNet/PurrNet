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
        private const string LinkAfterInstallKey = "PurrTransport.linkAfterInstall";

        private static bool _busy;
        private static bool _linkAfterSignIn;
        private static bool _linkAfterSignInEditorProfile;
        private static bool _askedFreeOffer;
        private static bool _relayFree;

        /// <summary>Whether the website says the relay is free (asked once per editor session; false until it answers).</summary>
        private static bool relayFree
        {
            get
            {
                if (!_askedFreeOffer)
                {
                    _askedFreeOffer = true;
                    AskFreeOffer();
                }
                return _relayFree;
            }
        }

        private static async void AskFreeOffer()
        {
            try
            {
                var offer = await PurrPackageManagerAPI.GetFreeOffer();
                if (!offer.Success || offer.Value?.free == null || !offer.Value.free.TryGetValue("relay", out var free) || free == _relayFree)
                    return;
                _relayFree = free;
                RepaintInspectors();
            }
            catch
            {
                // ignore
            }
        }

        public static void Draw()
        {
            var editorProfile = PurrServicesProjectLink.isEditorProfile;

            if (PurrServicesProjectLink.isLinked)
            {
                var name = PurrServicesProjectLink.projectName ?? PurrServicesProjectLink.projectId;
                var where = editorProfile ? "Editor" : "Editor & builds";
                var buildName = PurrServicesProjectLink.buildProjectName ?? PurrServicesProjectLink.buildProjectId;
                var tooltip = (relayFree
                                  ? "Relay rooms belong to this PurrNet project: free with unlimited players (fair use), "
                                  : "Relay rooms belong to this PurrNet project: metered, ") +
                              "isolated from every other project, and shown live on its dashboard.";
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
                        "Play Mode uses the shared test relay (Unity Editor override without a project). " +
                        $"Builds use \"{buildName}\"."),
                    $"Test relay (Editor)  ·  builds: {buildName}");
                return;
            }

            EditorGUILayout.HelpBox(relayFree
                    ? "Limited mode: test relay only, not for production. Link a free project to ship (unlimited players, fair use)."
                    : "Limited mode: test relay only, not for production. Link a free PurrNet project before you ship.",
                MessageType.Warning);

            var installed = PurrPackageQuickInstall.IsInstalled(PackageName);
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;

            if (installed && !playing && SessionState.GetBool(LinkAfterInstallKey, false))
            {
                SessionState.EraseBool(LinkAfterInstallKey);
                EditorApplication.delayCall += () => StartLink(editorProfile);
            }

            using (new EditorGUI.DisabledScope(_busy || _linkAfterSignIn || PurrPackageQuickInstall.isInstalling || playing))
            {
                if (!installed)
                {
                    if (GUILayout.Button(PurrPackageQuickInstall.isInstalling ? "Installing PurrServices…" : "Install PurrServices & link"))
                    {
                        SessionState.SetBool(LinkAfterInstallKey, true);
                        _ = PurrPackageQuickInstall.InstallByUpmName(PackageName);
                    }
                }
                else
                {
                    var label = _busy ? "Linking…"
                        : _linkAfterSignIn ? "Waiting for sign-in…"
                        : PurrPackageManagerAuth.HasApiKey() ? "Link free project"
                        : "Sign in & link free project";
                    if (GUILayout.Button(label))
                        StartLink(editorProfile);
                }
            }

            if (playing)
                EditorGUILayout.HelpBox("Exit Play Mode to link a project.", MessageType.None);
        }

        private static void ProjectRow(GUIContent label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                GUILayout.Label(new GUIContent(value, label.tooltip), EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.MinWidth(0));
                if (GUILayout.Button(new GUIContent("...", "Open PurrServices"), GUILayout.ExpandWidth(false)) &&
                    !EditorApplication.ExecuteMenuItem(SetupMenuPath))
                    Debug.LogError($"Could not open {SetupMenuPath}. Install or update PurrServices.");
            }
        }

        private static string UnityProjectName()
        {
            var directory = Directory.GetParent(Application.dataPath);
            return directory?.Name ?? Application.productName ?? "Unity Project";
        }

        private static void StartLink(bool editorProfile)
        {
            if (_busy || _linkAfterSignIn) return;
            if (PurrPackageManagerAuth.HasApiKey())
            {
                Link(editorProfile);
                return;
            }

            _linkAfterSignInEditorProfile = editorProfile;
            _linkAfterSignIn = true;
            PurrPackageManagerAuth.onAuthChanged += OnAuthChanged;
            PurrPackageManagerAuth.Login();
            RepaintInspectors();
        }

        private static void OnAuthChanged()
        {
            PurrPackageManagerAuth.onAuthChanged -= OnAuthChanged;
            _linkAfterSignIn = false;
            if (PurrPackageManagerAuth.HasApiKey())
                Link(_linkAfterSignInEditorProfile);
            else
                RepaintInspectors();
        }

        /// <summary>Creates a PurrNet project named after this Unity project and links it, through PurrServices.</summary>
        private static async void Link(bool editorProfile)
        {
            if (_busy) return;

            var method = Type.GetType(SetupWindowTypeName)?.GetMethod("CreateAndLinkProject", new[] { typeof(string), typeof(bool) });
            if (method == null)
            {
                EditorUtility.DisplayDialog("PurrServices", "Update PurrServices to link a project from here.", "OK");
                return;
            }

            var projectName = UnityProjectName();
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
                    EditorUtility.DisplayDialog("Could not link a project", result.Error ?? "Unknown error.", "OK");
                    return;
                }

                Debug.Log($"[PurrTransport] Linked PurrNet project '{projectName}' to " +
                          (editorProfile ? "the Unity Editor override." : "Player Builds."));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not link a project", exception.GetBaseException().Message, "OK");
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
