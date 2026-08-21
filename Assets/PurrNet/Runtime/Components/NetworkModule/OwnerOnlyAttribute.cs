using System;
using JetBrains.Annotations;
using UnityEngine.Scripting;

namespace PurrNet
{
    /// <summary>
    /// Scopes a NetworkModule so its state is only ever sent to the owner of the parent identity.
    /// Non-owner observers still see the object, they just never receive this module's data.
    /// If the identity has no owner, the module sends to nobody.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public class OwnerOnlyAttribute : PreserveAttribute
    {
        [UsedImplicitly]
        public OwnerOnlyAttribute() { }
    }
}
