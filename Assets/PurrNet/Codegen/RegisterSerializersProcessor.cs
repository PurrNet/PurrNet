#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;
using PurrNet.Pooling;

namespace PurrNet.Codegen
{
    public static class RegisterSerializersProcessor
    {
        public static bool IsDeltaWriteMethod(MethodDefinition method, out TypeReference type)
        {
            type = null;

            if (method.ReturnType.MetadataType != MetadataType.Boolean)
                return false;

            if (method.Parameters.Count != 3)
                return false;

            if (method.Parameters[0].ParameterType.FullName != typeof(BitPacker).FullName)
                return false;

            if (method.Parameters[1].ParameterType.IsByReference)
                return false;

            if (method.Parameters[2].ParameterType.IsByReference)
                return false;

            if (method.Parameters[2].ParameterType != method.Parameters[1].ParameterType)
                return false;

            type = method.Parameters[1].ParameterType;
            return true;
        }

        public static bool IsDeltaReadMethod(MethodDefinition method, out TypeReference type)
        {
            type = null;

            if (method.ReturnType.MetadataType != MetadataType.Void)
                return false;

            if (method.Parameters.Count != 3)
                return false;

            if (method.Parameters[0].ParameterType.FullName != typeof(BitPacker).FullName)
                return false;

            if (method.Parameters[1].ParameterType.IsByReference)
                return false;

            if (!method.Parameters[2].ParameterType.IsByReference)
                return false;

            type = method.Parameters[1].ParameterType;

            return true;
        }

        public static bool IsWriteMethod(MethodDefinition method, out TypeReference type)
        {
            type = null;

            if (method.ReturnType.MetadataType != MetadataType.Void)
                return false;

            if (method.Parameters.Count != 2)
                return false;

            if (method.Parameters[0].ParameterType.FullName != typeof(BitPacker).FullName)
                return false;

            if (method.Parameters[1].ParameterType.IsByReference)
                return false;

            type = method.Parameters[1].ParameterType;
            return true;
        }

        public static bool IsReadMethod(MethodDefinition method, out TypeReference type)
        {
            type = null;

            if (method.ReturnType.MetadataType != MetadataType.Void)
                return false;

            if (method.Parameters.Count != 2)
                return false;

            if (method.Parameters[0].ParameterType.FullName != typeof(BitPacker).FullName)
                return false;

            if (!method.Parameters[1].ParameterType.IsByReference)
                return false;

            type = method.Parameters[1].ParameterType;

            if (type is ByReferenceType byRefType)
                type = byRefType.ElementType;

            return true;
        }

        struct PackType
        {
            public bool isDelta;
            public TypeReference type;
            public MethodDefinition method;
        }

        public static void EnsureCoreClrAccessible(TypeReference typeRef, ModuleDefinition module)
        {
            switch (typeRef)
            {
                case null: return;
                case GenericInstanceType git:
                    EnsureCoreClrAccessible(git.ElementType, module);
                    foreach (var arg in git.GenericArguments)
                        EnsureCoreClrAccessible(arg, module);
                    return;
                case ByReferenceType byRef: EnsureCoreClrAccessible(byRef.ElementType, module); return;
                case ArrayType arr:         EnsureCoreClrAccessible(arr.ElementType, module);   return;
                case PointerType ptr:       EnsureCoreClrAccessible(ptr.ElementType, module);   return;
            }

            var resolved = typeRef.Resolve();

            while (resolved != null && resolved.IsNested && resolved.Module == module)
            {
                if (!resolved.IsNestedPublic)
                    resolved.IsNestedPublic = true;
                resolved = resolved.DeclaringType?.Resolve();
            }
        }

        public static void HandleType(TypeReference actualType, ModuleDefinition module, TypeDefinition type,
            HashSet<TypeReference> toIgnoreForDelta, HashSet<TypeReference> toIgnoreForSerialization)
        {
            if (type.FullName == typeof(Packer).FullName)
                return;

            if (type.FullName == typeof(Packer<>).FullName)
                return;

            bool isStatic = type.IsAbstract && type.IsSealed;

            if (!isStatic)
                return;

            using var writeTypes = DisposableList<PackType>.Create(32);
            using var readTypes = DisposableList<PackType>.Create(32);

            var mcount = type.Methods.Count;
            for (var i = 0; i < mcount; i++)
            {
                var method = type.Methods[i];
                if (method.HasGenericParameters || method.ContainsGenericParameter)
                    continue;

                if (!method.IsStatic)
                    break;

                if (method.HasGenericParameters)
                    continue;

                if (IsWriteMethod(method, out var writeType))
                {
                    if (writeType == null)
                        throw new Exception("WriteType is null");

                    writeTypes.Add(new PackType
                    {
                        type = writeType,
                        method = method
                    });

                    toIgnoreForSerialization?.Add(writeType);
                }
                else if (IsReadMethod(method, out var readType))
                {
                    if (readType == null)
                        throw new Exception("ReadType is null");

                    readTypes.Add(new PackType
                    {
                        type = readType,
                        method = method
                    });

                    toIgnoreForSerialization?.Add(readType);
                }
                else if (IsDeltaWriteMethod(method, out var deltaWriteType))
                {
                    if (deltaWriteType == null)
                        throw new Exception("DeltaWriteType is null");

                    writeTypes.Add(new PackType
                    {
                        isDelta = true,
                        type = deltaWriteType,
                        method = method
                    });

                    toIgnoreForDelta?.Add(deltaWriteType);
                    GenerateDeltaSerializersProcessor.CacheDeltaWrite(deltaWriteType, method);
                }
                else if (IsDeltaReadMethod(method, out var deltaReadType))
                {
                    if (deltaReadType == null)
                        throw new Exception("DeltaReadType is null");

                    readTypes.Add(new PackType
                    {
                        isDelta = true,
                        type = deltaReadType,
                        method = method
                    });

                    toIgnoreForDelta?.Add(deltaReadType);
                    GenerateDeltaSerializersProcessor.CacheDeltaRead(deltaReadType, method);
                }
            }

            bool hasIDuplicate = DuplicateHelpers.HasDuplicateInterface(actualType);
            bool hasIPurrEquatable = EquatableHelpers.HasEquatableInterface(actualType);
            if (writeTypes.Count == 0 && readTypes.Count == 0 && !hasIDuplicate && !hasIPurrEquatable)
                return;

            var writeFuncDelegate = module.GetTypeDefinition(typeof(WriteFunc<>)).Import(module);
            var readFuncDelegate = module.GetTypeDefinition(typeof(ReadFunc<>)).Import(module);

            var deltaWriteFuncDelegate = module.GetTypeDefinition(typeof(DeltaWriteFunc<>)).Import(module);
            var deltaReadFuncDelegate = module.GetTypeDefinition(typeof(DeltaReadFunc<>)).Import(module);

            var registerMethod = new MethodDefinition("Register_Type_Generated_PurrNet", MethodAttributes.Static,
                module.TypeSystem.Void);

            bool hasGeneratedByILAttribute = false;

            foreach (var attribute in type.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == typeof(GeneratedByILAttribute).FullName)
                {
                    hasGeneratedByILAttribute = true;
                    break;
                }
            }

            var editorType = module.GetTypeDefinition<RegisterPackersAttribute>().Import(module);
            var editorConstructor = editorType.Resolve().Methods.First(m => m.IsConstructor && m.HasParameters)
                .Import(module);
            var editorAttribute = new CustomAttribute(editorConstructor);
            editorAttribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Int32, hasGeneratedByILAttribute ? -1 : 0));
            registerMethod.CustomAttributes.Add(editorAttribute);
            registerMethod.Body.InitLocals = true;

            type.Methods.Add(registerMethod);

            var il = registerMethod.Body.GetILProcessor();

            for (int i = 0; i < writeTypes.Count; i++)
            {
                var writeType = writeTypes[i];
                EnsureCoreClrAccessible(writeType.type, module);
                var writeMethod = writeType.method.Import(module);
                var resolved = writeMethod.Resolve();
                resolved.IsPublic = true;
                resolved.AggressiveInlining = true;

                var nonDeltaPackerType = module.GetTypeDefinition(typeof(Packer<>)).Import(module);
                var deltaPackerType = module.GetTypeDefinition(typeof(DeltaPacker<>)).Import(module);

                var actualPackerType = writeType.isDelta ? deltaPackerType : nonDeltaPackerType;

                var genPackerType = new GenericInstanceType(actualPackerType);
                genPackerType.GenericArguments.Add(writeType.type.Import(module));

                var writeDelType = writeType.isDelta ? deltaWriteFuncDelegate : writeFuncDelegate;
                var writeFuncGeneric = new GenericInstanceType(writeDelType);
                writeFuncGeneric.GenericArguments.Add(writeType.type.Import(module));

                var delegateConstructor = writeDelType.Resolve()
                    .Methods.First(m => m.IsConstructor && m.HasParameters);
                var delegateConstructorRef = new MethodReference(delegateConstructor.Name,
                    delegateConstructor.ReturnType, writeFuncGeneric)
                {
                    HasThis = delegateConstructor.HasThis,
                    ExplicitThis = delegateConstructor.ExplicitThis,
                    CallingConvention = delegateConstructor.CallingConvention
                };

                foreach (var param in delegateConstructor.Parameters)
                    delegateConstructorRef.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                        param.ParameterType));

                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, writeMethod);
                il.Emit(OpCodes.Newobj, delegateConstructorRef);

                var write = genPackerType.GetMethod("RegisterWriter", false).Import(module);
                var genericWrite = new MethodReference("RegisterWriter", module.TypeSystem.Void, genPackerType)
                {
                    HasThis = write.HasThis,
                    ExplicitThis = write.ExplicitThis,
                    CallingConvention = write.CallingConvention
                };

                foreach (var param in write.Parameters)
                    genericWrite.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                        param.ParameterType));

                if (writeType.isDelta)
                    il.Emit(OpCodes.Dup);

                il.Emit(OpCodes.Call, genericWrite);

                if (writeType.isDelta)
                {
                    var nativeDeltaPackerType = module.GetTypeDefinition(typeof(NativeDeltaPacker<>)).Import(module);
                    var genNativePackerType = new GenericInstanceType(nativeDeltaPackerType);
                    genNativePackerType.GenericArguments.Add(writeType.type.Import(module));

                    var nativeWrite = genNativePackerType.GetMethod("RegisterWriter", false).Import(module);
                    var genericNativeWrite = new MethodReference("RegisterWriter", module.TypeSystem.Void, genNativePackerType)
                    {
                        HasThis = nativeWrite.HasThis,
                        ExplicitThis = nativeWrite.ExplicitThis,
                        CallingConvention = nativeWrite.CallingConvention
                    };

                    foreach (var param in nativeWrite.Parameters)
                        genericNativeWrite.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                            param.ParameterType));

                    il.Emit(OpCodes.Call, genericNativeWrite);
                }
            }

            for (int i = 0; i < readTypes.Count; i++)
            {
                var readType = readTypes[i];
                var readMethod = readType.method.Import(module);
                var resolved = readMethod.Resolve();

                resolved.IsPublic = true;
                resolved.AggressiveInlining = true;

                // Create a GenericInstanceMethod for Packer.RegisterReader<T>

                var typeArgument = readType.type.Import(module);

                // If the type is a ByReferenceType (e.g., ref int), get the base type
                if (typeArgument is ByReferenceType byRefType)
                {
                    typeArgument = byRefType.ElementType; // Use the base type (e.g., int from ref int)
                }

                EnsureCoreClrAccessible(typeArgument, module);

                var packerType = module.GetTypeDefinition(typeof(Packer<>)).Import(module);
                var deltaPackerType = module.GetTypeDefinition(typeof(DeltaPacker<>)).Import(module);
                var actualPackerType = readType.isDelta ? deltaPackerType : packerType;

                var genPackerType = new GenericInstanceType(actualPackerType);
                genPackerType.GenericArguments.Add(typeArgument);

                // Create the generic delegate type (ReadFunc<T>)
                var readDelType = readType.isDelta ? deltaReadFuncDelegate : readFuncDelegate;
                var readFuncGeneric = new GenericInstanceType(readDelType);
                readFuncGeneric.GenericArguments.Add(typeArgument);

                // Resolve the constructor of the generic delegate (ReadFunc<T>(object, IntPtr))
                var delegateConstructor = readDelType.Resolve()
                    .Methods.First(m => m.IsConstructor && m.HasParameters);

                // Construct the delegate constructor reference
                var delegateConstructorRef = new MethodReference(delegateConstructor.Name,
                    delegateConstructor.ReturnType, readFuncGeneric)
                {
                    HasThis = delegateConstructor.HasThis,
                    ExplicitThis = delegateConstructor.ExplicitThis,
                    CallingConvention = delegateConstructor.CallingConvention
                };

                // Add parameters to the delegate constructor reference
                foreach (var param in delegateConstructor.Parameters)
                {
                    delegateConstructorRef.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                        param.ParameterType));
                }

                // Generate IL for: Packer.RegisterReader<int>(Read);
                il.Emit(OpCodes.Ldnull); // Load 'null' for the target instance (static method)
                il.Emit(OpCodes.Ldftn, readMethod); // Load the method pointer
                il.Emit(OpCodes.Newobj, delegateConstructorRef); // Create the delegate instance

                var read = genPackerType.GetMethod("RegisterReader", false).Import(module);
                var genericread = new MethodReference("RegisterReader", module.TypeSystem.Void, genPackerType)
                {
                    HasThis = read.HasThis,
                    ExplicitThis = read.ExplicitThis,
                    CallingConvention = read.CallingConvention
                };

                foreach (var param in read.Parameters)
                    genericread.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                        param.ParameterType));

                if (readType.isDelta)
                    il.Emit(OpCodes.Dup);

                il.Emit(OpCodes.Call, genericread);

                if (readType.isDelta)
                {
                    var nativeDeltaPackerType = module.GetTypeDefinition(typeof(NativeDeltaPacker<>)).Import(module);
                    var genNativePackerType = new GenericInstanceType(nativeDeltaPackerType);
                    genNativePackerType.GenericArguments.Add(typeArgument);

                    var nativeRead = genNativePackerType.GetMethod("RegisterReader", false).Import(module);
                    var genericNativeRead = new MethodReference("RegisterReader", module.TypeSystem.Void, genNativePackerType)
                    {
                        HasThis = nativeRead.HasThis,
                        ExplicitThis = nativeRead.ExplicitThis,
                        CallingConvention = nativeRead.CallingConvention
                    };

                    foreach (var param in nativeRead.Parameters)
                        genericNativeRead.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                            param.ParameterType));

                    il.Emit(OpCodes.Call, genericNativeRead);
                }
            }

            if (hasIDuplicate)
                DuplicateHelpers.InjectRegistration(type, actualType, il);

            if (hasIPurrEquatable)
                EquatableHelpers.InjectRegistration(type, actualType, il);

            il.Emit(OpCodes.Ret);
        }
    }
}
#endif
