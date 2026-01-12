using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;

#if UNITY_MONO_CECIL
namespace PurrNet.Codegen
{
    public static class DuplicateHelpers
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

        public static TypeReference MakeSelfRef(TypeDefinition type)
        {
            if (!type.HasGenericParameters) return type;
            var gi = new GenericInstanceType(type);
            foreach (var gp in type.GenericParameters) gi.GenericArguments.Add(gp);
            return gi;
        }

        public static bool HasDuplicateInterface(TypeReference def)
        {
            var resolved = def.Resolve();
            return resolved != null && HasDuplicateInterface(resolved);
        }

        public static bool HasDuplicateInterface(TypeDefinition def)
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

        public static void InjectRegistration(TypeDefinition parentClass, TypeReference type, ILProcessor il)
        {
            var purrDuplicate = parentClass.Module.GetTypeDefinition(typeof(PurrCopy)).Import(parentClass.Module);
            var purrOverrideGen = purrDuplicate.GetMethod("Override", true).Import(parentClass.Module);
            var genericOverride = new GenericInstanceMethod(purrOverrideGen);
            genericOverride.GenericArguments.Add(type.Import(parentClass.Module));
            il.Emit(OpCodes.Call, genericOverride.Import(parentClass.Module));
        }
    }
}
#endif
