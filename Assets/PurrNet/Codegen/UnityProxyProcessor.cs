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

        public static void Process(TypeDefinition type, [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            try
            {
                bool isProxyItself = type.FullName == typeof(UnityProxy).FullName
                    || type.FullName == AddressablesProxyFullName;

                if (isProxyItself)
                    return;

                var module = type.Module;

                string objectClassFullName = typeof(UnityEngine.Object).FullName;

                TypeDefinition addressablesProxyType = null;
                bool addressablesProxyResolved = false;

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

                        // --- UnityEngine.Object interception (Instantiate / Destroy / DontDestroyOnLoad) ---
                        if (declaringTypeName == objectClassFullName)
                        {
                            if (methodReference.Name != "Instantiate" &&
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

                            processor.Replace(instruction, processor.Create(OpCodes.Call, targerRef));
                            continue;
                        }

                        // --- Addressables interception (InstantiateAsync / ReleaseInstance) ---
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

                        processor.Replace(instruction, processor.Create(OpCodes.Call, addrTargetRef));
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

        /// <summary>
        /// Finds a matching method definition in the proxy type for the given original method.
        /// For instance methods (isInstanceToStatic = true), the proxy method has one extra
        /// parameter at position 0 representing the original 'this' reference.
        /// </summary>
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

                // Check for matching generic parameters
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

                        // Compare names and constraints
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

                // For instance-to-static, verify the first proxy param accepts the declaring type
                if (isInstanceToStatic)
                {
                    if (!IsAssignableFrom(method.Parameters[0].ParameterType,
                            originalMethod.DeclaringType))
                        continue;
                }

                // Check remaining parameters
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

            // If either type is generic, check their arguments
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

        /// <summary>
        /// Checks whether typeRef is, or derives from, a type with the given full name.
        /// Used to match AssetReferenceGameObject → AssetReference hierarchy.
        /// </summary>
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

        /// <summary>
        /// Checks whether the declaring type is assignable to the parameter type.
        /// i.e. declaringType == paramType or declaringType derives from paramType.
        /// </summary>
        static bool IsAssignableFrom(TypeReference paramType, TypeReference declaringType)
        {
            return IsOrDerivedFrom(declaringType, paramType.FullName);
        }

        /// <summary>
        /// Finds a type definition by full name across the module and its referenced assemblies.
        /// Used for string-based lookup when typeof() is not available (e.g. conditional compilation).
        /// </summary>
        static TypeDefinition ResolveTypeByName(ModuleDefinition module, string fullName)
        {
            // Check the module's own types
            foreach (var t in module.Types)
            {
                if (t.FullName == fullName)
                    return t;
            }

            // Check referenced assemblies
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
                    // Skip unresolvable assemblies
                }
            }

            return null;
        }
    }
}
#endif
