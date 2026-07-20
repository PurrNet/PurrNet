using System.Collections.Generic;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Opt-in network LOD for the identities on this GameObject.
    /// The server periodically resolves a tier per observer based on distance to their anchors,
    /// which drives send-rate scheduling via <see cref="ShouldSendToPlayer"/>.
    /// Network LOD never changes observer membership or visibility.
    /// </summary>
    public sealed class NetworkLOD : NetworkIdentity
    {
        [SerializeField] private NetworkLODProfile _profile;

        private readonly Dictionary<PlayerID, byte> _tiers = new Dictionary<PlayerID, byte>();
        private readonly List<NetworkIdentity> _siblings = new List<NetworkIdentity>();

        public NetworkLODProfile profile
        {
            get => _profile;
            set => _profile = value;
        }

        /// <summary>
        /// Optional override for send scheduling. Defaults to <see cref="LODIntervalScheduler"/>.
        /// </summary>
        public ILODScheduler scheduler { get; set; }

        public uint staggerOffset { get; private set; }

        /// <summary>
        /// Current tier for the given player. 0 (full detail) for owners and players not yet evaluated.
        /// Server only; always 0 on clients.
        /// </summary>
        public byte GetTier(PlayerID player)
        {
            if (owner == player)
                return 0;
            return _tiers.GetValueOrDefault(player, (byte)0);
        }

        /// <summary>
        /// True when this player is in the send-culled tier. The player may still be an observer.
        /// </summary>
        public bool IsCulled(PlayerID player)
        {
            return GetTier(player) == NetworkLODProfile.CulledTier;
        }

        /// <summary>
        /// Whether LOD-aware senders should include this object's state for the given player on the current tick.
        /// A culled tier means "send nothing" for LOD-gated traffic only; it does not affect visibility.
        /// Always true when no profile is assigned or the manager is unavailable.
        /// </summary>
        public bool ShouldSendToPlayer(PlayerID player)
        {
            byte tier = GetTier(player);

            if (tier == NetworkLODProfile.CulledTier)
                return false;

            var nm = networkManager;
            if (!nm)
                return true;

            var activeScheduler = scheduler ?? LODIntervalScheduler.instance;
            return activeScheduler.ShouldSendThisTick(this, player, tier, nm.tickModule.localTick);
        }

        internal void ApplyTier(PlayerID player, byte previousTier, byte newTier)
        {
            _tiers[player] = newTier;

            if (previousTier == newTier)
                return;

            for (var i = 0; i < _siblings.Count; i++)
            {
                var sibling = _siblings[i];
                if (!sibling)
                    continue;

                sibling.TriggerOnLODTierChanged(player, previousTier, newTier);
            }
        }

        internal void RemovePlayer(PlayerID player)
        {
            _tiers.Remove(player);
        }

        protected override void OnSpawned(bool asServer)
        {
            staggerOffset = id.HasValue ? (uint)(id.Value.GetHashCode() & 0x7fffffff) : 0u;

            GetComponents(_siblings);
            for (var i = 0; i < _siblings.Count; i++)
                _siblings[i].SetLODComponent(this);

            if (networkManager.TryGetModule<Modules.NetworkLODFactory>(asServer, out var factory) &&
                factory.TryGetModule(sceneId, out var module))
                module.Register(this);
        }

        protected override void OnDespawned(bool asServer)
        {
            if (asServer)
                _tiers.Clear();

            for (var i = 0; i < _siblings.Count; i++)
            {
                if (_siblings[i])
                    _siblings[i].SetLODComponent(null);
            }

            _siblings.Clear();

            if (networkManager.TryGetModule<Modules.NetworkLODFactory>(asServer, out var factory) &&
                factory.TryGetModule(sceneId, out var module))
                module.Unregister(this);
        }
    }
}
