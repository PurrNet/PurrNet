#if UNITY_EDITOR && UNITY_MONO_CECIL
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Codegen
{
    public static class Il2CppAssemblyDiagnostics
    {
        [MenuItem("Tools/PurrNet/Features/Debug/Diagnose IL2CPP Assembly Load Failure (post failed IL Build)")]
        public static void Diagnose()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var candidates = new[]
            {
                Path.Combine(projectRoot, "Library", "Bee", "artifacts", "WebGL", "ManagedStripped", "PurrNet.Runtime.dll"),
                Path.Combine(projectRoot, "Library", "Bee", "artifacts", "WebGL", "Managed", "PurrNet.Runtime.dll"),
                Path.Combine(projectRoot, "Library", "ScriptAssemblies", "PurrNet.Runtime.dll")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                {
                    Debug.Log($"[IL2CPP Diag] Skip (missing): {path}");
                    continue;
                }

                Debug.Log($"[IL2CPP Diag] Checking: {path}");
                try
                {
                    using var ad = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
                    foreach (var type in ad.MainModule.GetTypes())
                    {
                        InspectType(type, path);
                    }
                    Debug.Log($"[IL2CPP Diag] OK: {path}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[IL2CPP Diag] Load failed for {path}: {ex.Message}");
                }
            }
        }

        static void InspectType(TypeDefinition type, string assemblyPath)
        {
            try
            {
                _ = type.Methods.Count;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Debug.LogError($"[IL2CPP Diag] Failing type: {type.FullName} | Assembly: {assemblyPath}\n{ex}");
                throw;
            }

            foreach (var nested in type.NestedTypes)
                InspectType(nested, assemblyPath);
        }
    }
}
#endif
