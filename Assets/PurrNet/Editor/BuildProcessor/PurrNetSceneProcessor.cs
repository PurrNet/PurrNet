using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using PurrNet.Pooling;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Editor
{
    public class PurrNetSceneProcessor : IProcessSceneWithReport, IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            Cleanup();
        }

        private static void Cleanup()
        {
            const string VERSION = "Assets/Resources/PurrVersion.json";
            const string PROJECT_ID = "Assets/Resources/PurrTelemetryProjectId.txt";

            if (File.Exists(VERSION))
                File.Delete(VERSION);

            if (File.Exists(VERSION + ".meta"))
                File.Delete(VERSION + ".meta");

            if (File.Exists(PROJECT_ID))
                File.Delete(PROJECT_ID);

            if (File.Exists(PROJECT_ID + ".meta"))
                File.Delete(PROJECT_ID + ".meta");

            if (Directory.Exists("Assets/Resources"))
            {
                var files = Directory.GetFiles("Assets/Resources");
                var dirs = Directory.GetDirectories("Assets/Resources");

                bool isResourcesFolderEmpty = files.Length == 0 &&
                                              dirs.Length == 0;

                if (isResourcesFolderEmpty)
                {
                    Directory.Delete("Assets/Resources");
                    if (File.Exists("Assets/Resources.meta"))
                        File.Delete("Assets/Resources.meta");
                }
            }

            AssetDatabase.Refresh();
        }

        static string TryFindVersion()
        {
            var packagePath = AssetDatabase.GUIDToAssetPath("0ec978dbed50a6f4b9a57580867f1fae");

            if (string.IsNullOrEmpty(packagePath))
                return "v?";

            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(packagePath);

            if (textAsset == null)
                return "v?";

            var json = JObject.Parse(textAsset.text);
            return 'v' + (json["version"]?.ToString() ?? "?");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            const string VERSION = "Assets/Resources/PurrVersion.json";
            const string PROJECT_ID = "Assets/Resources/PurrTelemetryProjectId.txt";

            Directory.CreateDirectory(Path.GetDirectoryName(VERSION) ?? string.Empty);
            File.WriteAllText(VERSION, TryFindVersion());

            var projectId = ComputeProjectId();
            if (!string.IsNullOrEmpty(projectId))
                File.WriteAllText(PROJECT_ID, projectId);

            AssetDatabase.Refresh();
        }

        static string ComputeProjectId()
        {
            try
            {
                var path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");

                if (!File.Exists(path))
                    return null;

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

                    return sb.ToString();
                }
            }
            catch
            {
                // Not critical
            }

            return null;
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var rootObjects = scene.GetRootGameObjects();
            var obj = new GameObject("PurrNetSceneHelper");

            if (report == null)
                obj.hideFlags = HideFlags.HideInHierarchy;

            var sceneInfo = obj.AddComponent<PurrSceneInfo>();
            sceneInfo.rootGameObjects = new System.Collections.Generic.List<GameObject>();

            var total = ListPool<NetworkIdentity>.Instantiate();
            var local = ListPool<NetworkIdentity>.Instantiate();

            for (uint i = 0; i < rootObjects.Length; i++)
            {
                sceneInfo.rootGameObjects.Add(rootObjects[i]);
                rootObjects[i].GetComponentsInChildren(true, local);
                total.AddRange(local);
                local.Clear();
            }

            foreach (var nid in total)
            {
                if (!nid) continue;
                nid.ResetIsSetup();
            }

            ListPool<NetworkIdentity>.Destroy(total);
            ListPool<NetworkIdentity>.Destroy(local);
        }
    }
}
