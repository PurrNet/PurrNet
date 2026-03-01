using System;
using System.Reflection;
using PurrNet.Logging;

namespace PurrNet
{
    internal class ReflectedMethod
    {
        public readonly string name;
        public readonly ReflectionRPCType rpcType;
        readonly MethodInfo _method;

        public ReflectedMethod(UnityEngine.Object target, ReflectionMethodData data)
        {
            var type = target.GetType();
            name = data.name;
            rpcType = data.rpcType;
            _method = data.GetMethod(type);

            if (_method == null)
                PurrLogger.LogError($"Method '{name}' not found on {type.Name}", target);
        }

        public void Invoke(UnityEngine.Object target, object[] args)
        {
            if (_method == null)
            {
                PurrLogger.LogError($"Method '{name}' is null, aborting");
                return;
            }

            _method.Invoke(target, args);
        }
    }
}
