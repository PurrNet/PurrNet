using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class InstallCompatiblePackages
    {
        const string PACKAGES = "Tools/PurrNet/Packages";

        [UsedImplicitly]
        public static void VerifyEdgegap()
        {
#if EDGEGAP_PURRNET_SUPPORT
            VerifyEdgegapInternal();
#endif
        }

        [UsedImplicitly]
        private static void OpenDockerInstallInstructions()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    Application.OpenURL("https://docs.docker.com/desktop/setup/install/windows-install/");
                    break;
                case RuntimePlatform.OSXEditor:
                    Application.OpenURL("https://docs.docker.com/desktop/setup/install/mac-install/");
                    break;
                case RuntimePlatform.LinuxEditor:
                    Application.OpenURL("https://docs.docker.com/desktop/setup/install/linux/");
                    break;
                default:
                    Application.OpenURL("https://docs.docker.com/engine/install/");
                    break;
            }
        }

#if EDGEGAP_PURRNET_SUPPORT
        static bool HasLinuxBuildSupport()
        {
            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
                return true;
            return false;
        }


        [MenuItem("Tools/PurrNet/Edgegap/Verify Requirements", priority = 201)]
        static void VerifyEdgegapInternal()
        {
            EditorUtility.DisplayProgressBar("Verifying Edgegap Requirements", "Please wait...", 0.5f);

            var summary = new System.Text.StringBuilder();

            string buttonText;
            System.Action buttonAction;

            if (!ToolChecker.CheckTool("docker"))
            {
                summary.AppendLine("Docker is not installed.");
                buttonText = "Install Docker";
                buttonAction = OpenDockerInstallInstructions;
            }
            else if (!ToolChecker.CheckTool("docker", "ps"))
            {
                summary.AppendLine("Docker is not running.");
                summary.AppendLine("If you have docker desktop installed, please start it.");
                buttonText = "Open Docker Docs";
                buttonAction = OpenDockerInstallInstructions;
            }
            else if (!HasLinuxBuildSupport())
            {
                summary.AppendLine($"Linux modules are not installed for {Application.unityVersion}.");
                summary.AppendLine($"- Linux Build Support (Mono and or IL2CPP)");
                summary.AppendLine($"- Linux Dedicated Server Build Support");
                summary.AppendLine($"(Unity restart is required once installed)");
                buttonText = "Open Unity Hub";
                buttonAction = () =>
                {
                    EditorUtility.DisplayProgressBar("Opening Unity Hub", "Please wait...", 1f);
                    ToolChecker.CheckTool(UnityHubHelper.Path);
                    EditorUtility.ClearProgressBar();
                };
            }
            else
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Edgegap Requirements Verified", "All requirements are met.", "Ok");
                return;
            }

            string cancelButton = buttonText != "Ok" ? "Ok" : string.Empty;

            EditorUtility.ClearProgressBar();

            if (EditorUtility.DisplayDialog("Missing Edgegap Requirements", summary.ToString(), buttonText, cancelButton))
                buttonAction.Invoke();
        }

        [MenuItem(PACKAGES + "/Edgegap/Update Edgegap", priority = 101)]
        public static void UpdateEdgegap()
        {
            Client.Remove("com.edgegap.unity-servers-plugin");
            Client.Add(
                "https://github.com/edgegap/edgegap-unity-plugin.git#partner/purrnet-source");
            Client.Resolve();
        }

        [MenuItem(PACKAGES + "/Edgegap/Uninstall Edgegap", priority = 102)]
        public static void UninstallEdgegap()
        {
            if (EditorUtility.DisplayDialog("Uninstall Edgegap", "This will remove Edgegap from the package manager. Do you want to continue?", "Yes", "No"))
            {
                Client.Remove("com.edgegap.unity-servers-plugin");
                SymbolsHelper.RemoveSymbol("EDGEGAP_PLUGIN_SERVERS");
                Client.Resolve();
            }
        }
#else
        [MenuItem(PACKAGES + "/Install Edgegap", priority = 100)]
        public static void InstallEdgegap()
        {
            if (!GitHelper.CheckGit())
                return;

            Client.Add("https://github.com/edgegap/edgegap-unity-plugin.git#partner/purrnet-source");
            Client.Resolve();
        }
#endif

#if UNITASK_PURRNET_SUPPORT
        [MenuItem(PACKAGES + "/Uninstall UniTask", priority = 101)]
        public static void UninstallUniTask()
        {
            if (EditorUtility.DisplayDialog("Uninstall UniTask", "This will remove UniTask from the package manager. Do you want to continue?", "Yes", "No"))
            {
                Client.Remove("com.cysharp.unitask");
                Client.Resolve();
            }
        }
#else
        [MenuItem(PACKAGES + "/Install UniTask", priority = 104)]
        public static void InstallUniTask()
        {
            if (!GitHelper.CheckGit())
                return;

            if (EditorUtility.DisplayDialog("Install UniTask",
                    "This will install UniTask from the package manager. Do you want to continue?", "Yes", "No"))
            {
                Client.Add("https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask");
                Client.Resolve();
            }
        }
#endif
    }
}
