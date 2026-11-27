#if PURR_ESSENTIALS_STANDALONE
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PurrNet.Essentials
{
    internal static class PurrEssentialsUpdater
    {
        private const string MenuPath = "Tools/PurrNet/Update Essentials";
        private const string PendingKey = "PurrNet_Essentials_UpdateFrom";
        private const string DialogTitle = "PurrNet Essentials";

        private static AddRequest _request;

        [MenuItem(MenuPath, true)]
        private static bool CanUpdate()
        {
            return _request == null;
        }

        [MenuItem(MenuPath, false, -99)]
        private static void Update()
        {
            var info = PackageInfo.FindForAssembly(typeof(PurrEssentialsUpdater).Assembly);

            if (info == null || info.source != PackageSource.Git || info.git == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "Essentials is not installed from a git URL, so it cannot be updated from here.", "OK");
                return;
            }

            string revision = info.git.revision;

            if (IsPinned(revision))
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    $"Essentials is pinned to '{revision}'. Point it at #dev or #release in Packages/manifest.json to receive updates.",
                    "OK");
                return;
            }

            int separator = info.packageId.IndexOf('@');
            if (separator < 0)
            {
                Debug.LogError($"[PurrNet] Could not read the Essentials git URL from '{info.packageId}'.");
                return;
            }

            SessionState.SetString(PendingKey, info.git.hash ?? "");
            _request = Client.Add(info.packageId.Substring(separator + 1));
            EditorApplication.update += Poll;
        }

        private static bool IsPinned(string revision)
        {
            if (string.IsNullOrEmpty(revision))
                return false;

            return Regex.IsMatch(revision, @"\A[0-9a-fA-F]{40}\z") || Regex.IsMatch(revision, @"\Av?\d+\.\d+");
        }

        private static void Poll()
        {
            if (_request == null)
            {
                EditorApplication.update -= Poll;
                return;
            }

            if (!_request.IsCompleted)
                return;

            var request = _request;
            _request = null;
            EditorApplication.update -= Poll;

            if (request.Status != StatusCode.Success)
            {
                SessionState.EraseString(PendingKey);
                Debug.LogError($"[PurrNet] Updating Essentials failed: {request.Error?.message}");
                return;
            }

            Report(request.Result);
        }

        [InitializeOnLoadMethod]
        private static void ReportAfterReload()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                return;

            Report(PackageInfo.FindForAssembly(typeof(PurrEssentialsUpdater).Assembly));
        }

        private static void Report(PackageInfo info)
        {
            string previous = SessionState.GetString(PendingKey, "");
            SessionState.EraseString(PendingKey);

            string current = info?.git?.hash ?? "";

            if (string.IsNullOrEmpty(current) || current == previous)
            {
                Debug.Log($"[PurrNet] Essentials is already up to date ({info?.version}).");
                return;
            }

            Debug.Log($"[PurrNet] Essentials updated to {info.version} ({Short(previous)} -> {Short(current)}).");
        }

        private static string Short(string hash)
        {
            return string.IsNullOrEmpty(hash) || hash.Length <= 8 ? hash : hash.Substring(0, 8);
        }
    }
}
#endif
