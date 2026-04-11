using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PurrNet
{
    public static class PurrTelemetry
    {
        const string ENDPOINT = "https://purrnet.dev/api/telemetry/event";
        const string INSTALL_ID_KEY = "PurrNet_InstallId";
        const string OPT_OUT_KEY = "PurrNet_Telemetry_OptOut";

        static string _installationId;

        public static bool IsEnabled
        {
            get
            {
                if (Application.isBatchMode)
                    return false;

                if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                        StringComparison.OrdinalIgnoreCase))
                    return false;

#if UNITY_EDITOR
                if (EditorPrefs.GetBool(OPT_OUT_KEY, false))
                    return false;
#endif
                return true;
            }
        }

        static string GetInstallationId()
        {
            if (!string.IsNullOrEmpty(_installationId))
                return _installationId;

            _installationId = PlayerPrefs.GetString(INSTALL_ID_KEY, "");

            if (string.IsNullOrEmpty(_installationId))
            {
                _installationId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(INSTALL_ID_KEY, _installationId);
                PlayerPrefs.Save();
            }

            return _installationId;
        }

        public static async void SendEvent(string eventType, Dictionary<string, object> metadata)
        {
            if (!IsEnabled)
                return;

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["installation_id"] = GetInstallationId(),
                    ["event_type"] = eventType,
                    ["metadata"] = metadata
                };

                var json = JsonConvert.SerializeObject(payload);
                var bodyRaw = Encoding.UTF8.GetBytes(json);

                var request = new UnityWebRequest(ENDPOINT, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var tcs = new TaskCompletionSource<bool>();
                var op = request.SendWebRequest();
                op.completed += _ => tcs.TrySetResult(true);
                await tcs.Task;

                request.Dispose();
            }
            catch
            {
                // Silent failure, we don't want to disrupt Unity because of telemetry :-)
            }
        }

        public static void SendConnectionEvent(NetworkManager manager)
        {
            if (!IsEnabled)
                return;

            var metadata = new Dictionary<string, object>
            {
                ["purrnet_version"] = NetworkManager.version,
                ["player_count"] = manager.playerCount,
                ["transport"] = manager.transport ? manager.transport.GetType().Name : "Unknown"
            };

            SendEvent("connection", metadata);
        }
    }
}
