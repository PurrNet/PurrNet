using System;

namespace PurrNet
{
    /// <summary>
    /// When "Client Only Builds" is enabled in the PurrNet project settings, the body of this method is
    /// removed from client builds (builds without UNITY_SERVER) and replaced with an error log.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class StripInClientOnlyBuildAttribute : Attribute
    {
    }
}
