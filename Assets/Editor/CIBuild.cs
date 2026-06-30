using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// game-ci custom build entry point. Invoked via `buildMethod: CIBuild.BuildLinuxPlayer`.
// Builds the StandaloneLinux64 IL2CPP player; produces a Development build (live ProfilerMarkers,
// so the benchmark's CPU breakdown works) when PURR_DEV_BUILD=1, otherwise a release build.
public static class CIBuild
{
    private const string OutputPath = "build/StandaloneLinux64/PurrNetTests";

    public static void BuildLinuxPlayer()
    {
        // Read from the Unity command line (passed via game-ci customParameters) rather than an env
        // var — game-ci runs the build in a container that only forwards an allowlisted set of env
        // vars, so a custom env var never reaches this process.
        bool dev = Array.IndexOf(Environment.GetCommandLineArgs(), "-purrDevBuild") >= 0;

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);

        var outputPath = GetCommandLineValue("-purrBuildOutput") ?? OutputPath;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            options = dev ? BuildOptions.Development : BuildOptions.None
        };

        Debug.Log($"[CIBuild] Building StandaloneLinux64 IL2CPP (development={dev}) -> {outputPath}");
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"[CIBuild] Result={summary.result} size={summary.totalSize} errors={summary.totalErrors}");

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static string GetCommandLineValue(string key)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
                return args[i + 1];
        }

        return null;
    }
}
