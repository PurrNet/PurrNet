#if UNITY_MONO_CECIL
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;

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

        private static bool HasIPurrEquatableT(TypeDefinition def)
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

                    if (ifaceDef.Namespace == "PurrNet.Packing" &&
                        ifaceDef.Name == "IPurrEquatable`1" &&
                        ifaceType is GenericInstanceType git &&
                        git.GenericArguments.Count == 1)
                        if (SameType(git.GenericArguments[0], self))
                            return true;
                }

                cur = cur.BaseType?.Resolve();
            }

            return false;
        }

        static bool AlreadyHasPurrEqualsFunction(TypeDefinition type)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name != "PurrEquals") continue;
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
            if (HasIPurrEquatableT(type)) return;
            if (AlreadyHasPurrEqualsFunction(type)) return;

            var module = type.Module;
            var iPurrEquatableOpen = module.GetTypeDefinition(typeof(IPurrEquatable<>)).Import(module);
            var selfRef = MakeSelfRef(type);
            var importedSelfRef = selfRef?.Import(module);
            if (importedSelfRef == null) return;

            var iPurrEquatableClosed = new GenericInstanceType(iPurrEquatableOpen);
            iPurrEquatableClosed.GenericArguments.Add(importedSelfRef);

            type.Interfaces.Add(new InterfaceImplementation(iPurrEquatableClosed));

            var purrEquals = new MethodDefinition(
                "PurrEquals",
                MethodAttributes.Public | MethodAttributes.Final |
                MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                module.TypeSystem.Boolean
            );

            purrEquals.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, importedSelfRef));

            try
            {
                var il = purrEquals.Body.GetILProcessor();
                ImplementBody(type, purrEquals, il);
                type.Methods.Add(purrEquals);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed IPurrEquatable.ImplementBody for {type.FullName}: {e.Message}", e);
            }
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

                // PurrEquality is byte-exact for unmanaged values and recursively uses registered
                // comparers for managed values. Do not use ceq/op_Equality here: float signed zero,
                // NaN payloads, and Unity's approximate vector operators are observable on the wire.
                var equalsMethod = GenerateSerializersProcessor.CreateGenericMethod(
                    purrEqualityType, fieldType, purrEqualityCheck, type.Module);

                PushAB(il, fieldRef);
                il.Append(Instruction.Create(OpCodes.Call, equalsMethod));
                il.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
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

    }
}
#endif
