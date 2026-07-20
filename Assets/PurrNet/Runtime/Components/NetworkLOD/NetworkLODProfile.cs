using System;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public struct NetworkLODTier
    {
        [Tooltip("Players within this distance (and beyond the previous tier) fall into this tier.")]
        [Min(0)] public float maxDistance;

        [Tooltip("Extra distance a player must exceed before being demoted out of this tier. Prevents flapping at the boundary.")]
        [Min(0)] public float hysteresis;

        [Tooltip("How often identities in this tier send, in ticks. 1 = every tick, 2 = every other tick, etc.")]
        [Min(1)] public int sendIntervalTicks;
    }

    [CreateAssetMenu(menuName = "PurrNet/Network LOD/LOD Profile", fileName = "New LOD Profile")]
    public class NetworkLODProfile : ScriptableObject
    {
        public const byte CulledTier = byte.MaxValue;

        [SerializeField]
        private NetworkLODTier[] _tiers =
        {
            new NetworkLODTier { maxDistance = 30f, hysteresis = 5f, sendIntervalTicks = 1 },
            new NetworkLODTier { maxDistance = 75f, hysteresis = 5f, sendIntervalTicks = 2 },
            new NetworkLODTier { maxDistance = 150f, hysteresis = 10f, sendIntervalTicks = 4 }
        };

        [Tooltip("If true, players beyond the last tier enter a send-culled tier. They remain observers; LOD-aware senders decide what to skip.")]
        [SerializeField] private bool _cullBeyondLastTier;

        public int tierCount => _tiers?.Length ?? 0;

        /// <summary>
        /// Configures the profile at runtime, for programmatically created profiles.
        /// </summary>
        public void Configure(NetworkLODTier[] tiers, bool cullBeyondLastTier)
        {
            _tiers = tiers;
            _cullBeyondLastTier = cullBeyondLastTier;
        }

        public bool cullBeyondLastTier => _cullBeyondLastTier;

        public NetworkLODTier GetTier(int index) => _tiers[index];

        public int GetSendIntervalTicks(byte tier)
        {
            if (_tiers == null || _tiers.Length == 0 || tier == CulledTier)
                return 1;
            int idx = Mathf.Min(tier, _tiers.Length - 1);
            return Mathf.Max(1, _tiers[idx].sendIntervalTicks);
        }

        /// <summary>
        /// Resolves the tier for a given squared distance, applying hysteresis relative to the current tier.
        /// Promotion (moving to a closer tier) uses the raw band edge; demotion requires exceeding the edge plus hysteresis.
        /// </summary>
        public byte ResolveTier(float sqrDistance, byte currentTier)
        {
            if (_tiers == null || _tiers.Length == 0)
                return 0;

            int count = _tiers.Length;
            int current = currentTier == CulledTier ? count : currentTier;
            int result = 0;

            while (result < count)
            {
                float edge = _tiers[result].maxDistance;
                if (current <= result)
                    edge += _tiers[result].hysteresis;
                if (sqrDistance <= edge * edge)
                    break;
                result++;
            }

            if (result == count)
                return _cullBeyondLastTier ? CulledTier : (byte)(count - 1);

            return (byte)result;
        }
    }
}
