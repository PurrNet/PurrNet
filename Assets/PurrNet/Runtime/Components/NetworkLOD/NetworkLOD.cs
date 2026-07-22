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
    public sealed class NetworkLOD : NetworkIdentity, ILODOwnedTarget, ILODDetailedTierTarget
    {
        [SerializeField] private NetworkLODProfile _profile;

        private readonly List<NetworkIdentity> _siblings = new List<NetworkIdentity>();
        private Modules.NetworkLODModule _serverModule;
        private Modules.NetworkLODModule _clientModule;

        public NetworkLODProfile profile
        {
            get => _profile;
            set
            {
                _profile = value;
                _serverModule?.UpdateProfile(this, _profile);
                if (!object.ReferenceEquals(_clientModule, _serverModule))
                    _clientModule?.UpdateProfile(this, _profile);
            }
        }

        /// <summary>
        /// Optional override for send scheduling. Defaults to <see cref="LODIntervalScheduler"/>.
        /// </summary>
        public ILODScheduler scheduler { get; set; }

        public uint staggerOffset { get; private set; }

        public Vector3 position => transform.position;

        public uint staggerSeed => staggerOffset;

        /// <summary>
        /// Current tier for the given player. 0 (full detail) for owners and players not yet evaluated.
        /// Server only; always 0 on clients.
        /// </summary>
        public byte GetTier(PlayerID player)
        {
            if (owner == player)
                return 0;
            return (_serverModule ?? _clientModule)?.GetTier(this, player) ?? 0;
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
            return activeScheduler.ShouldSendThisTick(this, _profile, player, tier, nm.tickModule.localTick);
        }

        public void ApplyTier(PlayerID player, byte tier)
        {
            var module = _serverModule ?? _clientModule;
            if (module != null && module.SetTier(this, player, tier))
                return;

            ApplyTier(player, GetTier(player), tier);
        }

        bool ILODOwnedTarget.IsOwnedBy(PlayerID player)
        {
            return owner == player;
        }

        void ILODDetailedTierTarget.ApplyTier(PlayerID player, byte previousTier, byte newTier)
        {
            ApplyTier(player, previousTier, newTier);
        }

        private void ApplyTier(PlayerID player, byte previousTier, byte newTier)
        {

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

        protected override void OnSpawned(bool asServer)
        {
            staggerOffset = id.HasValue ? (uint)(id.Value.GetHashCode() & 0x7fffffff) : 0u;

            GetComponents(_siblings);
            for (var i = 0; i < _siblings.Count; i++)
                _siblings[i].SetLODComponent(this);

            if (networkManager.TryGetModule<Modules.NetworkLODFactory>(asServer, out var factory) &&
                factory.TryGetModule(sceneId, out var module))
            {
                if (asServer)
                    _serverModule = module;
                else
                    _clientModule = module;
                module.Register(this, _profile);
            }
        }

        protected override void OnDespawned(bool asServer)
        {
            for (var i = 0; i < _siblings.Count; i++)
            {
                if (_siblings[i])
                    _siblings[i].SetLODComponent(null);
            }

            _siblings.Clear();

            var registeredModule = asServer ? _serverModule : _clientModule;

            if (registeredModule != null)
                registeredModule.Unregister(this);
            else if (networkManager.TryGetModule<Modules.NetworkLODFactory>(asServer, out var factory) &&
                     factory.TryGetModule(sceneId, out var module))
                module.Unregister(this);

            if (asServer)
                _serverModule = null;
            else
                _clientModule = null;
        }
    }
}
