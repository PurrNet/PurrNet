using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PurrNet.Editor
{
    public class PurrNetTelemetryBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string OUTPUT_PATH = "Assets/Resources/PurrTelemetryProjectId.txt";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var projectId = ComputeProjectId();

            if (string.IsNullOrEmpty(projectId))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(OUTPUT_PATH) ?? string.Empty);
            File.WriteAllText(OUTPUT_PATH, projectId);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (File.Exists(OUTPUT_PATH))
                File.Delete(OUTPUT_PATH);

            var meta = OUTPUT_PATH + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
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
    }
}
