using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;
using Unity.CompilationPipeline.Common.Diagnostics;

#if UNITY_MONO_CECIL
namespace PurrNet.Codegen
{
    public static class OptimizePackers
    {
        public static void HandleType(bool isWritting, MethodReference method, List<DiagnosticMessage> messages)
        {
            var methodDefinition = method.Resolve();

            if (methodDefinition == null)
                return;

            var il = methodDefinition.Body.GetILProcessor();
            // We need to look for any Packer<T> or DeltaPacker<T> and replace them with the inline version when possible

            int count = methodDefinition.Body.Instructions.Count;
            for (var i = 0; i < count; i++)
            {
                var instruction = methodDefinition.Body.Instructions[i];

                if (instruction.OpCode != OpCodes.Call)
                    continue;

                if (instruction.Operand is not MethodReference methodReference)
                    continue;

                var baseReference = methodReference.DeclaringType;
                var baseDefinition = methodReference.DeclaringType.Resolve();

                if (baseDefinition == null)
                    continue;

                bool isPacker = baseDefinition.FullName == typeof(Packer<>).FullName;
                bool isDeltaPacker = baseDefinition.FullName == typeof(DeltaPacker<>).FullName;

                if (!isPacker && !isDeltaPacker)
                    continue;

                if (baseReference is not GenericInstanceType genericArgument || genericArgument.GenericArguments.Count != 1)
                    continue;

                var serializedType = genericArgument.GenericArguments[0];
                var module = methodDefinition.Module;
                if (isPacker)
                {
                    if (GenerateSerializersProcessor.TryGetInlinedMethod(isWritting, serializedType, module,
                            out var inlineMethod))
                    {
                        il.Replace(instruction, Instruction.Create(OpCodes.Call, inlineMethod));
                    }
                }
                else
                {
                    if (GenerateDeltaSerializersProcessor.TryGetInlinedMethod(isWritting, serializedType, module,
                            out var inlineMethod))
                    {
                        il.Replace(instruction, Instruction.Create(OpCodes.Call, inlineMethod));
                    }
                }
            }
        }
    }
}
#endif
