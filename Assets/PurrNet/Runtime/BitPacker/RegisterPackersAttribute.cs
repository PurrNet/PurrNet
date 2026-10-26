using System;
using UnityEngine.Scripting;

namespace PurrNet.Packing
{
    [AttributeUsage(AttributeTargets.Method)]
    [RequireAttributeUsages]
    public class RegisterPackersAttribute : PreserveAttribute
    {
        public readonly int priority;

        /// <summary>
        /// Register packers
        /// </summary>
        /// <param name="priority">Higher priority means packer will be called first</param>
        public RegisterPackersAttribute(int priority = 0)
        {
            this.priority = priority;
        }
    }
}
