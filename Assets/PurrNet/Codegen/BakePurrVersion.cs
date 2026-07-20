#if UNITY_MONO_CECIL
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace PurrNet.Codegen
{
    public static class BakePurrVersion
    {
        const string PACKAGE_JSON_GUID = "0ec978dbed50a6f4b9a57580867f1fae";

        static readonly string[] SEARCH_ROOTS = { "./Assets", "./Packages" };

        static string TryFindVersion()
        {
            foreach (var root in SEARCH_ROOTS)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var metaFile in Directory.EnumerateFiles(root, "package.json.meta",
                             SearchOption.AllDirectories))
                {
                    string metaContent;

                    try
                    {
                        metaContent = File.ReadAllText(metaFile);
                    }
                    catch
                    {
                        continue;
                    }

                    if (metaContent.IndexOf(PACKAGE_JSON_GUID, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var packageJsonPath = metaFile[..^".meta".Length];

                    if (!File.Exists(packageJsonPath))
                        continue;

                    try
                    {
                        var json = JObject.Parse(File.ReadAllText(packageJsonPath));
                        return 'v' + (json["version"]?.ToString() ?? "?");
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        public static void Process(TypeDefinition type, bool isEditor, List<DiagnosticMessage> messages)
        {
            if (isEditor)
                return;

            var init = type.GetMethod("Init");
            var setMethod = type.GetMethod("SetBakedVersion");

            if (init == null || setMethod == null)
                return;

            var version = TryFindVersion();

            if (string.IsNullOrEmpty(version))
                return;

            var setRef = setMethod.Import(type.Module);
            var ret = init.Body.Instructions[^1];
            var processor = init.Body.GetILProcessor();

            processor.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, version));
            processor.InsertBefore(ret, Instruction.Create(OpCodes.Call, setRef));
        }
    }
}
#endif
