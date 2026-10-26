using System;

namespace PurrNet.Packing
{
    // Generated indices identify only registration methods. Looking up every static
    // method also materializes unrelated reflection metadata in native players.
    public readonly struct PackerRegistrationEntry
    {
        public const string IndexTypeName = "PurrNet.Generated.PackerRegistrationIndexV1";
        public const string IndexMethodName = "GetRegistrations";

        public readonly RuntimeMethodHandle method;
        public readonly RuntimeTypeHandle declaringType;
        public readonly int priority;

        public PackerRegistrationEntry(RuntimeMethodHandle method, RuntimeTypeHandle declaringType, int priority)
        {
            this.method = method;
            this.declaringType = declaringType;
            this.priority = priority;
        }
    }
}
