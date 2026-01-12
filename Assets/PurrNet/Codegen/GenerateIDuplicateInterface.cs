#if UNITY_MONO_CECIL
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;

namespace PurrNet.Codegen
{
    public static class GenerateIDuplicateInterface
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

        private static bool HasIDuplicateT(TypeDefinition def)
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
                        ifaceDef.Name == "IDuplicate`1" &&
                        ifaceType is GenericInstanceType git &&
                        git.GenericArguments.Count == 1)
                        if (SameType(git.GenericArguments[0], self))
                            return true;
                }

                cur = cur.BaseType?.Resolve();
            }

            return false;
        }

        static bool AlreadyHasDuplicateFunction(TypeDefinition type)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name != "Duplicate") continue;
                if (method.Parameters.Count != 0) continue;
                if (method.ReturnType.FullName != type.FullName) continue;
                return true;
            }
            return false;
        }

        public static void HandleType(TypeDefinition type)
        {
            if (type == null) return;
            if (!type.IsValueType) return;
            if (type.IsInterface) return;
            if (type.IsEnum) return;
            if (type.Module?.Assembly == null) return;
            if (type.Module.Assembly.MainModule != type.Module) return;
            if (type.IsUnmanaged()) return;

            if (HasIDuplicateT(type)) return;
            if (AlreadyHasDuplicateFunction(type)) return;

            var module = type.Module;
            var iDuplicateOpen = module.GetTypeDefinition(typeof(IDuplicate<>)).Import(module);
            var selfRef = MakeSelfRef(type);
            var importedSelfRef = selfRef?.Import(module);
            if (importedSelfRef == null) return;

            var iDuplicateClosed = new GenericInstanceType(iDuplicateOpen);
            iDuplicateClosed.GenericArguments.Add(importedSelfRef);

            type.Interfaces.Add(new InterfaceImplementation(iDuplicateClosed));

            var duplicate = new MethodDefinition(
                "Duplicate",
                MethodAttributes.Public | MethodAttributes.Final |
                MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                importedSelfRef
            );

            try
            {
                var il = duplicate.Body.GetILProcessor();
                ImplementBody(type, importedSelfRef, duplicate, il);
                type.Methods.Add(duplicate);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed IDuplicate.ImplementBody for {type.FullName}: {e.Message}", e);
            }
        }

        private static void ImplementBody(TypeDefinition type, TypeReference selfRef, MethodDefinition method, ILProcessor il)
        {
            var resultVar = new VariableDefinition(selfRef);
            method.Body.Variables.Add(resultVar);
            method.Body.InitLocals = true;

            var purrCopyType = type.Module.GetTypeDefinition(typeof(PurrCopy<>)).Import(type.Module);
            var purrDubMethod = purrCopyType.GetMethod("Duplicate").Import(type.Module);

            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldobj, selfRef));
            il.Append(Instruction.Create(OpCodes.Stloc, resultVar));

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

                bool isUnmanaged = resolvedFieldType != null && resolvedFieldType.IsUnmanaged();

                if (isUnmanaged)
                    continue;

                var dupMethod = GenerateSerializersProcessor.CreateGenericMethod(
                    purrCopyType, fieldType, purrDubMethod, type.Module);

                il.Append(Instruction.Create(OpCodes.Ldloca_S, resultVar));
                il.Append(Instruction.Create(OpCodes.Ldarg_0));
                il.Append(Instruction.Create(OpCodes.Ldflda, fieldRef));
                il.Append(Instruction.Create(OpCodes.Call, dupMethod));
                il.Append(Instruction.Create(OpCodes.Stfld, fieldRef));
            }

            // Load the result and return it
            il.Append(Instruction.Create(OpCodes.Ldloc, resultVar));
            il.Append(Instruction.Create(OpCodes.Ret));
        }
    }
}
#endif
