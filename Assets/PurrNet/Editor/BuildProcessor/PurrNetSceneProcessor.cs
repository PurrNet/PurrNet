using System.IO;
using System.Security.Cryptography;
using System.Text;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Editor
{
    public class PurrNetSceneProcessor : IProcessSceneWithReport, IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

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

        public void OnPreprocessBuild(BuildReport report)
        {
            ApplicationConstants.Set("projectId", ComputeProjectId());
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
