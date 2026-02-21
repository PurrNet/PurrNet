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

        public static bool TryGetInlinedMethod(bool isWritting, TypeReference serializedType, ModuleDefinition module, out MethodReference o)
        {
            if (isWritting)
            {
                if (TryGetInlinedDeltaWrite(serializedType, out o))
                {
                    o = o.Import(module);
                    return true;
                }
                return false;
            }
            if (TryGetInlinedDeltaRead(serializedType, out o))
            {
                o = o.Import(module);
                return true;
            }
            return false;
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

        static TypeReference GetDeltaPackerForType(ModuleDefinition module, TypeReference type)
        {
            bool isUnmanaged = type?.Resolve()?.IsUnmanaged() == true;
            if (isUnmanaged)
                return module.GetTypeDefinition(typeof(NativeDeltaPacker<>)).Import(module);
            return module.GetTypeDefinition(typeof(DeltaPacker<>)).Import(module);
        }

        static MethodReference GetDeltaPackerReadMethod(ModuleDefinition module, TypeReference type)
        {
            var deltaPackerGenType = GetDeltaPackerForType(module, type);
            var deltaSerializer = deltaPackerGenType.GetMethod("Read").Import(module);
            return deltaSerializer;
        }

        static MethodReference GetDeltaPackerReadMethodUnpacked(ModuleDefinition module, TypeReference type)
        {
            var deltaPackerGenType = GetDeltaPackerForType(module, type);
            var deltaSerializer = deltaPackerGenType.GetMethod("ReadUnpacked").Import(module);
            return deltaSerializer;
        }

        static MethodReference GetDeltaPackerWriteMethod(ModuleDefinition module, TypeReference type)
        {
            var deltaPackerGenType = GetDeltaPackerForType(module, type);
            var deltaSerializer = deltaPackerGenType.GetMethod("Write").Import(module);
            return deltaSerializer;
        }

        static MethodReference GetDeltaPackerWriteMethodUnpacked(ModuleDefinition module, TypeReference type)
        {
            var deltaPackerGenType = GetDeltaPackerForType(module, type);
            var deltaSerializer = deltaPackerGenType.GetMethod("WriteUnpacked").Import(module);
            return deltaSerializer;
        }

        private static void CreateReadMethod(ModuleDefinition module, MethodDefinition method, TypeReference typeRef,
            TypeReference bitStreamType)
        {
            var packerType = module.GetTypeDefinition(typeof(Packer)).Import(module);
            var packerTypeBoolean = bitStreamType.GetMethod("ReadBit").Import(module);
            var advanceBit = bitStreamType.GetMethod("AdvanceBit", false).Import(module);

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
            var elseBlock = il.Create(OpCodes.Ldarg_2);

            var standaloneType = GenerateSerializersProcessor.HasInterfaceExtra(type, typeof(IStandaloneSerializable));

            if (standaloneType != null && standaloneType.FullName != type.FullName)
            {
                bool useDirectCall = !TryGetInlinedMethod(false, standaloneType, module, out var genericM);
                bool standaloneUnmanaged = useDirectCall && standaloneType.Resolve()?.IsUnmanaged() == true;

                var variable = new VariableDefinition(standaloneType);
                method.Body.Variables.Add(variable);

                // variable = this
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Stloc, variable);

                if (useDirectCall && !standaloneUnmanaged)
                    EmitLoadDeltaDelegate(il, module, standaloneType, false);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloca, variable);

                if (!useDirectCall)
                    il.Emit(OpCodes.Call, genericM);
                else
                    EmitDirectDeltaCall(il, module, standaloneType, bitStreamType, false, standaloneUnmanaged);

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc, variable);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stind_Ref);

                il.Emit(OpCodes.Ret);
                return;
            }

            int readFields = 0;

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
                ++readFields;
            }

            GenerateSerializersProcessor.CreateGettersAndSetters(false, type);

            if (type.IsEnum)
            {
                var underlyingType = type.GetField("value__").FieldType;
                bool useDirectCall = !TryGetInlinedMethod(false, underlyingType, module, out var enumReadMethod);
                bool enumUnmanaged = useDirectCall && underlyingType.Resolve()?.IsUnmanaged() == true;

                var tmpVar = new VariableDefinition(underlyingType);

                method.Body.Variables.Add(tmpVar);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, tmpVar);

                if (useDirectCall && !enumUnmanaged)
                    EmitLoadDeltaDelegate(il, module, underlyingType, false);

                il.Emit(OpCodes.Ldarg_0);

                // load the address of the field
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloca, tmpVar);

                if (!useDirectCall)
                    il.Emit(OpCodes.Call, enumReadMethod);
                else
                    EmitDirectDeltaCall(il, module, underlyingType, bitStreamType, false, enumUnmanaged);

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc, tmpVar);
                GenerateSerializersProcessor.EmitStindForEnum(il, type);
                ++readFields;
            }
            else
            {
                if (isClass && type.BaseType != null && type.BaseType.FullName != typeof(object).FullName)
                {
                    var baseType = GenerateSerializersProcessor.ResolveGenericTypeRef(type.BaseType, typeRef);

                    if (baseType is { IsValueType: false })
                    {
                        bool useDirectCall = !TryGetInlinedMethod(false, baseType, module, out var genericM);

                        var variable = new VariableDefinition(baseType);
                        method.Body.Variables.Add(variable);

                        // variable = this
                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Ldind_Ref);
                        il.Emit(OpCodes.Stloc, variable);

                        // baseType is always a class (IsValueType: false), so always managed delegate path
                        if (useDirectCall)
                            EmitLoadDeltaDelegate(il, module, baseType, false);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldloca, variable);

                        if (!useDirectCall)
                            il.Emit(OpCodes.Call, genericM);
                        else
                            EmitDelegateInvoke(il, module, baseType, false);

                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Ldloc, variable);
                        il.Emit(OpCodes.Castclass, typeRef);
                        il.Emit(OpCodes.Stind_Ref);
                        ++readFields;
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

                    bool useDirectCall = false;
                    bool isFieldUnmanaged = false;
                    MethodReference packer = null;

                    if (TryGetInlinedMethod(false, fieldType, module, out packer))
                    {
                        // inlined - use Call
                    }
                    else if (shouldSkipDelta)
                    {
                        var deltaPackerGenType = GetDeltaPackerForType(module, fieldType);
                        var deltaBypassSerializer = GetDeltaPackerReadMethodUnpacked(module, fieldType);
                        packer = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, fieldType, deltaBypassSerializer, module);
                    }
                    else
                    {
                        useDirectCall = true;
                        isFieldUnmanaged = fieldType.Resolve()?.IsUnmanaged() == true;
                    }

                    if (!field.IsPublic)
                    {
                        var variable = new VariableDefinition(fieldType);
                        method.Body.Variables.Add(variable);

                        var getter = GenerateSerializersProcessor.MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}");
                        var setter = GenerateSerializersProcessor.MakeFullNameValidCSharp($"Purrnet_Set_{field.Name}");

                        var getterReference = new MethodReference(getter, field.FieldType, typeRef)
                        {
                            HasThis = true
                        };

                        var setterReference = new MethodReference(setter, module.TypeSystem.Void, typeRef)
                        {
                            HasThis = true
                        };

                        setterReference.Parameters.Add(
                            new ParameterDefinition("value", ParameterAttributes.None, field.FieldType));

                        if (useDirectCall && !isFieldUnmanaged)
                            EmitLoadDeltaDelegate(il, module, fieldType, false);

                        il.Emit(OpCodes.Ldarg_0);

                        if (isClass)
                            il.Emit(OpCodes.Ldarg_1);
                        else il.Emit(OpCodes.Ldarga_S, oldValueArg);

                        il.Emit(OpCodes.Call, getterReference);

                        il.Emit(OpCodes.Ldloca, variable);

                        if (!useDirectCall)
                            il.Emit(OpCodes.Call, packer);
                        else
                            EmitDirectDeltaCall(il, module, fieldType, bitStreamType, false, isFieldUnmanaged);

                        il.Emit(OpCodes.Ldarg_2);
                        if (isClass) il.Emit(OpCodes.Ldind_Ref);
                        il.Emit(OpCodes.Ldloc, variable);
                        il.Emit(OpCodes.Callvirt, setterReference);
                        ++readFields;
                        continue;
                    }

                    var fieldRef = new FieldReference(field.Name, field.FieldType, typeRef).Import(module);

                    if (useDirectCall && !isFieldUnmanaged)
                        EmitLoadDeltaDelegate(il, module, fieldType, false);

                    il.Emit(OpCodes.Ldarg_0);

                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldfld, fieldRef);

                    il.Emit(OpCodes.Ldarg_2);
                    if (isClass) il.Emit(OpCodes.Ldind_Ref);
                    il.Emit(OpCodes.Ldflda, fieldRef);

                    if (!useDirectCall)
                        il.Emit(OpCodes.Call, packer);
                    else
                        EmitDirectDeltaCall(il, module, fieldType, bitStreamType, false, isFieldUnmanaged);

                    ++readFields;
                }
            }

            il.Emit(OpCodes.Ret);
            il.Append(elseBlock);

            // value = oldValue

            // Ldarg_2 = Packer.Copy
            bool isUnmanaged = typeRef.Resolve()?.IsUnmanaged() == true;

            if (isUnmanaged)
                il.Emit(OpCodes.Ldarg_1);
            else il.Emit(OpCodes.Ldarga_S, oldValueArg);

            if (!isUnmanaged)
            {
                var copy = packerType.GetMethod("Copy", true).Import(module);
                var copyGeneric = new GenericInstanceMethod(copy);
                copyGeneric.GenericArguments.Add(typeRef);
                il.Emit(OpCodes.Call, copyGeneric);
            }

            il.Emit(OpCodes.Stobj, typeRef);
            il.Append(endOfFunction);

            if (readFields == 0)
            {
                il.Clear();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, advanceBit);
                il.Emit(OpCodes.Ret);
            }
        }

        private static void CreateWriteMethod(ModuleDefinition module, MethodDefinition method, TypeReference typeRef,
            TypeReference bitStreamType)
        {
            var bitPackerType = module.GetTypeDefinition(typeof(BitPacker)).Import(module);
            var advanceOneBitAndSet = bitPackerType.GetMethod("AdvanceOneBitAndSet", false).Import(module);
            var writeBit = bitPackerType.GetMethod("WriteBit", false).Import(module);
            var resetFlagAtAndMovePosition = bitPackerType.GetMethod("ResetFlagAtAndMovePosition", false).Import(module);

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
            var endOfFunction = il.Create(OpCodes.Ldarg_0);
            var returnFalse = il.Create(OpCodes.Ldc_I4_0);
            var startOfNormalDelta = il.Create(OpCodes.Nop);

            var standaloneType = GenerateSerializersProcessor.HasInterfaceExtra(type, typeof(IStandaloneSerializable));

            if (standaloneType != null && standaloneType.FullName != type.FullName)
            {
                bool useDirectCall = !TryGetInlinedMethod(true, standaloneType, module, out var genericM);
                bool standaloneUnmanaged = useDirectCall && standaloneType.Resolve()?.IsUnmanaged() == true;

                if (useDirectCall && !standaloneUnmanaged)
                    EmitLoadDeltaDelegate(il, module, standaloneType, true);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);

                if (!useDirectCall)
                    il.Emit(OpCodes.Call, genericM);
                else
                    EmitDirectDeltaCall(il, module, standaloneType, bitStreamType, true, standaloneUnmanaged);

                il.Emit(OpCodes.Ret);
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, advanceOneBitAndSet);
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

                il.Emit(OpCodes.Brtrue, startOfNormalDelta);

                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);

            }

            il.Append(startOfNormalDelta);

            GenerateSerializersProcessor.CreateGettersAndSetters(true, type);
            int writtenFields = 0;

            if (type.IsEnum)
            {
                var underlyingType = type.GetField("value__").FieldType;
                bool useDirectCall = !TryGetInlinedMethod(true, underlyingType, module, out var enumWriteMethod);
                bool enumUnmanaged = useDirectCall && underlyingType.Resolve()?.IsUnmanaged() == true;

                if (useDirectCall && !enumUnmanaged)
                    EmitLoadDeltaDelegate(il, module, underlyingType, true);

                il.Emit(OpCodes.Ldarg_0);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);

                if (!useDirectCall)
                    il.Emit(OpCodes.Call, enumWriteMethod);
                else
                    EmitDirectDeltaCall(il, module, underlyingType, bitStreamType, true, enumUnmanaged);

                il.Emit(OpCodes.Stloc_1);
                ++writtenFields;
            }
            else
            {
                bool isInheritedClass = isClass && type.BaseType != null &&
                                    type.BaseType.FullName != typeof(object).FullName;

                if (isInheritedClass)
                {
                    var baseType = GenerateSerializersProcessor.ResolveGenericTypeRef(type.BaseType, typeRef);

                    if (baseType is { IsValueType: false })
                    {
                        bool useDirectCall = !TryGetInlinedMethod(true, baseType, module, out var genericM);

                        // baseType is always a class (IsValueType: false), so always managed delegate path
                        if (useDirectCall)
                            EmitLoadDeltaDelegate(il, module, baseType, true);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldarg_2);

                        if (!useDirectCall)
                            il.Emit(OpCodes.Call, genericM);
                        else
                            EmitDelegateInvoke(il, module, baseType, true);

                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Or);

                        il.Emit(OpCodes.Stloc_1);
                        ++writtenFields;
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

                    ++writtenFields;

                    bool shouldSkipDelta = ShouldNotDeltaPackField(field);

                    bool useDirectCall = false;
                    bool isFieldUnmanaged = false;
                    MethodReference packer = null;

                    if (TryGetInlinedMethod(true, fieldType, module, out packer))
                    {
                        // inlined - use Call
                    }
                    else if (shouldSkipDelta)
                    {
                        var deltaPackerGenType = GetDeltaPackerForType(module, fieldType);
                        var deltaBypassSerializer = GetDeltaPackerWriteMethodUnpacked(module, fieldType);
                        packer = GenerateSerializersProcessor.CreateGenericMethod(deltaPackerGenType, fieldType, deltaBypassSerializer, module);
                    }
                    else
                    {
                        useDirectCall = true;
                        isFieldUnmanaged = fieldType.Resolve()?.IsUnmanaged() == true;
                    }

                    if (i > 0 || isInheritedClass)
                    {
                        il.Emit(OpCodes.Ldloc_1);
                    }

                    if (useDirectCall && !isFieldUnmanaged)
                        EmitLoadDeltaDelegate(il, module, fieldType, true);

                    if (!field.IsPublic)
                    {
                        var variable = new VariableDefinition(fieldType);
                        method.Body.Variables.Add(variable);

                        var getter = GenerateSerializersProcessor.MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}");
                        var getterReference = new MethodReference(getter, field.FieldType, typeRef)
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

                    if (!useDirectCall)
                        il.Emit(OpCodes.Call, packer);
                    else
                        EmitDirectDeltaCall(il, module, fieldType, bitStreamType, true, isFieldUnmanaged);

                    if (i > 0 || isInheritedClass)
                        il.Emit(OpCodes.Or);

                    il.Emit(OpCodes.Stloc_1);
                }
            }

            // if (isEqual)
            var endOfIf = il.Create(OpCodes.Ldc_I4_1);

            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Brtrue, endOfIf);

            // resetFlagAtAndMovePosition
            il.Append(endOfFunction);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Call, resetFlagAtAndMovePosition);
            il.Append(returnFalse);
            il.Emit(OpCodes.Ret);

            il.Append(endOfIf);
            il.Emit(OpCodes.Ret);

            if (writtenFields == 0)
            {
                method.Body.Instructions.Clear();
                method.Body.Variables.Clear();
                method.Body.InitLocals = false;

                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, writeBit));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            }

            method.DebugInformation.Scope =
                new ScopeDebugInformation(method.Body.Instructions[0], method.Body.Instructions[^1]);
            method.DebugInformation.Scope.Variables.Add(new VariableDebugInformation(flagPos, "flagPos"));
            method.DebugInformation.Scope.Variables.Add(new VariableDebugInformation(isEqualVar, "wasChanged"));
        }

        /// <summary>
        /// For managed types: emits ldsfld DeltaPacker(T).WriteFunc/ReadFunc before args.
        /// </summary>
        static void EmitLoadDeltaDelegate(ILProcessor il, ModuleDefinition module, TypeReference fieldType, bool isWrite)
        {
            var deltaPackerDef = module.GetTypeDefinition(typeof(DeltaPacker<>));
            var fieldName = isWrite ? "WriteFunc" : "ReadFunc";

            // Construct the field type: DeltaWriteFunc<!0> or DeltaReadFunc<!0>
            // We import the open delegate type and use the raw generic parameter !0 from the definition.
            // This avoids ImportReference crashing on generic parameters without context.
            var delegateOpenType = isWrite
                ? module.GetTypeDefinition(typeof(DeltaWriteFunc<>))
                : module.GetTypeDefinition(typeof(DeltaReadFunc<>));
            var constructedFieldType = new GenericInstanceType(module.ImportReference(delegateOpenType));
            constructedFieldType.GenericArguments.Add(deltaPackerDef.GenericParameters[0]);

            var genericInstance = new GenericInstanceType(deltaPackerDef.Import(module));
            genericInstance.GenericArguments.Add(fieldType);

            var fieldRef = new FieldReference(fieldName, constructedFieldType, genericInstance);
            il.Emit(OpCodes.Ldsfld, fieldRef);
        }

        /// <summary>
        /// For managed types: emits callvirt DeltaWriteFunc(T)/DeltaReadFunc(T).Invoke after args.
        /// </summary>
        static void EmitDelegateInvoke(ILProcessor il, ModuleDefinition module, TypeReference fieldType, bool isWrite)
        {
            var delegateTypeDef = isWrite
                ? module.GetTypeDefinition(typeof(DeltaWriteFunc<>))
                : module.GetTypeDefinition(typeof(DeltaReadFunc<>));

            var genericDelegate = new GenericInstanceType(delegateTypeDef.Import(module));
            genericDelegate.GenericArguments.Add(fieldType);

            var invokeRef = genericDelegate.GetMethodRef("Invoke").Import(module);
            il.Emit(OpCodes.Callvirt, invokeRef);
        }

        /// <summary>
        /// For unmanaged types: emits ldsfld NativeDeltaPacker(T).WriteFunc/ReadFunc + calli after args.
        /// </summary>
        static void EmitNativeCalli(ILProcessor il, ModuleDefinition module, TypeReference fieldType, TypeReference bitStreamType, bool isWrite)
        {
            var nativeDeltaPackerDef = module.GetTypeDefinition(typeof(NativeDeltaPacker<>));
            var fieldName = isWrite ? "WriteFunc" : "ReadFunc";
            var genericParam = nativeDeltaPackerDef.GenericParameters[0]; // !0

            // Construct FunctionPointerType matching the field signature:
            // Write: delegate*<BitPacker, !0, !0, bool>
            // Read:  delegate*<BitPacker, !0, ref !0, void>
            var funcPtrType = new FunctionPointerType();
            funcPtrType.ReturnType = isWrite ? module.TypeSystem.Boolean : module.TypeSystem.Void;
            funcPtrType.Parameters.Add(new ParameterDefinition(bitStreamType));
            funcPtrType.Parameters.Add(new ParameterDefinition(genericParam));
            funcPtrType.Parameters.Add(isWrite
                ? new ParameterDefinition(genericParam)
                : new ParameterDefinition(new ByReferenceType(genericParam)));

            var genericInstance = new GenericInstanceType(nativeDeltaPackerDef.Import(module));
            genericInstance.GenericArguments.Add(fieldType);

            var fieldRef = new FieldReference(fieldName, funcPtrType, genericInstance);
            il.Emit(OpCodes.Ldsfld, fieldRef);

            // calli uses concrete types (the function pointer is already on the stack)
            var callSite = new CallSite(isWrite ? module.TypeSystem.Boolean : module.TypeSystem.Void);
            callSite.Parameters.Add(new ParameterDefinition(bitStreamType));
            callSite.Parameters.Add(new ParameterDefinition(fieldType));
            callSite.Parameters.Add(isWrite
                ? new ParameterDefinition(fieldType)
                : new ParameterDefinition(new ByReferenceType(fieldType)));
            il.Emit(OpCodes.Calli, callSite);
        }

        /// <summary>
        /// Emits a direct delta call, bypassing .Write/.Read wrappers.
        /// Unmanaged: ldsfld NativeDeltaPacker(T).Func + calli.
        /// Managed: callvirt DeltaWriteFunc(T)/DeltaReadFunc(T).Invoke (delegate must already be on stack).
        /// </summary>
        static void EmitDirectDeltaCall(ILProcessor il, ModuleDefinition module, TypeReference fieldType, TypeReference bitStreamType, bool isWrite, bool isUnmanaged)
        {
            if (isUnmanaged)
                EmitNativeCalli(il, module, fieldType, bitStreamType, isWrite);
            else
                EmitDelegateInvoke(il, module, fieldType, isWrite);
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
                a.AttributeType.FullName == typeof(DontDeltaCompressAttribute).FullName) || GenerateSerializersProcessor.DoesTypeHaveAttribute(field.FieldType.Resolve(), typeof(DontDeltaCompressAttribute));
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
