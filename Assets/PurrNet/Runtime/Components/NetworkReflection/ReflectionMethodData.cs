using System;
using System.Reflection;

namespace PurrNet
{
    [Serializable]
    public struct ReflectionMethodData
    {
        public string name;
        public ReflectionRPCType rpcType;

        public MethodInfo GetMethod(Type reflectionType)
        {
            var methods = reflectionType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            for (var i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name)
                    return methods[i];
            }

            return null;
        }
    }
}
