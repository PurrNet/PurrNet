#if UNITY_MONO_CECIL
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine.Scripting;

namespace PurrNet.Codegen
{
    public static class GenerateRPCManifestProcessor
    {
        public static void EmitRegistration(ModuleDefinition module, TypeDefinition type,
            DisposableList<RPCMethod> rpcs, int idOffset)
        {
            if (rpcs.Count == 0)
                return;

            var manifestClass = CreateManifestClass(module, type);

            var registerMethod = new MethodDefinition("Register",
                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig,
                module.TypeSystem.Void);

            AddRegisterPackersAttribute(module, registerMethod);

            registerMethod.Body = new MethodBody(registerMethod) { InitLocals = false };

            var manifestType = module.GetTypeDefinition(typeof(Manifest)).Import(module);
            var registerRpcMethod = manifestType.GetMethod("RegisterRPC", false).Import(module);

            var signatureType = module.GetTypeDefinition<RPCSignature>();
            var signatureMake = signatureType.GetMethod("Make").Import(module);

            var getTypeFromHandle = module.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"));

            var il = registerMethod.Body.GetILProcessor();

            for (int i = 0; i < rpcs.Count; i++)
            {
                var rpc = rpcs[i];
                int rpcId = idOffset + i;

                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, getTypeFromHandle);

                il.Emit(OpCodes.Ldc_I4, rpcId);

                PostProcessor.PushRPCSignatureMakeArgs(il, rpc);
                il.Emit(OpCodes.Call, signatureMake);

                il.Emit(OpCodes.Call, registerRpcMethod);
            }

            il.Emit(OpCodes.Ret);

            manifestClass.Methods.Add(registerMethod);
            module.Types.Add(manifestClass);
        }

        static TypeDefinition CreateManifestClass(ModuleDefinition module, TypeDefinition type)
        {
            string namespaceName = string.IsNullOrWhiteSpace(type.Namespace)
                ? "PurrNet.CodeGen.Manifest"
                : type.Namespace + ".PurrNet.CodeGen.Manifest";

            string className =
                $"{GenerateSerializersProcessor.MakeFullNameValidCSharp(type.FullName)}_RPCManifest";

            var manifestClass = new TypeDefinition(namespaceName, className,
                TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract |
                TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);

            var generatedByIL = module.GetTypeDefinition<GeneratedByILAttribute>().Import(module);
            var generatedByILCtor = generatedByIL.Resolve().Methods
                .First(m => m.IsConstructor && !m.HasParameters).Import(module);
            manifestClass.CustomAttributes.Add(new CustomAttribute(generatedByILCtor));

            var preserveType = module.GetTypeDefinition<PreserveAttribute>().Import(module);
            var preserveCtor = preserveType.Resolve().Methods
                .First(m => m.IsConstructor && !m.HasParameters).Import(module);
            manifestClass.CustomAttributes.Add(new CustomAttribute(preserveCtor));

            return manifestClass;
        }

        static void AddRegisterPackersAttribute(ModuleDefinition module, MethodDefinition method)
        {
            var attrType = module.GetTypeDefinition<RegisterPackersAttribute>().Import(module);
            var attrCtor = attrType.Resolve().Methods
                .First(m => m.IsConstructor && m.HasParameters).Import(module);
            var attribute = new CustomAttribute(attrCtor);
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(module.TypeSystem.Int32, -1));
            method.CustomAttributes.Add(attribute);
        }
    }
}
#endif
