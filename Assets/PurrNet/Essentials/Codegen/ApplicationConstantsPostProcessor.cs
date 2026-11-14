#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Utils;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace PurrNet.Essentials.Codegen
{
    public class ApplicationConstantsPostProcessor : ILPostProcessor
    {
        const string ASSEMBLY_NAME = "PurrNet.Essentials.Runtime";
        const string TYPE_NAME = "PurrNet.Essentials.ApplicationConstants";
        const string SAVE_PATH = "./ProjectSettings/ApplicationConstants.json";

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return compiledAssembly.Name == ASSEMBLY_NAME;
        }

        static Dictionary<string, string> ReadConstants()
        {
            var constants = new Dictionary<string, string>();

            if (!File.Exists(SAVE_PATH))
                return constants;

            PurrJson.ReadStringMap(File.ReadAllText(SAVE_PATH), constants);
            return constants;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
                return null!;

            if (Array.IndexOf(compiledAssembly.Defines, "UNITY_EDITOR") >= 0)
                return null!;

            var messages = new List<DiagnosticMessage>();

            try
            {
                var constants = ReadConstants();

                if (constants.Count == 0)
                    return null!;

                var pdbData = compiledAssembly.InMemoryAssembly.PdbData;
                bool hasSymbols = pdbData != null && pdbData.Length > 0;

                using var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
                using var pdbStream = hasSymbols ? new MemoryStream(pdbData) : null;

                var readerParameters = new ReaderParameters { ReadSymbols = hasSymbols };
                if (hasSymbols)
                {
                    readerParameters.SymbolStream = pdbStream;
                    readerParameters.SymbolReaderProvider = new PortablePdbReaderProvider();
                }

                using var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, readerParameters);
                var type = assemblyDefinition.MainModule.GetType(TYPE_NAME);
                var init = FindMethod(type, "Init");
                var setMethod = FindMethod(type, "Set");

                if (init == null || setMethod == null)
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = $"Failed to bake application constants: {TYPE_NAME} is missing Init or Set.",
                    });
                    return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
                }

                var keys = new List<string>(constants.Keys);
                keys.Sort(StringComparer.Ordinal);

                var ret = init.Body.Instructions[^1];
                var processor = init.Body.GetILProcessor();

                for (var i = 0; i < keys.Count; i++)
                {
                    processor.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, keys[i]));
                    processor.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, constants[keys[i]]));
                    processor.InsertBefore(ret, Instruction.Create(OpCodes.Call, setMethod));
                }

                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                var writerParameters = new WriterParameters { WriteSymbols = hasSymbols };
                if (hasSymbols)
                {
                    writerParameters.SymbolStream = pdb;
                    writerParameters.SymbolWriterProvider = new PortablePdbWriterProvider();
                }

                assemblyDefinition.Write(pe, writerParameters);
                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), messages);
            }
            catch (Exception e)
            {
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = $"Failed to bake application constants: {e.Message}\n{e.StackTrace}",
                });
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
            }
        }

        static MethodDefinition FindMethod(TypeDefinition type, string name)
        {
            if (type == null)
                return null;

            for (var i = 0; i < type.Methods.Count; i++)
            {
                if (type.Methods[i].Name == name)
                    return type.Methods[i];
            }

            return null;
        }
    }
}
#endif
