#if UNITY_MONO_CECIL
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Codegen
{
    public static class GenerateIEquatableInterface
    {
        private static bool SameType(TypeReference a, TypeReference b)
        {
            if (a == null || b == null) return false;
            var ad = a.Resolve();
            var bd = b.Resolve();
            if (ad != null && bd != null)
                return ad.FullName == bd.FullName;
            return a.FullName == b.FullName;
        }

        private static TypeReference MakeSelfRef(TypeDefinition type)
        {
            if (!type.HasGenericParameters) return type;
            var gi = new GenericInstanceType(type);
            foreach (var gp in type.GenericParameters) gi.GenericArguments.Add(gp);
            return gi;
        }

        private static bool HasIEquatableT(TypeDefinition def)
        {
            var cur = def;
            var self = MakeSelfRef(def);

            while (cur != null)
            {
                foreach (var iface in cur.Interfaces)
                {
                    var ifaceType = iface.InterfaceType;
                    var ifaceDef = ifaceType.Resolve();
                    if (ifaceDef == null) continue;

                    if (ifaceDef.Namespace == "System" &&
                        ifaceDef.Name == "IEquatable`1" &&
                        ifaceType is GenericInstanceType git &&
                        git.GenericArguments.Count == 1)
                        if (SameType(git.GenericArguments[0], self))
                            return true;
                }

                cur = cur.BaseType?.Resolve();
            }

            return false;
        }

        static bool AlreadyHasEqualFunction(TypeDefinition type)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name != "Equals") continue;
                if (method.Parameters.Count != 1) continue;
                if (method.Parameters[0].ParameterType.FullName != type.FullName) continue;
                if (method.ReturnType != type.Module.TypeSystem.Boolean) continue;
                return true;
            }
            return false;
        }

        public static void HandleType(TypeDefinition type)
        {
            if (type == null) return;
            if (!(type.IsValueType || type.IsClass)) return;
            if (type.IsInterface) return;
            if (type.IsEnum) return;
            if (type.Module?.Assembly == null) return;
            if (type.Module.Assembly.MainModule != type.Module) return;
            if (HasIEquatableT(type)) return;
            if (AlreadyHasEqualFunction(type)) return;

            var module = type.Module;
            var iEquatableOpen = new TypeReference("System", "IEquatable`1", module, module.TypeSystem.CoreLibrary);
            var selfRef = MakeSelfRef(type);
            var importedSelfRef = selfRef?.Import(module);
            if (importedSelfRef == null) return;

            var iEquatableClosed = new GenericInstanceType(iEquatableOpen);
            iEquatableClosed.GenericArguments.Add(importedSelfRef);

            type.Interfaces.Add(new InterfaceImplementation(iEquatableClosed));

            var equals = new MethodDefinition(
                "Equals",
                MethodAttributes.Public | MethodAttributes.Final |
                MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                module.TypeSystem.Boolean
            );

            equals.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, importedSelfRef));

            try
            {
                var il = equals.Body.GetILProcessor();
                ImplementBody(type, equals, il);
                type.Methods.Add(equals);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed IEquatable.ImplementBody for {type.FullName}: {e.Message}", e);
            }
        }

        private static bool IsPrimitiveNumeric(TypeReference type)
        {
            var mt = type?.MetadataType;
            return mt is MetadataType.SByte or MetadataType.Byte or MetadataType.Int16 or MetadataType.UInt16
                or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64
                or MetadataType.Char or MetadataType.Single or MetadataType.Double or MetadataType.Boolean;
        }

        static bool TryGetEqualityOperator(TypeDefinition type, out MethodReference method)
        {
            if (type == null || !type.TryGetMethod("op_Equality", false, out var r) || !r.IsStatic ||
                r.ReturnType != type.Module.TypeSystem.Boolean || !r.IsPublic)
            {
                method = null;
                return false;
            }

            method = r;
            return method != null;
        }

        private static bool TryGetEqualsFunction(TypeDefinition type, out MethodReference method)
        {
            if (type == null || !type.TryGetMethod("Equals", false, out var r) || r.IsStatic ||
                r.ReturnType != type.Module.TypeSystem.Boolean || !r.IsPublic || r.Parameters[0].ParameterType.FullName != type.FullName)
            {
                method = null;
                return false;
            }

            method = r;
            return method != null;
        }

        private static void ImplementBody(TypeDefinition type, MethodDefinition method, ILProcessor il)
        {
            var returnTrue = Instruction.Create(OpCodes.Ldc_I4_1);
            var returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);

            var purrEqualityType = type.Module.GetTypeDefinition(typeof(PurrEquality<>)).Import(type.Module);
            var purrEqualityCheck = purrEqualityType.GetMethod("Equals").Import(type.Module);

            if (!type.IsValueType && type.BaseType != null && type.BaseType.FullName != typeof(object).FullName)
            {
                var equalsMethod = GenerateSerializersProcessor.CreateGenericMethod(
                    purrEqualityType, type.BaseType, purrEqualityCheck, type.Module);

                il.Append(Instruction.Create(OpCodes.Ldarg_0));
                il.Append(Instruction.Create(OpCodes.Ldarg_1));

                il.Append(Instruction.Create(OpCodes.Call, equalsMethod));
                il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
            }

            var otherParam = method.Parameters[0];

            foreach (var field in type.Fields)
            {
                if (field.IsStatic)
                    continue;

                var isDelegate = PostProcessor.InheritsFrom(field.FieldType.Resolve(), typeof(Delegate).FullName);

                if (isDelegate)
                    continue;

                var ignore = GenerateSerializersProcessor.ShouldIgnoreField(field);

                if (ignore)
                    continue;

                var fieldType = GenerateSerializersProcessor.ResolveGenericFieldType(field, type);
                var resolvedFieldType = fieldType?.Resolve();

                FieldReference fieldRef;

                if (type.HasGenericParameters)
                {
                    // Link the field to the open generic instance
                    var resolvedParent = new GenericInstanceType(type);

                    // Populate the generic arguments
                    foreach (var genericArg in type.GenericParameters)
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

                bool shouldSkipEqualityCheck = field.FieldType.IsArray || field.FieldType.FullName == typeof(Quaternion).FullName;

                if (IsPrimitiveNumeric(field.FieldType))
                {
                    PushAB(il, fieldRef);

                    // check if these integer fields are equal, if not return false
                    il.Append(Instruction.Create(OpCodes.Ceq));
                    il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
                }
                else switch (shouldSkipEqualityCheck)
                {
                    case false when TryGetEqualityOperator(resolvedFieldType, out var opEquality):
                        PushAB(il, field);

                        il.Append(Instruction.Create(OpCodes.Call, opEquality.Import(type.Module)));
                        il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
                        break;
                    case false when TryGetEqualsFunction(resolvedFieldType, out var equals):
                        PushAB_A(type, il, fieldRef, otherParam);

                        il.Append(Instruction.Create(OpCodes.Call, equals.Import(type.Module)));
                        il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
                        break;
                    default:
                    {
                        // Fallback is PurrEquality<T>.Equals(a, b)
                        var equalsMethod = GenerateSerializersProcessor.CreateGenericMethod(
                            purrEqualityType, fieldType, purrEqualityCheck, type.Module);

                        PushAB(il, fieldRef);
                        il.Append(Instruction.Create(OpCodes.Call, equalsMethod));
                        il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
                        break;
                    }
                }
            }

            il.Append(Instruction.Create(OpCodes.Br, returnTrue));

            il.Append(returnFalse);
            il.Append(Instruction.Create(OpCodes.Ret));

            il.Append(returnTrue);
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        private static void PushAB(ILProcessor il, FieldReference field)
        {
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldfld, field));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, field));
        }

        private static void PushAB_A(TypeDefinition type, ILProcessor il, FieldReference field, ParameterDefinition otherParam)
        {
            bool isOtherParamAClass = !type.IsValueType && type.IsClass;
            il.Append(isOtherParamAClass
                ? Instruction.Create(OpCodes.Ldarg_1)
                : Instruction.Create(OpCodes.Ldarga_S, otherParam));

            bool isFieldARefType = !field.FieldType.IsValueType && field.FieldType.Resolve()?.IsClass == true;

            il.Append(isFieldARefType
                ? Instruction.Create(OpCodes.Ldfld, field)
                : Instruction.Create(OpCodes.Ldflda, field));

            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, field));
        }
    }
}
#endif
