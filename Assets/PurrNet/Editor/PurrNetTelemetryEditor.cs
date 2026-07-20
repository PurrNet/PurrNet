using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PurrNet.Utils;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrNetTelemetryEditor
    {
        [InitializeOnLoadMethod]
        static void Init()
        {
            if (!PurrInternalTelemetry.IsEnabled)
                return;

            InitProjectId();

            if (ApplicationContext.isClone)
                return;

            ShowFirstRunNotice();
            SendProjectStart();
            SendSteamSession();
        }

        static void InitProjectId()
        {
            try
            {
                var path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");

                if (!File.Exists(path))
                    return;

                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.TrimStart();

                    if (!trimmed.StartsWith("productGUID:"))
                        continue;

                    var guid = trimmed.Substring("productGUID:".Length).Trim();

                    if (string.IsNullOrEmpty(guid))
                        break;

                    using var sha = SHA256.Create();
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(guid));

                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash)
                        sb.Append(b.ToString("x2"));

                    PurrInternalTelemetry.ProjectId = sb.ToString();
                    break;
                }
            }
            catch
            {
                // Not critical
            }
        }

        static void ShowFirstRunNotice()
        {
            var key = "PurrNet_Telemetry_Notice_" + Application.dataPath.GetHashCode();

            if (EditorPrefs.GetBool(key, false))
                return;

            EditorPrefs.SetBool(key, true);

            Debug.Log(
                "[PurrNet] Anonymous telemetry is enabled to help improve PurrNet. " +
                "No personal data is collected. See TELEMETRY.md for details.");
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

            PurrInternalTelemetry.SendEvent("project_start", metadata);
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

            PurrInternalTelemetry.SendEvent("steam_session", metadata);
        }

#if PURRNET_NO_TELEMETRY
        [MenuItem("Tools/PurrNet/Misc/Enable Telemetry", false, 200)]
        static void EnableTelemetry()
        {
            SymbolsHelper.RemoveSymbol("PURRNET_NO_TELEMETRY");
        }
#else
        [MenuItem("Tools/PurrNet/Misc/Disable Telemetry", false, 200)]
        static void DisableTelemetry()
        {
            SymbolsHelper.AddSymbol("PURRNET_NO_TELEMETRY");
        }
#endif
    }
}
