#if UNITY_MONO_CECIL
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PurrNet.Codegen
{
    public static class GenerateIEquatableInterface
    {
        static bool SameType(TypeReference a, TypeReference b)
        {
            if (a == null || b == null) return false;
            var ad = a.Resolve();
            var bd = b.Resolve();
            if (ad != null && bd != null)
                return ad.FullName == bd.FullName;
            return a.FullName == b.FullName;
        }

        static TypeReference MakeSelfRef(TypeDefinition type)
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
                    {
                        if (SameType(git.GenericArguments[0], self))
                            return true;
                    }
                }

                cur = cur.BaseType?.Resolve();
            }

            return false;
        }

        public static void HandleType(TypeDefinition type)
        {
            if (type == null) return;
            if (!(type.IsValueType || type.IsClass)) return;
            if (type.IsInterface) return;
            if (type.Module?.Assembly == null) return;
            if (type.Module.Assembly.MainModule != type.Module) return;
            if (HasIEquatableT(type)) return;

            var module = type.Module;
            var iEquatableOpen = new TypeReference("System", "IEquatable`1", module, module.TypeSystem.CoreLibrary);
            var selfRef = MakeSelfRef(type);

            var importedSelfRef = module.ImportReference(selfRef);
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

            var il = equals.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
            il.Append(Instruction.Create(OpCodes.Ret));

            type.Methods.Add(equals);
        }
    }
}
#endif
