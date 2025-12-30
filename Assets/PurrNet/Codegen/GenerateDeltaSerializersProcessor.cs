#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;

namespace PurrNet.Codegen
{
    public static class GenerateDeltaSerializersProcessor
    {
        public static void HandleType(AssemblyDefinition assembly, TypeReference type, TypeDefinition generatedClass)
        {
            var bitStreamType = assembly.MainModule.GetTypeDefinition(typeof(BitPacker)).Import(assembly.MainModule);

            var writeMethod = new MethodDefinition("WriteDelta", MethodAttributes.Public | MethodAttributes.Static,
                assembly.MainModule.TypeSystem.Boolean);
            var readMethod = new MethodDefinition("ReadDelta", MethodAttributes.Public | MethodAttributes.Static,
                assembly.MainModule.TypeSystem.Void);

            CreateWriteMethod(assembly.MainModule, writeMethod, type, bitStreamType);
            CreateReadMethod(assembly.MainModule, readMethod, type, bitStreamType);

            generatedClass.Methods.Add(writeMethod);
            CacheDeltaWrite(type, writeMethod);

            generatedClass.Methods.Add(readMethod);
            CacheDeltaRead(type, readMethod);
        }

        [ThreadStatic] public static Dictionary<TypeReference, MethodReference> inlinedDeltaReadMethods;
        [ThreadStatic] public static Dictionary<TypeReference, MethodReference> inlinedDeltaWriteMethods;

        static bool TryGetInlinedDeltaRead(TypeReference type, out MethodReference method)
        {
            inlinedDeltaReadMethods ??= new Dictionary<TypeReference, MethodReference>(128, TypeReferenceEqualityComparer.Default);
            return inlinedDeltaReadMethods.TryGetValue(type, out method);
        }

        static bool TryGetInlinedDeltaWrite(TypeReference type, out MethodReference method)
        {
            inlinedDeltaWriteMethods ??= new Dictionary<TypeReference, MethodReference>(128, TypeReferenceEqualityComparer.Default);
            return inlinedDeltaWriteMethods.TryGetValue(type, out method);
        }

        public static bool TryGetInlinedMethod(bool isWritting, TypeReference serializedType, out MethodReference o)
        {
            if (isWritting)
                return TryGetInlinedDeltaWrite(serializedType, out o);
            return TryGetInlinedDeltaRead(serializedType, out o);
        }

        public static bool IsSafeForInline(TypeReference type)
        {
            if (type.IsValueType)
                return true;

            if (type.IsPrimitive)
                return true;

            var resolved = type.Resolve();

            if (resolved == null)
                return false;

            if (!resolved.IsClass || resolved.IsEnum)
                return true;

            if (resolved.IsSealed)
                return true;

            return false;
        }

        public static void CacheDeltaRead(TypeReference deltaWriteType, MethodDefinition method)
        {
            if (!IsSafeForInline(deltaWriteType))
                return;
            inlinedDeltaReadMethods ??= new Dictionary<TypeReference, MethodReference>(128, TypeReferenceEqualityComparer.Default);
            inlinedDeltaReadMethods[deltaWriteType] = method;
        }

        public static void CacheDeltaWrite(TypeReference type, MethodReference reference)
        {
            if (!IsSafeForInline(type))
                return;
            inlinedDeltaWriteMethods ??= new Dictionary<TypeReference, MethodReference>(128, TypeReferenceEqualityComparer.Default);
            inlinedDeltaWriteMethods[type] = reference;
        }

        private static void CreateReadMethod(ModuleDefinition module, MethodDefinition method, TypeReference typeRef,
            TypeReference bitStreamType)
        {
            var packerType = module.GetTypeDefinition(typeof(Packer)).Import(module);
            var deltaPackerGenType = module.GetTypeDefinition(typeof(DeltaPacker<>)).Import(module);

            var deltaSerializer = deltaPackerGenType.GetMethod("Read").Import(module);
            var deltaBypassSerializer = deltaPackerGenType.GetMethod("ReadUnpacked").Import(module);
            var packerTypeBoolean = bitStreamType.GetMethod("ReadBit").Import(module);

            var streamArg = new ParameterDefinition("stream", ParameterAttributes.None, bitStreamType);
            var oldValueArg = new ParameterDefinition("oldValue", ParameterAttributes.None, typeRef);
            var valueArg = new ParameterDefinition("value", ParameterAttributes.None, new ByReferenceType(typeRef));

            var type = typeRef.Resolve();
            bool isClass = !type.IsValueType;

            method.Parameters.Add(streamArg);
            method.Parameters.Add(oldValueArg);
            method.Parameters.Add(valueArg);
            method.Body = new MethodBody(method)
            {
                InitLocals = true
            };

            var il = method.Body.GetILProcessor();
            var endOfFunction = il.Create(OpCodes.Ret);
            var elseBlock = il.Create(OpCodes.Nop);

            var standaloneType = GenerateSerializersProcessor.HasInterfaceExtra(type, typeof(IStandaloneSerializable));

            if (standaloneType != null && standaloneType.FullName != type.FullName)
            {
                if (!TryGetInlinedDeltaRead(standaloneType, out var genericM))
                    genericM = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, standaloneType, deltaSerializer,
                        module);

                var variable = new VariableDefinition(standaloneType);
                method.Body.Variables.Add(variable);

                // variable = this
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Stloc, variable);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloca, variable);
                il.Emit(OpCodes.Call, genericM);

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc, variable);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stind_Ref);

                il.Emit(OpCodes.Ret);
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, packerTypeBoolean);

            // if true, return
            il.Emit(OpCodes.Brfalse, elseBlock);

            if (isClass)
            {
                var readIsNull = bitStreamType.GetMethod("ReadIsNull", true).Import(module);
                var genericIsNull = new GenericInstanceMethod(readIsNull);
                genericIsNull.GenericArguments.Add(typeRef);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, genericIsNull);
                il.Emit(OpCodes.Brfalse, endOfFunction);
            }

            GenerateSerializersProcessor.CreateGettersAndSetters(false, type);

            if (type.IsEnum)
            {
                var underlyingType = type.GetField("value__").FieldType;
                if (!TryGetInlinedDeltaRead(underlyingType, out var enumReadMethod))
                    enumReadMethod = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, underlyingType, deltaSerializer,
                        module);

                var tmpVar = new VariableDefinition(underlyingType);

                method.Body.Variables.Add(tmpVar);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc_1);

                il.Emit(OpCodes.Ldarg_0);

                // load the address of the field
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloca, tmpVar);
                il.Emit(OpCodes.Call, enumReadMethod);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc_1);
                GenerateSerializersProcessor.EmitStindForEnum(il, type);
            }
            else
            {
                if (isClass && type.BaseType != null && type.BaseType.FullName != typeof(object).FullName)
                {
                    var baseType = type.BaseType;

                    if (baseType is { IsValueType: false })
                    {
                        if (!TryGetInlinedDeltaRead(baseType, out var genericM))
                            genericM = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, baseType, deltaSerializer,
                                    module);

                        var variable = new VariableDefinition(baseType);
                        method.Body.Variables.Add(variable);

                        // variable = this
                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Ldind_Ref);
                        il.Emit(OpCodes.Stloc, variable);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldloca, variable);
                        il.Emit(OpCodes.Call, genericM);

                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Ldloc, variable);
                        il.Emit(OpCodes.Castclass, type);
                        il.Emit(OpCodes.Stind_Ref);
                    }
                }

                foreach (var field in type.Fields)
                {
                    if (field.IsStatic)
                        continue;

                    bool isDelegate = PostProcessor.InheritsFrom(field.FieldType.Resolve(), typeof(Delegate).FullName);

                    if (isDelegate)
                        continue;

                    var fieldType = GenerateSerializersProcessor.ResolveGenericFieldType(field, typeRef);

                    if (fieldType == null)
                        continue;

                    bool ignore = ShouldIgnoreField(field);

                    if (ignore)
                        continue;

                    bool shouldSkipDelta = ShouldNotDeltaPackField(field);

                    if (!TryGetInlinedDeltaRead(fieldType, out var packer))
                        packer = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, fieldType,
                        shouldSkipDelta ? deltaBypassSerializer : deltaSerializer,
                        module);

                    if (!field.IsPublic)
                    {
                        var variable = new VariableDefinition(fieldType);
                        method.Body.Variables.Add(variable);

                        var getter = GenerateSerializersProcessor.MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}");
                        var setter = GenerateSerializersProcessor.MakeFullNameValidCSharp($"Purrnet_Set_{field.Name}");

                        var getterReference = new MethodReference(getter, fieldType, typeRef)
                        {
                            HasThis = true
                        };

                        var setterReference = new MethodReference(setter, module.TypeSystem.Void, typeRef)
                        {
                            HasThis = true
                        };

                        setterReference.Parameters.Add(
                            new ParameterDefinition("value", ParameterAttributes.None, fieldType));

                        il.Emit(OpCodes.Ldarg_0);

                        if (isClass)
                            il.Emit(OpCodes.Ldarg_1);
                        else il.Emit(OpCodes.Ldarga_S, oldValueArg);

                        il.Emit(OpCodes.Call, getterReference);

                        il.Emit(OpCodes.Ldloca, variable);
                        il.Emit(OpCodes.Call, packer);

                        il.Emit(OpCodes.Ldarg_2);
                        if (isClass) il.Emit(OpCodes.Ldind_Ref);
                        il.Emit(OpCodes.Ldloc, variable);
                        il.Emit(OpCodes.Call, setterReference);

                        continue;
                    }

                    var fieldRef = new FieldReference(field.Name, field.FieldType, typeRef).Import(module);
                    il.Emit(OpCodes.Ldarg_0);

                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldfld, fieldRef);

                    il.Emit(OpCodes.Ldarg_2);
                    if (isClass) il.Emit(OpCodes.Ldind_Ref);
                    il.Emit(OpCodes.Ldflda, fieldRef);

                    il.Emit(OpCodes.Call, packer);
                }
            }

            il.Emit(OpCodes.Br, endOfFunction);
            il.Append(elseBlock);

            // value = oldValue

            // Ldarg_2 = Packer.Copy
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_1);

            if (typeRef.Resolve()?.IsUnmanaged() != true)
            {
                var copy = packerType.GetMethod("Copy", true).Import(module);
                var copyGeneric = new GenericInstanceMethod(copy);
                copyGeneric.GenericArguments.Add(typeRef);
                il.Emit(OpCodes.Call, copyGeneric);
            }

            il.Emit(OpCodes.Stobj, typeRef);

            il.Append(endOfFunction);
        }

        private static void CreateWriteMethod(ModuleDefinition module, MethodDefinition method, TypeReference typeRef,
            TypeReference bitStreamType)
        {
            var bitPackerType = module.GetTypeDefinition(typeof(BitPacker)).Import(module);
            var deltaPackerGenType = module.GetTypeDefinition(typeof(DeltaPacker<>)).Import(module);

            var deltaSerializer = deltaPackerGenType.GetMethod("Write").Import(module);
            var deltaBypassSerializer = deltaPackerGenType.GetMethod("WriteUnpacked").Import(module);
            var advanceBits = bitPackerType.GetMethod("AdvanceBits", false).Import(module);
            var writeAt = bitPackerType.GetMethod("WriteAt", false).Import(module);
            var setBitPosition = bitPackerType.GetMethod("SetBitPosition", false).Import(module);

            var streamArg = new ParameterDefinition("stream", ParameterAttributes.None, bitStreamType);
            var oldValueArg = new ParameterDefinition("oldValue", ParameterAttributes.None, typeRef);
            var valueArg = new ParameterDefinition("value", ParameterAttributes.None, typeRef);

            var type = typeRef.Resolve();
            var flagPos = new VariableDefinition(module.TypeSystem.Int32);
            var isEqualVar = new VariableDefinition(module.TypeSystem.Boolean);

            bool isClass = !type.IsValueType;

            method.Parameters.Add(streamArg);
            method.Parameters.Add(oldValueArg);
            method.Parameters.Add(valueArg);
            method.Body = new MethodBody(method)
            {
                InitLocals = true
            };

            method.Body.Variables.Add(flagPos);
            method.Body.Variables.Add(isEqualVar);

            var il = method.Body.GetILProcessor();
            var endOfFunction = il.Create(OpCodes.Nop);

            var standaloneType = GenerateSerializersProcessor.HasInterfaceExtra(type, typeof(IStandaloneSerializable));

            if (standaloneType != null && standaloneType.FullName != type.FullName)
            {
                if (!TryGetInlinedDeltaWrite(standaloneType, out var genericM))
                    genericM =
                    GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, standaloneType, deltaSerializer,
                        module);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, genericM);
                il.Emit(OpCodes.Ret);
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, advanceBits);
            il.Emit(OpCodes.Stloc_0);

            if (isClass)
            {
                var writeIsNull = bitStreamType.GetMethod("HandleNullScenarios", true).Import(module);
                var genericIsNull = new GenericInstanceMethod(writeIsNull);
                genericIsNull.GenericArguments.Add(typeRef);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloca_S, isEqualVar);
                il.Emit(OpCodes.Call, genericIsNull);

                // if null return
                il.Emit(OpCodes.Brfalse, endOfFunction);
            }

            GenerateSerializersProcessor.CreateGettersAndSetters(true, type);

            if (type.IsEnum)
            {
                var underlyingType = type.GetField("value__").FieldType;
                if (!TryGetInlinedDeltaWrite(underlyingType, out var enumWriteMethod))
                    enumWriteMethod = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, underlyingType, deltaSerializer,
                        module);

                il.Emit(OpCodes.Ldarg_0);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, enumWriteMethod);

                il.Emit(OpCodes.Stloc_1);
            }
            else
            {
                bool isInheritedClass = isClass && type.BaseType != null &&
                                    type.BaseType.FullName != typeof(object).FullName;

                if (isInheritedClass)
                {
                    var baseType = type.BaseType;

                    if (baseType is { IsValueType: false })
                    {
                        if (!TryGetInlinedDeltaWrite(baseType, out var genericM))
                            genericM = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, baseType, deltaSerializer,
                                module);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldarg_2);

                        il.Emit(OpCodes.Call, genericM);
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Or);

                        il.Emit(OpCodes.Stloc_1);
                    }
                }

                for (var i = 0; i < type.Fields.Count; i++)
                {
                    var field = type.Fields[i];
                    if (field.IsStatic)
                        continue;

                    bool isDelegate = PostProcessor.InheritsFrom(field.FieldType.Resolve(), typeof(Delegate).FullName);

                    if (isDelegate)
                        continue;

                    var fieldType = GenerateSerializersProcessor.ResolveGenericFieldType(field, typeRef);

                    if (fieldType == null)
                        continue;

                    var ignore = ShouldIgnoreField(field);

                    if (ignore)
                        continue;

                    bool shouldSkipDelta = ShouldNotDeltaPackField(field);

                    if (!TryGetInlinedDeltaWrite(fieldType, out var packer))
                        packer = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, fieldType,
                            shouldSkipDelta ? deltaBypassSerializer : deltaSerializer,
                            module);

                    if (!field.IsPublic)
                    {
                        var variable = new VariableDefinition(fieldType);
                        method.Body.Variables.Add(variable);

                        var getter = GenerateSerializersProcessor.MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}");
                        var getterReference = new MethodReference(getter, fieldType, typeRef)
                        {
                            HasThis = true
                        };

                        il.Emit(OpCodes.Ldarg_0);

                        if (isClass)
                            il.Emit(OpCodes.Ldarg_1);
                        else il.Emit(OpCodes.Ldarga_S, oldValueArg);

                        il.Emit(OpCodes.Call, getterReference);

                        if (isClass)
                            il.Emit(OpCodes.Ldarg_2);
                        else il.Emit(OpCodes.Ldarga_S, valueArg);

                        il.Emit(OpCodes.Call, getterReference);
                    }
                    else
                    {
                        var fieldRef = new FieldReference(field.Name, field.FieldType, typeRef).Import(module);

                        il.Emit(OpCodes.Ldarg_0);

                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldfld, fieldRef);

                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Ldfld, fieldRef);
                    }

                    il.Emit(OpCodes.Call, packer);

                    if (i > 0 || isInheritedClass)
                    {
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Or);
                    }

                    il.Emit(OpCodes.Stloc_1);
                }
            }

            // WriteAt
            il.Append(endOfFunction);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Call, writeAt);

            // if (isEqual)
            var endOfIf = il.Create(OpCodes.Nop);

            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Brtrue, endOfIf);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Call, setBitPosition);

            il.Append(endOfIf);

            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Ret);

            method.DebugInformation.Scope =
                new ScopeDebugInformation(method.Body.Instructions[0], method.Body.Instructions[^1]);
            method.DebugInformation.Scope.Variables.Add(new VariableDebugInformation(flagPos, "flagPos"));
            method.DebugInformation.Scope.Variables.Add(new VariableDebugInformation(isEqualVar, "wasChanged"));
        }

        private static bool ShouldIgnoreField(FieldDefinition field)
        {
            bool ignore = field.CustomAttributes.Any(a =>
                a.AttributeType.FullName == typeof(DontPackAttribute).FullName) || GenerateSerializersProcessor.DoesTypeHaveDontPackAttribute(field.FieldType.Resolve());

            return ignore;
        }

        private static bool ShouldNotDeltaPackField(FieldDefinition field)
        {
            bool ignore = field.CustomAttributes.Any(a =>
                a.AttributeType.FullName == typeof(DontDeltaCompressAttribute).FullName);

            if (GenerateSerializersProcessor.DoesTypeHaveAttribute(field.FieldType.Resolve(), typeof(DontDeltaCompressAttribute)))
                ignore = true;
            return ignore;
        }

        public static void HandleGenericType(AssemblyDefinition assembly, TypeReference type,
            HandledGenericTypes genericT)
        {
            // TODO: Implement
        }
    }
}
#endif
