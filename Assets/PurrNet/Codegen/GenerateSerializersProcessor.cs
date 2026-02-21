#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using Object = UnityEngine.Object;

namespace PurrNet.Codegen
{
    public enum HandledGenericTypes
    {
        None,
        List,
        Array,
        HashSet,
        Dictionary,
        Nullable,
        Queue,
        Stack,
        DisposableList,
        DisposableArray,
        DisposableHashSet,
        DisposableDictionary,
        NativeArray,
        NativeList
    }

    public static class GenerateSerializersProcessor
    {
        public static bool ValideType(TypeReference type)
        {
            // Check if the type itself is an interface
            /*if (type.Resolve()?.IsInterface == true)
            {
                return false;
            }*/

            bool isDelegate = PostProcessor.InheritsFrom(type.Resolve(), typeof(Delegate).FullName);

            if (isDelegate)
                return false;

            // Check if the type is a generic instance
            if (type is GenericInstanceType genericInstance)
            {
                if (genericInstance.GenericArguments.Count == 0)
                    return false;

                // Recursively validate all generic arguments
                foreach (var argument in genericInstance.GenericArguments)
                {
                    if (argument.ContainsGenericParameter ||/* argument.Resolve()?.IsInterface == true ||*/
                        !ValideType(argument))
                    {
                        return false;
                    }
                }
            }
            else if (type.ContainsGenericParameter)
            {
                // If the type itself contains generic parameters (e.g., T)
                return false;
            }

            if (type.HasGenericParameters)
            {
                foreach (var param in type.GenericParameters)
                {
                    if (!PostProcessor.IsConcreteType(param, out _))
                        return false;
                }
            }

            // If no open generics or interfaces are found, return true
            return true;
        }

        public static string MakeFullNameValidCSharp(string name)
        {
            return name.Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "_").Replace(".", "_")
                .Replace("`", "_").Replace("/", "_").Replace("[", "_I_").Replace("]", "_I_");
        }

        public static void HandleType(bool hashOnly, AssemblyDefinition assembly, TypeReference type,
            HashSet<string> visited, HashSet<TypeReference> ignoreSerialization,
            HashSet<TypeReference> ignoreDelta)
        {
            if (!visited.Add(type.FullName))
                return;

            if (!ValideType(type))
                return;

            if (!PostProcessor.IsTypeInOwnModule(type, assembly.MainModule))
                return;

            string namespaceName = type.Namespace;
            if (string.IsNullOrWhiteSpace(namespaceName))
                namespaceName = "PurrNet.CodeGen.Serializers";
            else namespaceName += ".PurrNet.CodeGen.Serializers";

            // create static class
            var serializerClass = new TypeDefinition(namespaceName,
                $"{MakeFullNameValidCSharp(type.FullName)}_Serializer",
                TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.Public,
                assembly.MainModule.TypeSystem.Object
            );

            var editorType = assembly.MainModule.GetTypeDefinition<GeneratedByILAttribute>().Import(assembly.MainModule);
            var editorConstructor = editorType.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters).Import(assembly.MainModule);
            var editorAttribute = new CustomAttribute(editorConstructor);
            serializerClass.CustomAttributes.Add(editorAttribute);
            var resolvedType = type.Resolve();

            if (resolvedType == null)
                return;

            bool hasDontPack = DoesTypeHaveDontPackAttribute(resolvedType);

            if (hasDontPack)
                return;

            var bitStreamType = assembly.MainModule.GetTypeDefinition(typeof(BitPacker)).Import(assembly.MainModule);
            var mainmodule = assembly.MainModule;

            if (resolvedType.IsInterface || hashOnly)
            {
                assembly.MainModule.Types.Add(serializerClass);
                HandleHashOnly(assembly, type, serializerClass);
                return;
            }

            bool isNetworkIdentity = PostProcessor.InheritsFrom(resolvedType, typeof(NetworkIdentity).FullName);
            bool isNetworkModule = PostProcessor.InheritsFrom(resolvedType, typeof(NetworkModule).FullName);
            bool hasINetworkModule = HasInterface(resolvedType, typeof(INetworkModule));

            if (hasINetworkModule)
                return;

            if (!isNetworkIdentity && !isNetworkModule &&
                PostProcessor.InheritsFrom(resolvedType, typeof(Object).FullName) &&
                !HasInterface(resolvedType, typeof(IPacked)) &&
                !HasInterface(resolvedType, typeof(IPackedAuto)) &&
                !HasInterface(resolvedType, typeof(IPackedSimple)))
            {
                return;
            }

            assembly.MainModule.Types.Add(serializerClass);

            if (IsGeneric(type, out var genericT))
            {
                GenerateDeltaSerializersProcessor.HandleGenericType(assembly, type, genericT);
                HandleGenerics(assembly, type, genericT, serializerClass);
                return;
            }

            if (isNetworkIdentity)
            {
                HandleNetworkIdentity(assembly, type, serializerClass);
                return;
            }

            if (isNetworkModule)
            {
                HandleNetworkModule(assembly, type, serializerClass);
                return;
            }

            if (ignoreSerialization?.Contains(type) == false)
            {
                // create static write method
                var writeMethod = new MethodDefinition("Write", MethodAttributes.Public | MethodAttributes.Static,
                    assembly.MainModule.TypeSystem.Void);
                var valueArg = new ParameterDefinition("value", ParameterAttributes.None, type);
                var streamArg = new ParameterDefinition("stream", ParameterAttributes.None, bitStreamType);
                writeMethod.Parameters.Add(streamArg);
                writeMethod.Parameters.Add(valueArg);
                writeMethod.Body = new MethodBody(writeMethod)
                {
                    InitLocals = true
                };

                var packerType = mainmodule.GetTypeDefinition(typeof(Packer<>)).Import(mainmodule);
                var readMethodP = packerType.GetMethod("Read").Import(mainmodule);
                var readDirectP = packerType.GetMethod("ReadAsExactType").Import(mainmodule);
                var writeMethodP = packerType.GetMethod("Write").Import(mainmodule);
                var writeDirectP = packerType.GetMethod("WriteAsExactType").Import(mainmodule);

                var write = writeMethod.Body.GetILProcessor();
                GenerateMethod(true, writeMethod, writeMethodP, writeDirectP, type, write, mainmodule, valueArg);
                serializerClass.Methods.Add(writeMethod);

                // create static read method
                var readMethod = new MethodDefinition("Read", MethodAttributes.Public | MethodAttributes.Static,
                    assembly.MainModule.TypeSystem.Void);
                readMethod.Parameters.Add(new ParameterDefinition("stream", ParameterAttributes.None, bitStreamType));
                readMethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                    new ByReferenceType(type)));

                readMethod.Body = new MethodBody(readMethod)
                {
                    InitLocals = true
                };

                var read = readMethod.Body.GetILProcessor();
                GenerateMethod(false, readMethod, readMethodP, readDirectP, type, read, mainmodule, valueArg);
                serializerClass.Methods.Add(readMethod);
            }

            if (ignoreDelta?.Contains(type) == false)
                GenerateDeltaSerializersProcessor.HandleType(assembly, type, serializerClass);

            GenerateIEquatableInterface.HandleType(resolvedType);
            GenerateIDuplicateInterface.HandleType(resolvedType);
            RegisterSerializersProcessor.HandleType(type, type.Module, serializerClass, null, null);
        }

        private static void HandleHashOnly(AssemblyDefinition assembly, TypeReference type,
            TypeDefinition serializerClass)
        {
            var registerMethod =
                new MethodDefinition("Register", MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);

            var editorType = assembly.MainModule.GetTypeDefinition<RegisterPackersAttribute>()
                .Import(assembly.MainModule);
            var editorConstructor = editorType.Resolve().Methods.First(m => m.IsConstructor && m.HasParameters)
                .Import(assembly.MainModule);
            var editorAttribute = new CustomAttribute(editorConstructor);
            editorAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.Int32, -1));
            registerMethod.CustomAttributes.Add(editorAttribute);

            var il = registerMethod.Body.GetILProcessor();

            var networkRegister = assembly.MainModule.GetTypeDefinition(typeof(NetworkRegister))
                .Import(assembly.MainModule);
            var hashMethod = networkRegister.GetMethod("Hash").Import(assembly.MainModule);

            if (DuplicateHelpers.HasDuplicateInterface(type))
                DuplicateHelpers.InjectRegistration(serializerClass, type, il);

            // NetworkRegister.Hash(RuntimeTypeHandle handle);
            il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, hashMethod);
            il.Emit(OpCodes.Ret);

            serializerClass.Methods.Add(registerMethod);
        }

        private static void HandleNetworkIdentity(AssemblyDefinition assembly, TypeReference type,
            TypeDefinition serializerClass)
        {
            var registerMethod =
                new MethodDefinition("Register", MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);

            var editorType = assembly.MainModule.GetTypeDefinition<RegisterPackersAttribute>();
            var editorConstructor = editorType.Resolve().Methods.First(m => m.IsConstructor && m.HasParameters)
                .Import(assembly.MainModule);
            var editorAttribute = new CustomAttribute(editorConstructor);
            editorAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.Int32, -1));
            registerMethod.CustomAttributes.Add(editorAttribute);

            registerMethod.Body = new MethodBody(registerMethod)
            {
                InitLocals = true
            };

            var register = registerMethod.Body.GetILProcessor();
            GenerateRegisterMethodForIdentity(type, register);
            serializerClass.Methods.Add(registerMethod);
        }

        private static void HandleNetworkModule(AssemblyDefinition assembly, TypeReference type,
            TypeDefinition serializerClass)
        {
            var registerMethod =
                new MethodDefinition("Register", MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);

            var editorType = assembly.MainModule.GetTypeDefinition<RegisterPackersAttribute>();
            var editorConstructor = editorType.Resolve().Methods.First(m => m.IsConstructor && m.HasParameters)
                .Import(assembly.MainModule);
            var editorAttribute = new CustomAttribute(editorConstructor);
            editorAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.Int32, -1));
            registerMethod.CustomAttributes.Add(editorAttribute);
            registerMethod.Body = new MethodBody(registerMethod)
            {
                InitLocals = true
            };

            var register = registerMethod.Body.GetILProcessor();
            GenerateRegisterMethodForModule(type, register);
            serializerClass.Methods.Add(registerMethod);
        }

        private static void HandleGenerics(AssemblyDefinition assembly, TypeReference type,
            HandledGenericTypes genericT,
            TypeDefinition serializerClass)
        {
            var registerMethod =
                new MethodDefinition("Register", MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);

            var editorType = assembly.MainModule.GetTypeDefinition<RegisterPackersAttribute>();
            var editorConstructor = editorType.Resolve().Methods.First(m => m.IsConstructor && m.HasParameters)
                .Import(assembly.MainModule);
            var editorAttribute = new CustomAttribute(editorConstructor);
            editorAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.TypeSystem.Int32, -1));
            registerMethod.CustomAttributes.Add(editorAttribute);
            registerMethod.Body = new MethodBody(registerMethod)
            {
                InitLocals = true
            };

            var register = registerMethod.Body.GetILProcessor();

            if (DuplicateHelpers.HasDuplicateInterface(type))
                DuplicateHelpers.InjectRegistration(serializerClass, type, register);

            GenerateRegisterMethod(assembly.MainModule, type, register, genericT);
            serializerClass.Methods.Add(registerMethod);
        }

        private static void GenerateRegisterMethodForIdentity(TypeReference type, ILProcessor il)
        {
            var packType = type.Module.GetTypeDefinition(typeof(PackNetworkIdentity)).Import(type.Module);
            var registerMethod = packType.GetMethod("RegisterIdentity", true).Import(type.Module);

            var genericRegisterMethod = new GenericInstanceMethod(registerMethod);
            genericRegisterMethod.GenericArguments.Add(type);

            il.Emit(OpCodes.Call, genericRegisterMethod);
            il.Emit(OpCodes.Ret);
        }

        private static void GenerateRegisterMethodForModule(TypeReference type, ILProcessor il)
        {
            var packType = type.Module.GetTypeDefinition(typeof(PackNetworkModule)).Import(type.Module);
            var registerMethod = packType.GetMethod("RegisterNetworkModule", true).Import(type.Module);

            var genericRegisterMethod = new GenericInstanceMethod(registerMethod);
            genericRegisterMethod.GenericArguments.Add(type);

            il.Emit(OpCodes.Call, genericRegisterMethod);
            il.Emit(OpCodes.Ret);
        }

        private static void GenerateRegisterMethod(ModuleDefinition module, TypeReference type, ILProcessor il,
            HandledGenericTypes handledType)
        {
            var importedType = type.Import(module);
            var packCollectionsType = module.GetTypeDefinition(typeof(PackCollections)).Import(module);

            switch (handledType)
            {
                case HandledGenericTypes.List when importedType is GenericInstanceType listType:

                    var registerListMethod = packCollectionsType.GetMethod("RegisterList", true).Import(module);
                    var genericRegisterListMethod = new GenericInstanceMethod(registerListMethod);
                    genericRegisterListMethod.GenericArguments.Add(listType.GenericArguments[0]);

                    il.Emit(OpCodes.Call, genericRegisterListMethod);

                    break;
                case HandledGenericTypes.Array when importedType is ArrayType arrayType:

                    var registerArrayMethod = packCollectionsType.GetMethod("RegisterArray", true).Import(module);
                    var genericRegisterArrayMethod = new GenericInstanceMethod(registerArrayMethod);
                    genericRegisterArrayMethod.GenericArguments.Add(arrayType.ElementType);

                    il.Emit(OpCodes.Call, genericRegisterArrayMethod);
                    break;
                case HandledGenericTypes.HashSet when importedType is GenericInstanceType hashSetType:

                    var registerHashSetMethod = packCollectionsType.GetMethod("RegisterHashSet", true).Import(module);
                    var genericRegisterHashSetMethod = new GenericInstanceMethod(registerHashSetMethod);
                    genericRegisterHashSetMethod.GenericArguments.Add(hashSetType.GenericArguments[0]);

                    il.Emit(OpCodes.Call, genericRegisterHashSetMethod);
                    break;
                case HandledGenericTypes.Queue when importedType is GenericInstanceType queueType:

                    var registerQueueMethod = packCollectionsType.GetMethod("RegisterQueue", true).Import(module);
                    var genericregisterQueueMethod = new GenericInstanceMethod(registerQueueMethod);
                    genericregisterQueueMethod.GenericArguments.Add(queueType.GenericArguments[0]);

                    il.Emit(OpCodes.Call, genericregisterQueueMethod);
                    break;
                case HandledGenericTypes.Stack when importedType is GenericInstanceType stackType:

                    var registerStackMethod = packCollectionsType.GetMethod("RegisterStack", true).Import(module);
                    var genericRegisterStackMethod = new GenericInstanceMethod(registerStackMethod);
                    genericRegisterStackMethod.GenericArguments.Add(stackType.GenericArguments[0]);

                    il.Emit(OpCodes.Call, genericRegisterStackMethod);
                    break;
                case HandledGenericTypes.DisposableList when importedType is GenericInstanceType stackType:

                    var registerDisposableListMethod =
                        packCollectionsType.GetMethod("RegisterDisposableList", true).Import(module);
                    var genericRegisterDListMethod = new GenericInstanceMethod(registerDisposableListMethod);
                    genericRegisterDListMethod.GenericArguments.Add(stackType.GenericArguments[0]);

                    il.Emit(OpCodes.Call, genericRegisterDListMethod);
                    break;
                case HandledGenericTypes.DisposableArray when importedType is GenericInstanceType stackType:
                    var registerDisposableArrayMethod =
                        packCollectionsType.GetMethod("RegisterDisposableArray", true).Import(module);
                    var genericRegisterDArrayMethod = new GenericInstanceMethod(registerDisposableArrayMethod);
                    genericRegisterDArrayMethod.GenericArguments.Add(stackType.GenericArguments[0]);
                    il.Emit(OpCodes.Call, genericRegisterDArrayMethod);
                    break;
                case HandledGenericTypes.DisposableHashSet when importedType is GenericInstanceType stackType:
                    var registerDisposableHashSetMethod =
                        packCollectionsType.GetMethod("RegisterDisposableHashSet", true).Import(module);
                    var genericRegisterDSetMethod = new GenericInstanceMethod(registerDisposableHashSetMethod);
                    genericRegisterDSetMethod.GenericArguments.Add(stackType.GenericArguments[0]);
                    il.Emit(OpCodes.Call, genericRegisterDSetMethod);
                    break;
                case HandledGenericTypes.DisposableDictionary when importedType is GenericInstanceType stackType:
                    var registerDisposableDicMethod =
                        packCollectionsType.GetMethod("RegisterDisposableDictionary", true).Import(module);
                    var genericRegisterDDSetMethod = new GenericInstanceMethod(registerDisposableDicMethod);
                    genericRegisterDDSetMethod.GenericArguments.Add(stackType.GenericArguments[0]);
                    genericRegisterDDSetMethod.GenericArguments.Add(stackType.GenericArguments[1]);
                    il.Emit(OpCodes.Call, genericRegisterDDSetMethod);
                    break;
                case HandledGenericTypes.Dictionary when importedType is GenericInstanceType dictionaryType:

                    var registerDictionaryMethod =
                        packCollectionsType.GetMethod("RegisterDictionary", true).Import(module);
                    var genericRegisterDictionaryMethod = new GenericInstanceMethod(registerDictionaryMethod);
                    genericRegisterDictionaryMethod.GenericArguments.Add(dictionaryType.GenericArguments[0]);
                    genericRegisterDictionaryMethod.GenericArguments.Add(dictionaryType.GenericArguments[1]);

                    il.Emit(OpCodes.Call, genericRegisterDictionaryMethod);
                    break;
                case HandledGenericTypes.Nullable when importedType is GenericInstanceType nullableType:
                    var registerNullableMethod = packCollectionsType.GetMethod("RegisterNullable", true).Import(module);
                    var genericRegisterNullableMethod = new GenericInstanceMethod(registerNullableMethod);
                    genericRegisterNullableMethod.GenericArguments.Add(nullableType.GenericArguments[0]);

                    il.Emit(OpCodes.Call, genericRegisterNullableMethod);
                    break;
                case HandledGenericTypes.NativeArray when importedType is GenericInstanceType nativeArrayType:
                    var registerNativeArrayMethod = packCollectionsType.GetMethod("RegisterNativeArray", true).Import(module);
                    var genericRegisterNativeArrayMethod = new GenericInstanceMethod(registerNativeArrayMethod);
                    genericRegisterNativeArrayMethod.GenericArguments.Add(nativeArrayType.GenericArguments[0]);
                    il.Emit(OpCodes.Call, genericRegisterNativeArrayMethod);
                    break;
                case HandledGenericTypes.NativeList when importedType is GenericInstanceType nativeListType:
                    var registerNativeListMethod = packCollectionsType.GetMethod("RegisterNativeList", true).Import(module);
                    var genericRegisterNativeListMethod = new GenericInstanceMethod(registerNativeListMethod);
                    genericRegisterNativeListMethod.GenericArguments.Add(nativeListType.GenericArguments[0]);
                    il.Emit(OpCodes.Call, genericRegisterNativeListMethod);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(handledType), handledType, null);
            }

            il.Emit(OpCodes.Ret);
        }

        public static bool HasInterfaceRaw(TypeDefinition def, Type interfaceType)
        {
            bool selfHas = false;
            foreach (var i in def.Interfaces)
            {
                var resolved = i.InterfaceType.Resolve();
                if (resolved != null && resolved.FullName == interfaceType.FullName)
                {
                    selfHas = true;
                    break;
                }
            }

            if (selfHas)
                return true;

            if (def.BaseType == null)
                return false;

            var baseType = def.BaseType.Resolve();
            return baseType != null && HasInterface(baseType, interfaceType);
        }

        public static TypeDefinition HasInterfaceExtra(TypeDefinition def, Type interfaceType)
        {
            bool selfHas = def.Interfaces.Any(i => i.InterfaceType.FullName == interfaceType.FullName);
            if (selfHas)
                return def;

            var baseType = def.BaseType?.Resolve();
            if (baseType != null)
                return HasInterfaceExtra(baseType, interfaceType);

            return null;
        }

        public static bool HasInterface(TypeDefinition def, Type interfaceType)
        {
            bool selfHas = def.Interfaces.Any(i => i.InterfaceType.FullName == interfaceType.FullName);
            if (selfHas)
                return true;

            if (def.BaseType == null)
                return false;

            var baseType = def.BaseType.Resolve();
            return baseType != null && HasInterface(baseType, interfaceType);
        }

        private static void CreateSetterMethod(TypeDefinition parent, FieldDefinition field)
        {
            var name = MakeFullNameValidCSharp($"Purrnet_Set_{field.Name}");

            foreach (var m in parent.Methods)
            {
                if (m.Name == name) return;
            }

            var method = new MethodDefinition(name, MethodAttributes.Public, parent.Module.TypeSystem.Void);
            var valueArg = new ParameterDefinition("value", ParameterAttributes.None, field.FieldType);
            method.Parameters.Add(valueArg);

            var setter = method.Body.GetILProcessor();

            FieldReference fieldRef;

            if (parent.HasGenericParameters)
            {
                // Link the field to the open generic instance
                var resolvedParent = new GenericInstanceType(parent);

                // Populate the generic arguments
                foreach (var genericArg in parent.GenericParameters)
                {
                    resolvedParent.GenericArguments.Add(genericArg);
                }

                // Create the FieldReference with the resolved generic parent
                fieldRef = new FieldReference(field.Name, field.FieldType, resolvedParent);
            }
            else
            {
                // Use the field directly if no generics are involved
                fieldRef = field;
            }

            setter.Emit(OpCodes.Ldarg_0);
            setter.Emit(OpCodes.Ldarg_1);
            setter.Emit(OpCodes.Stfld, fieldRef);

            setter.Emit(OpCodes.Ret);

            parent.Methods.Add(method);
        }

        private static void CreateGetterMethod(TypeDefinition parent, FieldDefinition field)
        {
            var name = MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}");

            foreach (var m in parent.Methods)
            {
                if (m.Name == name) return;
            }

            var method = new MethodDefinition(MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}"),
                MethodAttributes.Public, field.FieldType);
            var getter = method.Body.GetILProcessor();

            FieldReference fieldRef;

            if (parent.HasGenericParameters)
            {
                // Link the field to the open generic instance
                var resolvedParent = new GenericInstanceType(parent);

                // Populate the generic arguments
                foreach (var genericArg in parent.GenericParameters)
                {
                    resolvedParent.GenericArguments.Add(genericArg);
                }

                // Create the FieldReference with the resolved generic parent
                fieldRef = new FieldReference(field.Name, field.FieldType, resolvedParent);
            }
            else
            {
                // Use the field directly if no generics are involved
                fieldRef = field;
            }

            getter.Emit(OpCodes.Ldarg_0); // Load "this"
            getter.Emit(OpCodes.Ldfld, fieldRef); // Load field in the correct generic context
            getter.Emit(OpCodes.Ret); // Return the field value

            parent.Methods.Add(method);
        }

        public static bool DoesTypeHaveAttribute(TypeDefinition type, Type attribute)
        {
            while (true)
            {
                if (type == null)
                    return false;

                if (type.CustomAttributes.Any(a => a.AttributeType.FullName == attribute.FullName))
                    return true;

                if (type.BaseType != null)
                {
                    type = type.BaseType.Resolve();
                    continue;
                }

                break;
            }

            return false;
        }

        public static bool DoesTypeHaveDontPackAttribute(TypeDefinition type)
        {
            return DoesTypeHaveAttribute(type, typeof(DontPackAttribute));
        }

        private static void GenerateMethod(
            bool isWriting, MethodDefinition method, MethodReference serialize, MethodReference serializeDirect, TypeReference typeRef, ILProcessor il,
            ModuleDefinition mainmodule, ParameterDefinition valueArg)
        {
            var bitPackerType = mainmodule.GetTypeDefinition(typeof(BitPacker)).Import(mainmodule);
            var packerType = mainmodule.GetTypeDefinition(typeof(Packer<>)).Import(mainmodule);
            var type = typeRef.Resolve();

            var writeIsNull = bitPackerType.GetMethod("WriteIsNull", true).Import(mainmodule);
            var readIsNull = bitPackerType.GetMethod("ReadIsNull", true).Import(mainmodule);
            bool isClass = !type.IsValueType;

            var ret = il.Create(OpCodes.Ret);

            var standaloneType = HasInterfaceExtra(type, typeof(IStandaloneSerializable));

            if (standaloneType != null && standaloneType.FullName != type.FullName)
            {
                // call Packer<>.Write for the standalone type
                CallForStandalone(isWriting, method, serializeDirect, il, mainmodule, packerType, standaloneType, type);
                il.Append(ret);
                return;
            }

            if (isClass)
            {
                // write null check
                if (isWriting)
                {
                    var genericIsNull = new GenericInstanceMethod(writeIsNull);
                    genericIsNull.GenericArguments.Add(typeRef);

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, genericIsNull);
                }
                else
                {
                    var genericIsNull = new GenericInstanceMethod(readIsNull);
                    genericIsNull.GenericArguments.Add(typeRef);

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, genericIsNull);
                }

                // if returned false, just return
                il.Emit(OpCodes.Brfalse, ret);
            }

            CreateGettersAndSetters(isWriting, type);

            if (type.IsEnum)
            {
                HandleEnums(isWriting, method, serialize, type, il, packerType, mainmodule);
                il.Append(ret);
                return;
            }

            if (HasInterface(type, typeof(IPacked)))
            {
                HandleIData(isWriting, type, il, mainmodule, valueArg);
                il.Append(ret);
                return;
            }

            if (HasInterface(type, typeof(IPackedSimple)))
            {
                HandleIDataSimple(isWriting, type, il, mainmodule, valueArg);
                il.Append(ret);
                return;
            }

            if (isClass && type.BaseType != null && type.BaseType.FullName != typeof(object).FullName)
            {
                var baseType = ResolveGenericTypeRef(type.BaseType, typeRef);

                if (baseType is { IsValueType: false })
                {
                    var genericM = CreateGenericMethod(packerType, baseType, serializeDirect, mainmodule);
                    var variable = new VariableDefinition(baseType);

                    if (isWriting)
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                    }
                    else
                    {
                        // variable = this
                        method.Body.Variables.Add(variable);

                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldind_Ref);
                        il.Emit(OpCodes.Stloc, variable);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldloca, variable);
                    }

                    il.Emit(OpCodes.Call, genericM);

                    if (!isWriting)
                    {
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldloc, variable);
                        il.Emit(OpCodes.Castclass, type);
                        il.Emit(OpCodes.Stind_Ref);
                    }
                }
            }

            foreach (var field in type.Fields)
            {
                if (field.IsStatic)
                    continue;

                bool isDelegate = PostProcessor.InheritsFrom(field.FieldType.Resolve(), typeof(Delegate).FullName);

                if (isDelegate)
                    continue;

                var ignore = ShouldIgnoreField(field);

                if (ignore)
                    continue;

                var fieldType = ResolveGenericFieldType(field, typeRef);
                bool useNativeCalli = fieldType.Resolve()?.IsUnmanaged() == true;
                MethodReference genericM = useNativeCalli ? null : CreateGenericMethod(packerType, fieldType, serialize, mainmodule);

                // make field publicp
                if (!field.IsPublic)
                {
                    if (isWriting)
                    {
                        var getterName = MakeFullNameValidCSharp($"Purrnet_Get_{field.Name}");
                        var getter = new MethodReference(getterName, field.FieldType, typeRef)
                        {
                            HasThis = true
                        };

                        if (typeRef is GenericInstanceType genericInstanceType)
                        {
                            var copy = new MethodReference(getter.Name, getter.ReturnType, genericInstanceType)
                            {
                                HasThis = getter.HasThis
                            };

                            foreach (var param in getter.Parameters)
                                copy.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                                    param.ParameterType));

                            foreach (var genericParam in getter.GenericParameters)
                                copy.GenericParameters.Add(new GenericParameter(genericParam.Name, copy));

                            getter = copy;
                        }

                        il.Emit(OpCodes.Ldarg_0);

                        if (isClass)
                            il.Emit(OpCodes.Ldarg_1);
                        else
                            il.Emit(OpCodes.Ldarga_S, valueArg);

                        il.Emit(OpCodes.Call, getter);

                        if (useNativeCalli)
                            EmitNativePackerCalli(il, mainmodule, fieldType, bitPackerType, true);
                        else
                            il.Emit(OpCodes.Call, genericM);
                    }
                    else
                    {
                        var variable = new VariableDefinition(fieldType);
                        method.Body.Variables.Add(variable);

                        var setterName = MakeFullNameValidCSharp($"Purrnet_Set_{field.Name}");
                        var setter = new MethodReference(setterName, type.Module.TypeSystem.Void, typeRef)
                        {
                            HasThis = true
                        };

                        setter.Parameters.Add(
                            new ParameterDefinition("value", ParameterAttributes.None, field.FieldType));

                        if (typeRef is GenericInstanceType genericInstanceType)
                        {
                            var copy = new MethodReference(setter.Name, setter.ReturnType, genericInstanceType)
                            {
                                HasThis = setter.HasThis
                            };

                            foreach (var param in setter.Parameters)
                                copy.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                                    param.ParameterType));

                            foreach (var genericParam in setter.GenericParameters)
                                copy.GenericParameters.Add(new GenericParameter(genericParam.Name, copy));

                            setter = copy;
                        }

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldloca, variable);

                        if (useNativeCalli)
                            EmitNativePackerCalli(il, mainmodule, fieldType, bitPackerType, false);
                        else
                            il.Emit(OpCodes.Call, genericM);

                        il.Emit(OpCodes.Ldarg_1);
                        if (isClass) il.Emit(OpCodes.Ldind_Ref);

                        il.Emit(OpCodes.Ldloc, variable);
                        il.Emit(OpCodes.Call, setter);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    if (isClass && !isWriting) il.Emit(OpCodes.Ldind_Ref);

                    var fieldRef = new FieldReference(field.Name, field.FieldType, typeRef).Import(mainmodule);

                    il.Emit(isWriting ? OpCodes.Ldfld : OpCodes.Ldflda, fieldRef);

                    if (useNativeCalli)
                        EmitNativePackerCalli(il, mainmodule, fieldType, bitPackerType, isWriting);
                    else
                        il.Emit(OpCodes.Call, genericM);
                }
            }

            /*il.Emit(OpCodes.Ldarg_S, valueArg);
            if (isClass) il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, readData);*/

            il.Append(ret);
        }

        public static void CreateGettersAndSetters(bool isWriting, TypeDefinition type)
        {
            for (var i = 0; i < type.Fields.Count; i++)
            {
                var field = type.Fields[i];
                if (field.IsStatic)
                    continue;

                bool isDelegate = PostProcessor.InheritsFrom(field.FieldType.Resolve(), typeof(Delegate).FullName);

                if (isDelegate)
                    continue;

                var ignore = ShouldIgnoreField(field);

                if (ignore)
                    continue;

                if (!field.IsPublic)
                {
                    if (isWriting)
                        CreateGetterMethod(type, field);
                    else CreateSetterMethod(type, field);
                }
            }
        }

        private static void CallForStandalone(bool isWriting, MethodDefinition method, MethodReference serializeDirect,
            ILProcessor il, ModuleDefinition mainmodule, TypeReference packerType, TypeDefinition standaloneType,
            TypeDefinition type)
        {
            var genericM = CreateGenericMethod(packerType, standaloneType, serializeDirect, mainmodule);
            var variable = new VariableDefinition(standaloneType);

            if (isWriting)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
            }
            else
            {
                // variable = this
                method.Body.Variables.Add(variable);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Stloc, variable);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloca, variable);
            }

            il.Emit(OpCodes.Call, genericM);

            if (!isWriting)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc, variable);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stind_Ref);
            }
        }

        public static bool ShouldIgnoreField(FieldDefinition field)
        {
            bool ignore = field.CustomAttributes.Any(a =>
                a.AttributeType.FullName == typeof(DontPackAttribute).FullName) || DoesTypeHaveDontPackAttribute(field.FieldType.Resolve());
            return ignore;
        }

        public static TypeReference ResolveGenericTypeRef(TypeReference fieldType, TypeReference declaringType)
        {
            if (declaringType is GenericInstanceType genericDeclaringType)
            {
                return SubstituteDeclaringTypeGenerics(fieldType, genericDeclaringType);
            }

            return fieldType;
        }

        public static TypeReference ResolveGenericFieldType(FieldDefinition field, TypeReference declaringType)
        {
            var fieldType = field.FieldType;

            if (declaringType is GenericInstanceType genericDeclaringType)
            {
                return SubstituteDeclaringTypeGenerics(fieldType, genericDeclaringType);
            }

            return fieldType;
        }

        private static TypeReference SubstituteDeclaringTypeGenerics(TypeReference type, GenericInstanceType declaringGeneric)
        {
            // Direct mapping for generic parameter T from the declaring type
            if (type.IsGenericParameter)
            {
                var gp = (GenericParameter)type;
                return declaringGeneric.GenericArguments[gp.Position];
            }

            // Rebuild nested generic instances recursively: e.g., List<Blah<T>>, Dictionary<K, Blah<T>>
            if (type is GenericInstanceType gi)
            {
                var rebuilt = new GenericInstanceType(gi.ElementType);
                for (int i = 0; i < gi.GenericArguments.Count; i++)
                {
                    var arg = gi.GenericArguments[i];
                    rebuilt.GenericArguments.Add(SubstituteDeclaringTypeGenerics(arg, declaringGeneric));
                }
                return rebuilt;
            }

            // Support arrays with generic element types: T[] or Blah<T>[]
            if (type is ArrayType arrayType)
            {
                var substitutedElement = SubstituteDeclaringTypeGenerics(arrayType.ElementType, declaringGeneric);
                return new ArrayType(substitutedElement, arrayType.Rank);
            }

            // Non-generic type: return as-is
            return type;
        }

        private static void HandleIData(bool isWriting, TypeDefinition type,
            ILProcessor il, ModuleDefinition mainmodule, ParameterDefinition valueArg)
        {
            bool isClass = !type.IsValueType;

            if (isWriting)
            {
                var writeData = type.GetMethod("Write").Import(mainmodule);
                il.Emit(isClass ? OpCodes.Ldarg : OpCodes.Ldarga_S, valueArg);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, writeData);
            }
            else
            {
                var readData = type.GetMethod("Read").Import(mainmodule);
                il.Emit(OpCodes.Ldarg_S, valueArg);
                if (isClass) il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, readData);
            }

            il.Emit(OpCodes.Ret);
        }

        private static void HandleIDataSimple(bool isWriting, TypeDefinition type,
            ILProcessor il, ModuleDefinition mainmodule, ParameterDefinition valueArg)
        {
            bool isClass = !type.IsValueType;
            if (isWriting)
            {
                var writeData = type.GetMethod("Serialize").Import(mainmodule);
                il.Emit(isClass ? OpCodes.Ldarg : OpCodes.Ldarga_S, valueArg);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, writeData);
            }
            else
            {
                var readData = type.GetMethod("Serialize").Import(mainmodule);
                il.Emit(OpCodes.Ldarg_S, valueArg);
                if (isClass) il.Emit(OpCodes.Ldind_Ref);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, readData);
            }

            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// For unmanaged types: emits ldsfld NativePacker(T).WriteFunc/ReadFunc + calli.
        /// Args must already be on the stack before calling this.
        /// </summary>
        static void EmitNativePackerCalli(ILProcessor il, ModuleDefinition module, TypeReference fieldType, TypeReference bitStreamType, bool isWrite)
        {
            var nativePackerDef = module.GetTypeDefinition(typeof(NativePacker<>));
            var fieldName = isWrite ? "WriteFunc" : "ReadFunc";
            var genericParam = nativePackerDef.GenericParameters[0]; // !0

            // Construct FunctionPointerType matching the field signature:
            // Write: delegate*<BitPacker, !0, void>
            // Read:  delegate*<BitPacker, ref !0, void>
            var funcPtrType = new FunctionPointerType();
            funcPtrType.ReturnType = module.TypeSystem.Void;
            funcPtrType.Parameters.Add(new ParameterDefinition(bitStreamType));
            funcPtrType.Parameters.Add(isWrite
                ? new ParameterDefinition(genericParam)
                : new ParameterDefinition(new ByReferenceType(genericParam)));

            var genericInstance = new GenericInstanceType(nativePackerDef.Import(module));
            genericInstance.GenericArguments.Add(fieldType);

            var fieldRef = new FieldReference(fieldName, funcPtrType, genericInstance);
            il.Emit(OpCodes.Ldsfld, fieldRef);

            // calli uses concrete types
            var callSite = new CallSite(module.TypeSystem.Void);
            callSite.Parameters.Add(new ParameterDefinition(bitStreamType));
            callSite.Parameters.Add(isWrite
                ? new ParameterDefinition(fieldType)
                : new ParameterDefinition(new ByReferenceType(fieldType)));
            il.Emit(OpCodes.Calli, callSite);
        }

        private static void HandleEnums(bool isWriting, MethodDefinition method, MethodReference serialize,
            TypeDefinition type, ILProcessor il, TypeReference packerType, ModuleDefinition mainmodule)
        {
            var underlyingType = type.GetField("value__").FieldType;
            bool useNativeCalli = underlyingType.Resolve()?.IsUnmanaged() == true;
            var enumWriteMethod = useNativeCalli ? null : CreateGenericMethod(packerType, underlyingType, serialize, mainmodule);
            var bitPackerType = mainmodule.GetTypeDefinition(typeof(BitPacker)).Import(mainmodule);

            var tmpVar = new VariableDefinition(underlyingType);

            if (!isWriting)
            {
                method.Body.Variables.Add(tmpVar);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc_0);
            }

            il.Emit(OpCodes.Ldarg_0);

            // load the address of the field
            if (isWriting)
            {
                il.Emit(OpCodes.Ldarg_1);

                if (useNativeCalli)
                    EmitNativePackerCalli(il, mainmodule, underlyingType, bitPackerType, true);
                else
                    il.Emit(OpCodes.Call, enumWriteMethod);
            }
            else
            {
                il.Emit(OpCodes.Ldloca, tmpVar);

                if (useNativeCalli)
                    EmitNativePackerCalli(il, mainmodule, underlyingType, bitPackerType, false);
                else
                    il.Emit(OpCodes.Call, enumWriteMethod);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc_0);
                EmitStindForEnum(il, type);
            }

            il.Emit(OpCodes.Ret);
        }

        public static MethodReference CreateGenericMethod(TypeReference packerType, TypeReference type,
            MethodReference writeMethod, ModuleDefinition mainmodule)
        {
            var genericPackerType = new GenericInstanceType(packerType);
            genericPackerType.GenericArguments.Add(type);

            var genericWriteMethod = new MethodReference(writeMethod.Name, writeMethod.ReturnType,
                genericPackerType.Import(mainmodule))
            {
                HasThis = writeMethod.HasThis
            };

            foreach (var param in writeMethod.Parameters)
                genericWriteMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                    param.ParameterType));
            return genericWriteMethod.Import(mainmodule);
        }

        private static bool IsGeneric(TypeReference typeDef, out HandledGenericTypes type)
        {
            if (typeDef.IsArray)
            {
                type = HandledGenericTypes.Array;
                return true;
            }

            if (IsGeneric(typeDef, typeof(DisposableList<>)))
            {
                type = HandledGenericTypes.DisposableList;
                return true;
            }

            if (IsGenericUnityCollections(typeDef, "Unity.Collections.NativeArray`1"))
            {
                type = HandledGenericTypes.NativeArray;
                return true;
            }

            if (IsGenericUnityCollections(typeDef, "Unity.Collections.NativeList`1"))
            {
                type = HandledGenericTypes.NativeList;
                return true;
            }

            if (IsGeneric(typeDef, typeof(DisposableArray<>)))
            {
                type = HandledGenericTypes.DisposableArray;
                return true;
            }


            if (IsGeneric(typeDef, typeof(DisposableHashSet<>)))
            {
                type = HandledGenericTypes.DisposableHashSet;
                return true;
            }

            if (IsGeneric(typeDef, typeof(DisposableDictionary<,>)))
            {
                type = HandledGenericTypes.DisposableDictionary;
                return true;
            }

            if (IsGeneric(typeDef, typeof(List<>)))
            {
                type = HandledGenericTypes.List;
                return true;
            }

            if (IsGeneric(typeDef, typeof(Queue<>)))
            {
                type = HandledGenericTypes.Queue;
                return true;
            }

            if (IsGeneric(typeDef, typeof(Stack<>)))
            {
                type = HandledGenericTypes.Stack;
                return true;
            }

            if (IsGeneric(typeDef, typeof(HashSet<>)))
            {
                type = HandledGenericTypes.HashSet;
                return true;
            }

            if (IsGeneric(typeDef, typeof(Dictionary<,>)))
            {
                type = HandledGenericTypes.Dictionary;
                return true;
            }

            if (IsGeneric(typeDef, typeof(Nullable<>)))
            {
                type = HandledGenericTypes.Nullable;
                return true;
            }

            type = HandledGenericTypes.None;
            return false;
        }

        private static bool IsGeneric(TypeReference typeDef, Type type)
        {
            // Ensure method has a generic return type
            if (typeDef is GenericInstanceType genericReturnType)
            {
                // Resolve the element type to compare against Task<>
                var resolvedType = genericReturnType.ElementType.Resolve();

                // Check if the resolved type matches Task<>
                return resolvedType != null && resolvedType.FullName == type.FullName;
            }

            return false;
        }

        private static bool IsGenericUnityCollections(TypeReference typeDef, string genericTypeFullName)
        {
            if (typeDef is GenericInstanceType genericInstance)
            {
                var resolved = genericInstance.ElementType.Resolve();
                return resolved != null && resolved.FullName == genericTypeFullName;
            }

            return false;
        }

        private static TypeReference GetEnumUnderlyingType(TypeDefinition typeDef)
        {
            if (!typeDef.IsEnum)
                return null;

            var valueField = typeDef.Fields.FirstOrDefault(f => f.Name == "value__");
            return valueField?.FieldType;
        }

        public static void EmitStindForEnum(ILProcessor il, TypeDefinition enumType)
        {
            if (!enumType.IsEnum)
            {
                throw new ArgumentException($"{enumType.FullName} is not an enum.");
            }

            // Get the underlying type of the enum
            var underlyingType = GetEnumUnderlyingType(enumType);

            if (underlyingType == null)
            {
                throw new InvalidOperationException(
                    $"Unable to determine the underlying type of the enum {enumType.FullName}.");
            }

            // Emit the appropriate Stind opcode based on the underlying type
            switch (underlyingType.FullName)
            {
                case "System.Byte":
                case "System.SByte":
                    il.Emit(OpCodes.Stind_I1); // Store value for 1-byte types
                    break;

                case "System.Int16":
                case "System.UInt16":
                    il.Emit(OpCodes.Stind_I2); // Store value for 2-byte types
                    break;

                case "System.Int32":
                case "System.UInt32":
                    il.Emit(OpCodes.Stind_I4); // Store value for 4-byte types
                    break;

                case "System.Int64":
                case "System.UInt64":
                    il.Emit(OpCodes.Stind_I8); // Store value for 8-byte types
                    break;

                default:
                    throw new NotSupportedException($"Unsupported enum underlying type: {underlyingType.FullName}");
            }
        }
    }
}

#endif
