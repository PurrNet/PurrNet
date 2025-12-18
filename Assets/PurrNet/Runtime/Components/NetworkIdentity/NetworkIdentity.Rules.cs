using JetBrains.Annotations;
using PurrNet.Collections;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet
{
    public partial class NetworkIdentity
    {
        [SerializeField, HideInInspector] private NetworkRules _networkRules;
        [SerializeField, HideInInspector] private NetworkVisibilityRuleSet _visitiblityRules;

        private readonly PurrHashSet<PlayerID> _whitelist = new PurrHashSet<PlayerID>();

        private readonly PurrHashSet<PlayerID> _blacklist = new PurrHashSet<PlayerID>();

        /// <summary>
        /// Whitelist of players that can interact with this identity.
        /// This doesn't block visibility for others but rather enforces visibility for these players.
        /// </summary>
        [UsedImplicitly] public IReadonlyHashSet<PlayerID> whitelist => _whitelist;

        /// <summary>
        /// Blacklist of players that can't interact with this identity.
        /// </summary>
        [UsedImplicitly] public IReadonlyHashSet<PlayerID> blacklist => _blacklist;


        private bool _whiteBlackDirty;

        public bool WhitelistPlayer(PlayerID player)
        {
            if (!isServer)
            {
                PurrLogger.LogError(
                    $"Tried to whitelist player `{player}` on `{this}` but only the server can modify the whitelist.", this);
                return false;
            }

            if (_whitelist.Add(player))
            {
                SetVisibilityDirty();
                return true;
            }
            return false;
        }

        private void SetVisibilityDirty()
        {
            if (!_whiteBlackDirty)
            {
                RegisterTickEvent(true);
                _whiteBlackDirty = true;
            }
        }

        public bool BlacklistPlayer(PlayerID player)
        {
            if (!isServer)
            {
                PurrLogger.LogError(
                    $"Tried to blacklist player `{player}` on `{this}` but only the server can modify the blacklist.", this);
                return false;
            }

            if (_blacklist.Add(player))
            {
                SetVisibilityDirty();
                return true;
            }
            return false;
        }

        public bool RemoveWhitelistPlayer(PlayerID player)
        {
            if (!isServer)
            {
                PurrLogger.LogError(
                    $"Tried to remove player `{player}` from whitelist on `{this}` but only the server can modify the whitelist.", this);
                return false;
            }

            if (_whitelist.Remove(player))
            {
                SetVisibilityDirty();
                return true;
            }
            return false;
        }

        public bool RemoveBlacklistPlayer(PlayerID player)
        {
            if (!isServer)
            {
                PurrLogger.LogError(
                    $"Tried to remove player `{player}` from blacklist on `{this}` but only the server can modify the blacklist.", this);
                return false;
            }

            if (_blacklist.Remove(player))
            {
                SetVisibilityDirty();
                return true;
            }
            return false;
        }

        public NetworkRules networkRules =>
            _networkRules ? _networkRules : networkManager ? networkManager.networkRules : null;

        [UsedImplicitly]
        public NetworkVisibilityRuleSet visibilityRules => _visitiblityRules ? _visitiblityRules :
            networkManager ? networkManager.visibilityRules : null;

        public NetworkVisibilityRuleSet GetOverrideOrDefault(NetworkVisibilityRuleSet defaultValue)
        {
            return _visitiblityRules ? _visitiblityRules : defaultValue;
        }

        public bool HasDespawnAuthority(PlayerID player, bool asServer)
        {
            var rules = networkRules;
            return rules && networkRules.HasDespawnAuthority(this, player, asServer);
        }

        public bool HasSpawnAuthority(NetworkManager manager, bool asServer)
        {
            var rules = _networkRules ? _networkRules : manager.networkRules;
            return rules && rules.HasSpawnAuthority(manager, asServer);
        }

        public bool ShouldDespawnOnOwnerDisconnect()
        {
            var rules = networkRules;
            return rules && rules.ShouldDespawnOnOwnerDisconnect();
        }

        private NetworkRules GetNetworkRules(NetworkManager manager)
        {
            return _networkRules ? _networkRules : manager.networkRules;
        }

        public bool ShouldClientGiveOwnershipOnSpawn(NetworkManager manager)
        {
            var rules = GetNetworkRules(manager);
            return rules && rules.ShouldClientGiveOwnershipOnSpawn();
        }

        public bool ShouldPlayRPCsWhenDisabled()
        {
            var rules = networkRules;
            return rules && rules.ShouldPlayRPCsWhenDisabled();
        }

        public bool ShouldPropagateToChildren()
        {
            var rules = networkRules;
            return rules && rules.ShouldPropagateToChildren();
        }

        public bool ShouldOverrideExistingOwnership(bool asServer)
        {
            var rules = networkRules;
            return rules && rules.ShouldOverrideExistingOwnership(this, asServer);
        }

        public bool HasPropagateOwnershipAuthority()
        {
            var rules = networkRules;
            return rules && rules.HasPropagateOwnershipAuthority(this);
        }

        public bool HasChangeParentAuthority(bool asServer)
        {
            var rules = networkRules;
            return rules && rules.HasChangeParentAuthority(this, localPlayer, asServer);
        }

        public bool HasChangeParentAuthority(PlayerID player, bool asServer)
        {
            var rules = networkRules;
            return rules && rules.HasChangeParentAuthority(this, player, asServer);
        }


        public bool HasTransferOwnershipAuthority(bool asServer)
        {
            var rules = networkRules;
            return rules && rules.HasTransferOwnershipAuthority(this, localPlayer, asServer);
        }

        public bool HasTransferOwnershipAuthority(PlayerID player, bool asServer)
        {
            var rules = networkRules;
            return rules && rules.HasTransferOwnershipAuthority(this, player, asServer);
        }

        public bool HasGiveOwnershipAuthority(bool asServer)
        {
            var rules = networkRules;
            return rules && rules.HasGiveOwnershipAuthority(this, asServer);
        }

        public bool HasRemoveOwnershipAuthority(PlayerID player, bool asServer)
        {
            var rules = networkRules;
            return rules && rules.HasRemoveOwnershipAuthority(this, player, asServer);
        }

        internal bool TryAddObserver(PlayerID player)
        {
            if (_observers.Contains(player))
                return false;
            _observers.Add(player);
            return true;
        }

        internal bool TryRemoveObserver(PlayerID player)
        {
            return _observers.Remove(player);
        }
    }
}
