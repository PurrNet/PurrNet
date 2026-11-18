#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace PurrNet.Codegen
{
    public static class UnityProxyProcessor
    {
        const string AddressablesClassName = "UnityEngine.AddressableAssets.Addressables";
        const string AssetReferenceClassName = "UnityEngine.AddressableAssets.AssetReference";
        const string AddressablesProxyFullName = "PurrNet.AddressablesProxy";
        const string EffectsProxyFullName = "PurrNet.Recording.RecordedEffects";

        /// <summary>
        /// The engine calls that start or stop a sound, a particle system or an animation. The game's calls of them
        /// are pointed at the method of the same name in RecordedEffects, which makes the call and tells a running
        /// recording about it.
        /// </summary>
        static bool IsEffectCall(string declaringType, string method)
        {
            switch (declaringType)
            {
                case "UnityEngine.AudioSource":
                    return method == "Play" || method == "PlayOneShot" || method == "PlayClipAtPoint" || method == "Stop";
                case "UnityEngine.ParticleSystem":
                    return method == "Play" || method == "Stop" || method == "Emit";
                case "UnityEngine.Animator":
                    return method == "SetTrigger" || method == "Play" || method == "PlayInFixedTime" ||
                           method == "CrossFade" || method == "CrossFadeInFixedTime" ||
                           method == "SetBool" || method == "SetFloat" || method == "SetInteger";
                default:
                    return false;
            }
        }

        /// <summary>
        /// The engine calls that change how an object shows or what it collides with. They go through
        /// RecordedEffects too, in every assembly but PurrNet's own runtime, also in the ones whose calls are not
        /// recorded: a recording does not write these down, it looks at the object again when one is made.
        /// </summary>
        static bool IsWatchedCall(string declaringType, string method)
        {
            switch (declaringType)
            {
                case "UnityEngine.GameObject":
                    return method == "SetActive";
                case "UnityEngine.Renderer":
                case "UnityEngine.Collider":
                    return method == "set_enabled";
                default:
                    return false;
            }
        }

        public static void Process(TypeDefinition type, [UsedImplicitly] List<DiagnosticMessage> messages, bool effects = false,
            bool watched = false)
        {
            try
            {
                bool isProxyItself = type.FullName == typeof(UnityProxy).FullName
                    || type.FullName == AddressablesProxyFullName
                    || type.FullName == EffectsProxyFullName;

                if (isProxyItself)
                    return;

                var module = type.Module;

                string objectClassFullName = typeof(UnityEngine.Object).FullName;

                TypeDefinition addressablesProxyType = null;
                bool addressablesProxyResolved = false;
                TypeDefinition effectsProxyType = null;
                bool effectsProxyResolved = false;

                foreach (var method in type.Methods)
                {
                    if (method.Body == null) continue;

                    var processor = method.Body.GetILProcessor();

                    for (var i = 0; i < method.Body.Instructions.Count; i++)
                    {
                        var instruction = method.Body.Instructions[i];

                        if (instruction.Operand is not MethodReference methodReference)
                            continue;

                        if (methodReference.DeclaringType == null)
                            continue;

                        var declaringTypeName = methodReference.DeclaringType.FullName;

                        if (declaringTypeName == objectClassFullName)
                        {
                            if (methodReference.Name != "Instantiate" &&
                                methodReference.Name != "InstantiateAsync" &&
                                methodReference.Name != "Destroy" &&
                                methodReference.Name != "DontDestroyOnLoad")
                                continue;

                            var resolved = methodReference.Resolve();

                            if (resolved == null)
                                continue;

                            var unityProxyType = module.GetTypeReference(typeof(UnityProxy)).Import(module).Resolve();

                            if (unityProxyType == null)
                                continue;

                            var targetMethod = GetMatchingDefinition(resolved, unityProxyType, false);

                            if (targetMethod == null)
                                continue;

                            var targerRef = module.ImportReference(targetMethod);

                            if (methodReference is GenericInstanceMethod genericInstanceMethod)
                            {
                                var genRef = new GenericInstanceMethod(targerRef);

                                for (var j = 0; j < genericInstanceMethod.GenericArguments.Count; j++)
                                    genRef.GenericArguments.Add(genericInstanceMethod.GenericArguments[j]);

                                for (var j = 0; j < genRef.GenericParameters.Count; j++)
                                    genRef.GenericParameters.Add(genRef.GenericParameters[j]);

                                targerRef = module.ImportReference(genRef);
                            }

                            // Rewrite in place. Replace() removes the instruction, and any branch that targets this
                            // call (e.g. `Destroy(c ? a : b)`) would keep pointing at the removed one and be written
                            // with a stale offset; Unity 6.6's ILPP runner re-reads the assembly and rejects that.
                            instruction.Operand = targerRef;
                            continue;
                        }

                        if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                            ((effects && IsEffectCall(declaringTypeName, methodReference.Name)) ||
                             (watched && IsWatchedCall(declaringTypeName, methodReference.Name))))
                        {
                            if (!effectsProxyResolved)
                            {
                                effectsProxyResolved = true;
                                effectsProxyType = ResolveTypeByName(module, EffectsProxyFullName);
                            }

                            if (effectsProxyType == null)
                                continue;

                            var effectResolved = methodReference.Resolve();

                            if (effectResolved == null)
                                continue;

                            var effectTarget = GetMatchingDefinition(effectResolved, effectsProxyType, !effectResolved.IsStatic);

                            if (effectTarget == null)
                                continue;

                            instruction.OpCode = OpCodes.Call;
                            instruction.Operand = module.ImportReference(effectTarget);
                            continue;
                        }

                        if (methodReference.Name != "InstantiateAsync" &&
                            methodReference.Name != "ReleaseInstance")
                            continue;

                        bool isAddressablesStatic = declaringTypeName == AddressablesClassName;
                        bool isAssetRefInstance = !isAddressablesStatic &&
                            IsOrDerivedFrom(methodReference.DeclaringType, AssetReferenceClassName);

                        if (!isAddressablesStatic && !isAssetRefInstance)
                            continue;

                        // Lazy-resolve the AddressablesProxy type (once per type being processed)
                        if (!addressablesProxyResolved)
                        {
                            addressablesProxyResolved = true;
                            addressablesProxyType = ResolveTypeByName(module, AddressablesProxyFullName);
                        }

                        if (addressablesProxyType == null)
                            continue;

                        var addrResolved = methodReference.Resolve();

                        if (addrResolved == null)
                            continue;

                        var addrTargetMethod = GetMatchingDefinition(
                            addrResolved, addressablesProxyType, isAssetRefInstance);

                        if (addrTargetMethod == null)
                            continue;

                        var addrTargetRef = module.ImportReference(addrTargetMethod);

                        // in place, see above
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = addrTargetRef;
                    }
                }
            }
            catch (Exception e)
            {
                messages.Add(new DiagnosticMessage
                {
                    MessageData = $"Failed to process UnityProxy: {e.Message} {e.StackTrace}",
                    DiagnosticType = DiagnosticType.Error
                });
            }
        }

        static MethodDefinition GetMatchingDefinition(
            MethodReference originalMethod,
            TypeDefinition proxyType,
            bool isInstanceToStatic)
        {
            int paramOffset = isInstanceToStatic ? 1 : 0;
            int expectedParamCount = originalMethod.Parameters.Count + paramOffset;

            foreach (var method in proxyType.Methods)
            {
                if (method.Name != originalMethod.Name)
                    continue;

                if (method.Parameters.Count != expectedParamCount)
                    continue;

                if (method.HasGenericParameters != originalMethod.HasGenericParameters)
                    continue;

                if (method.HasGenericParameters)
                {
                    if (method.GenericParameters.Count != originalMethod.GenericParameters.Count)
                        continue;

                    for (int i = 0; i < method.GenericParameters.Count; i++)
                    {
                        var originalParam = originalMethod.GenericParameters[i];
                        var candidateParam = method.GenericParameters[i];

                        if (originalParam.Name != candidateParam.Name)
                            goto NextMethod;

                        if (originalParam.Constraints.Count != candidateParam.Constraints.Count)
                            goto NextMethod;

                        for (int j = 0; j < originalParam.Constraints.Count; j++)
                        {
                            if (!TypesMatch(originalParam.Constraints[j].ConstraintType,
                                    candidateParam.Constraints[j].ConstraintType))
                                goto NextMethod;
                        }
                    }
                }

                if (isInstanceToStatic)
                {
                    if (!IsAssignableFrom(method.Parameters[0].ParameterType,
                            originalMethod.DeclaringType))
                        continue;
                }

                bool match = true;
                for (int i = 0; i < originalMethod.Parameters.Count; i++)
                {
                    var originalParamType = originalMethod.Parameters[i].ParameterType;
                    var candidateParamType = method.Parameters[i + paramOffset].ParameterType;

                    if (!TypesMatch(originalParamType, candidateParamType))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return method;

                NextMethod:
                // ReSharper disable once RedundantJumpStatement
                continue;
            }

            return null;
        }

        static bool TypesMatch(TypeReference original, TypeReference candidate)
        {
            if (original.FullName != candidate.FullName)
                return false;

            if (original is GenericInstanceType originalGeneric && candidate is GenericInstanceType candidateGeneric)
            {
                if (originalGeneric.GenericArguments.Count != candidateGeneric.GenericArguments.Count)
                    return false;

                for (int i = 0; i < originalGeneric.GenericArguments.Count; i++)
                {
                    if (!TypesMatch(originalGeneric.GenericArguments[i], candidateGeneric.GenericArguments[i]))
                        return false;
                }
            }

            return true;
        }

        static bool IsOrDerivedFrom(TypeReference typeRef, string baseFullName)
        {
            var current = typeRef;
            while (current != null)
            {
                if (current.FullName == baseFullName)
                    return true;
                try
                {
                    var resolved = current.Resolve();
                    current = resolved?.BaseType;
                }
                catch
                {
                    break;
                }
            }

            return false;
        }

        static bool IsAssignableFrom(TypeReference paramType, TypeReference declaringType)
        {
            return IsOrDerivedFrom(declaringType, paramType.FullName);
        }

        static TypeDefinition ResolveTypeByName(ModuleDefinition module, string fullName)
        {
            foreach (var t in module.Types)
            {
                if (t.FullName == fullName)
                    return t;
            }

            foreach (var asmRef in module.AssemblyReferences)
            {
                try
                {
                    var asm = module.AssemblyResolver.Resolve(asmRef);
                    if (asm == null) continue;

                    foreach (var t in asm.MainModule.Types)
                    {
                        if (t.FullName == fullName)
                            return t;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
#endif
