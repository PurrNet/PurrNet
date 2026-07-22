using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Receives distance-based LOD tier updates from a <see cref="Modules.NetworkLODModule"/>.
    /// </summary>
    public interface ILODTarget
    {
        Vector3 position { get; }

        uint staggerSeed { get; }

        void ApplyTier(PlayerID player, byte tier);
    }

    internal interface ILODOwnedTarget : ILODTarget
    {
        bool IsOwnedBy(PlayerID player);
    }

    internal interface ILODDetailedTierTarget : ILODTarget
    {
        void ApplyTier(PlayerID player, byte previousTier, byte newTier);
    }
}
