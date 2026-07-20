#if UNITY_MONO_CECIL
using Mono.Cecil;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace PurrNet.Codegen
{
    public static class BakeApplicationConstants
    {
        const string SAVE_PATH = "./ProjectSettings/ApplicationConstants.json";

        static Dictionary<string, string> ReadConstants()
        {
            var constants = new Dictionary<string, string>();

            if (!File.Exists(SAVE_PATH))
                return constants;

            string jsonString = File.ReadAllText(SAVE_PATH);
            var json = JObject.Parse(jsonString);

            foreach (var (key, val) in json)
            {
                if (val == null)
                    continue;
                constants[key] = val.ToString();
            }

            return constants;
        }

        public static void Process(TypeDefinition type, bool isEditor, List<DiagnosticMessage> messages)
        {
            if (isEditor)
                return;

            var setMethod = type.GetMethod("Set").Import(type.Module);
            var init = type.GetMethod("Init");
            var constants = ReadConstants();

            if (constants.Count == 0)
                return;

            var ret = init.Body.Instructions[^1];
            var processor = init.Body.GetILProcessor();

            foreach (var (key, val) in constants)
            {
                processor.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, key));
                processor.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, val));
                processor.InsertBefore(ret, Instruction.Create(OpCodes.Call, setMethod));
            }
        }
    }
}
#endif
