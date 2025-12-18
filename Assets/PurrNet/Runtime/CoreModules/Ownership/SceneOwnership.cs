using System;
using System.Collections.Generic;

namespace PurrNet.Modules
{
    internal class SceneOwnership
    {
        static readonly List<OwnershipInfo> _cache = new List<OwnershipInfo>();

        readonly Dictionary<NetworkID, PlayerID> _owners = new Dictionary<NetworkID, PlayerID>();

        readonly Dictionary<PlayerID, HashSet<NetworkID>> _playerOwnedIds =
            new Dictionary<PlayerID, HashSet<NetworkID>>();

        private bool _asServer;

        public SceneOwnership(bool asServer)
        {
            _asServer = asServer;
        }

        public void PromoteToServerModule(HierarchyV2 hierarchy)
        {
            _asServer = true;

            foreach (var (owner, ids) in _playerOwnedIds)
            {
                foreach (var id in ids)
                {
                    if (hierarchy.TryGetIdentity(id, out var identity))
                    {
                        identity.internalOwnerServer = owner;
                        identity.internalOwnerClient = null;
                    }
                }
            }
        }

        public List<OwnershipInfo> GetState()
        {
            _cache.Clear();

            foreach (var (id, player) in _owners)
                _cache.Add(new OwnershipInfo { identity = id, player = player });

            return _cache;
        }

        public ICollection<NetworkID> TryGetOwnedObjects(PlayerID player)
        {
            if (_playerOwnedIds.TryGetValue(player, out var players))
                return players;
            return Array.Empty<NetworkID>();
        }

        public bool TryGetOwner(NetworkIdentity id, out PlayerID player)
        {
            if (!id.id.HasValue)
            {
                player = default;
                return false;
            }

            return _owners.TryGetValue(id.id.Value, out player);
        }

        public bool GiveOwnership(NetworkIdentity identity, PlayerID player)
        {
            if (identity.id == null)
                return false;

            _owners[identity.id.Value] = player;

            var oldOwner = identity.GetOwner(_asServer);

            // Remove from old owner's owned list
            if (oldOwner.HasValue && oldOwner.Value != player && _playerOwnedIds.TryGetValue(oldOwner.Value, out var owned))
                owned.Remove(identity.id.Value);

            // Add to new owner's owned list
            if (!_playerOwnedIds.TryGetValue(player, out var ownedIds))
            {
                ownedIds = new HashSet<NetworkID> { identity.id.Value };
                _playerOwnedIds[player] = ownedIds;
            }
            else ownedIds.Add(identity.id.Value);

            if (_asServer)
                identity.internalOwnerServer = player;
            else identity.internalOwnerClient = player;

            return true;
        }

        public bool RemoveOwnership(NetworkIdentity identity)
        {
            if (identity.id.HasValue && _owners.Remove(identity.id.Value, out var oldOwner))
            {
                if (_playerOwnedIds.TryGetValue(oldOwner, out var ownedIds))
                {
                    ownedIds.Remove(identity.id.Value);

                    if (ownedIds.Count == 0)
                        _playerOwnedIds.Remove(oldOwner);
                }

                if (_asServer)
                    identity.internalOwnerServer = null;
                else identity.internalOwnerClient = null;
                return true;
            }

            return false;
        }
    }
}
