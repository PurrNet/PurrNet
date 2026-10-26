using System;
using System.Reflection;
using PurrNet.Packing;

namespace PurrNet.Utils
{
    public static class DerivedTypeDiscovery
    {
        public const string PlayerIdentityIndexTypeName = "PurrNet.Generated.PlayerIdentityTypeIndexV1";

        // Missing or incompatible indexes retain the caller's existing reflection path.
        public static Type[] ReadIndex(Assembly assembly, string indexTypeName)
        {
            try
            {
                var index = assembly.GetType(indexTypeName, false);
                if (index == null || !index.IsDefined(typeof(GeneratedByILAttribute), false))
                    return null;
                var method = index.GetMethod("GetTypes", BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(Type[]))
                    return null;
                var types = method.Invoke(null, null) as Type[];
                if (types == null) return null;
                for (int i = 0; i < types.Length; i++)
                    if (types[i] == null) return null;
                return types;
            }
            catch { return null; }
        }
    }
}
