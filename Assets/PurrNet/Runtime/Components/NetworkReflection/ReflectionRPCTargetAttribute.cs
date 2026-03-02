using System;

namespace PurrNet
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ReflectionRPCTargetAttribute : Attribute
    {
        public Type TargetType { get; }

        public ReflectionRPCTargetAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }
}
