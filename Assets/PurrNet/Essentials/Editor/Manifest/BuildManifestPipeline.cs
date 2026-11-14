using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Essentials
{
    public sealed class BuildManifestResult
    {
        public string buildId;
        public string contentHash;
        public string path;
        public BuildManifest manifest;
    }

    public static class BuildManifestPipeline
    {
        public const string OutputDirectory = "Library/PurrNet/Manifests";

        private const string ProjectKeyConstant = "PurrServices.build.apiKey";
        private const string ForceVariable = "PURRNET_BUILD_MANIFEST";
        private const string MenuPath = "Tools/PurrNet/Capture/Preview Build Manifest";

        private static BuildManifest _current;
        private static IBuildManifestExtractor _extractor;
        private static string _buildId;
        private static bool _markerPlaced;

        public static event Action<BuildManifestResult> onBuilt;

        public static bool isProjectLinked =>
            (ApplicationConstants.TryGet(ProjectKeyConstant, out var key) && !string.IsNullOrWhiteSpace(key)) ||
            Environment.GetEnvironmentVariable(ForceVariable) == "1";

        public static IBuildManifestExtractor FindExtractor()
        {
            var types = TypeCache.GetTypesDerivedFrom<IBuildManifestExtractor>();
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i].IsAbstract || types[i].IsInterface)
                    continue;

                try
                {
                    return (IBuildManifestExtractor)Activator.CreateInstance(types[i]);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PurrNet] Could not create manifest extractor {types[i].Name}: {e.Message}");
                }
            }

            return null;
        }

        public static string Begin(bool developmentBuild)
        {
            _current = null;
            _extractor = null;
            _buildId = null;
            _markerPlaced = false;

            if (developmentBuild)
                return null;

            var extractor = FindExtractor();
            if (extractor == null)
                return null;

            var manifest = new BuildManifest
            {
                netcode = extractor.netcode,
                netcodeVersion = extractor.netcodeVersion
            };
            extractor.Extract(manifest);

            _extractor = extractor;
            _current = manifest;
            _buildId = Guid.NewGuid().ToString("N");
            return _buildId;
        }

        public static void AddScene(Scene scene)
        {
            if (_current == null || !scene.IsValid())
                return;

            if (!_markerPlaced)
            {
                _markerPlaced = true;
                var marker = new GameObject("PurrBuildInfo") { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(marker, scene);
                marker.AddComponent<PurrBuildMarker>().bakedBuildId = _buildId;
            }

            _current.AddScene(scene.buildIndex, scene.name, scene.GetRootGameObjects(), _extractor.IsNetworkObject);
        }

        public static BuildManifestResult Finish()
        {
            var manifest = _current;
            string buildId = _buildId;
            _current = null;
            _extractor = null;
            _buildId = null;
            _markerPlaced = false;

            if (manifest == null)
                return null;

            var result = Save(manifest, buildId);
            onBuilt?.Invoke(result);
            return result;
        }

        private static BuildManifestResult Save(BuildManifest manifest, string name)
        {
            Directory.CreateDirectory(OutputDirectory);
            string json = manifest.ToJson();
            string path = Path.Combine(OutputDirectory, name + ".json");
            File.WriteAllText(path, json, new UTF8Encoding(false));

            if (manifest.meshData.Count > 0)
            {
                string meshes = Path.Combine(OutputDirectory, "meshes");
                Directory.CreateDirectory(meshes);
                foreach (var mesh in manifest.meshData)
                {
                    string meshPath = Path.Combine(meshes, mesh.Key + ".bin");
                    if (!File.Exists(meshPath))
                        File.WriteAllBytes(meshPath, mesh.Value);
                }
            }

            return new BuildManifestResult
            {
                buildId = name,
                contentHash = BuildManifest.Hash(Encoding.UTF8.GetBytes(json)),
                path = path,
                manifest = manifest
            };
        }

        [MenuItem(MenuPath)]
        private static void Preview()
        {
            var extractor = FindExtractor();
            if (extractor == null)
            {
                Debug.LogWarning("[PurrNet] No netcode in this project knows how to describe itself for capture.");
                return;
            }

            var manifest = new BuildManifest
            {
                netcode = extractor.netcode,
                netcodeVersion = extractor.netcodeVersion
            };
            extractor.Extract(manifest);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    manifest.AddScene(scene.buildIndex, scene.name, scene.GetRootGameObjects(), extractor.IsNetworkObject);
            }

            var result = Save(manifest, "preview");
            Debug.Log($"[PurrNet] Build manifest preview for {manifest.netcode} {manifest.netcodeVersion}: " +
                      $"{manifest.prefabCount} prefabs, {manifest.sceneCount} open scenes, {manifest.meshData.Count} meshes, " +
                      $"{new FileInfo(result.path).Length / 1024} KB at {Path.GetFullPath(result.path)}");
        }
    }

    internal sealed class BuildManifestBuildStep : IPreprocessBuildWithReport, IProcessSceneWithReport,
        IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!BuildManifestPipeline.isProjectLinked)
                return;

            try
            {
                BuildManifestPipeline.Begin((report.summary.options & BuildOptions.Development) != 0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PurrNet] The build manifest could not be started; this build will not be captured. {e.Message}");
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
                return;

            try
            {
                BuildManifestPipeline.AddScene(scene);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PurrNet] Scene {scene.name} could not be added to the build manifest. {e.Message}");
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                var result = BuildManifestPipeline.Finish();
                if (result != null)
                    Debug.Log($"[PurrNet] Build manifest {result.buildId} written to {result.path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PurrNet] The build manifest could not be written. {e.Message}");
            }
        }
    }
}
