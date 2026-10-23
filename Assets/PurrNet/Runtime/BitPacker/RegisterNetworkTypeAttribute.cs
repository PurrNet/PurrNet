using System;
using JetBrains.Annotations;
using UnityEngine.Scripting;

namespace PurrNet
{
    /// <summary>
    /// Lists the dependencies of an existing custom or reference codec instead of
    /// discovering them from implementation fields. This does not change the codec,
    /// generic payload discovery or RPC discovery, and is not inherited by subclasses.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class PackerDependenciesAttribute : Attribute
    {
        public PackerDependenciesAttribute(params Type[] dependencies) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true), Preserve]
    public class RegisterNetworkTypeAttribute : PreserveAttribute
    {
        public RegisterNetworkTypeAttribute([UsedImplicitly] Type type)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum), Preserve]
    public class DontPackAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field), Preserve]
    public class DontDeltaCompressAttribute : Attribute { }
}
