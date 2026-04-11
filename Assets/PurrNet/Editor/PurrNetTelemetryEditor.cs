using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrNetTelemetryEditor
    {
        const string OPT_OUT_KEY = "PurrNet_Telemetry_OptOut";

        [InitializeOnLoadMethod]
        static void Init()
        {
            if (!PurrTelemetry.IsEnabled)
                return;

            ShowFirstRunNotice();
            SendProjectStart();
            SendSteamSession();
        }

        static void ShowFirstRunNotice()
        {
            var key = "PurrNet_Telemetry_Notice_" + Application.dataPath.GetHashCode();

            if (EditorPrefs.GetBool(key, false))
                return;

            EditorPrefs.SetBool(key, true);

            Debug.Log(
                "[PurrNet] Anonymous telemetry is enabled to help improve PurrNet. " +
                "No personal data is collected. Disable via Tools > PurrNet > Disable Telemetry. " +
                "See TELEMETRY.md for details.");
        }

        static void SendProjectStart()
        {
            if (SessionState.GetBool("PurrNet_Telemetry_ProjectStartSent", false))
                return;

            SessionState.SetBool("PurrNet_Telemetry_ProjectStartSent", true);

            var metadata = new Dictionary<string, object>
            {
                ["purrnet_version"] = NetworkManager.version,
                ["unity_version"] = Application.unityVersion,
                ["os"] = SystemInfo.operatingSystem
            };

            PurrTelemetry.SendEvent("project_start", metadata);
        }

        static void SendSteamSession()
        {
            if (SessionState.GetBool("PurrNet_Telemetry_SteamSessionSent", false))
                return;

            var steamAppIdPath = Path.Combine(Application.dataPath, "..", "steam_appid.txt");

            if (!File.Exists(steamAppIdPath))
                return;

            var content = File.ReadAllText(steamAppIdPath).Trim();

            if (string.IsNullOrEmpty(content))
                return;

            SessionState.SetBool("PurrNet_Telemetry_SteamSessionSent", true);

            var metadata = new Dictionary<string, object>
            {
                ["purrnet_version"] = NetworkManager.version,
                ["steam_app_id"] = content
            };

            PurrTelemetry.SendEvent("steam_session", metadata);
        }

        [MenuItem("Tools/PurrNet/Disable Telemetry", priority = 200)]
        static void DisableTelemetry()
        {
            EditorPrefs.SetBool(OPT_OUT_KEY, true);
        }

        [MenuItem("Tools/PurrNet/Disable Telemetry", true)]
        static bool ValidateDisableTelemetry()
        {
            return !EditorPrefs.GetBool(OPT_OUT_KEY, false);
        }

        [MenuItem("Tools/PurrNet/Enable Telemetry", priority = 200)]
        static void EnableTelemetry()
        {
            EditorPrefs.SetBool(OPT_OUT_KEY, false);
        }

        [MenuItem("Tools/PurrNet/Enable Telemetry", true)]
        static bool ValidateEnableTelemetry()
        {
            return EditorPrefs.GetBool(OPT_OUT_KEY, false);
        }
    }
}
