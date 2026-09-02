using System.Collections.Generic;
using PurrNet.Collections;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    internal class VisilityV2
    {
        readonly NetworkManager _manager;
        readonly NetworkVisibilityRuleSet _defaultRuleSet;

        public delegate void VisibilityChanged(PlayerID player, Transform scope, bool hasVisibility);

        public event VisibilityChanged visibilityChanged;

        public VisilityV2(NetworkManager manager)
        {
            _manager = manager;
            _defaultRuleSet = manager.visibilityRules;
        }

        /// <summary>
        /// Refreshes visibility for the given GameObject for the specified player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="transform"></param>
        /// <returns>True if any visibility has changed</returns>
        public void RefreshVisibilityForGameObject(PlayerID player, Transform transform)
        {
            if (!transform)
                return;

            RefreshVisibilityForGameObject(player, transform, _defaultRuleSet, true, false);
        }

        public void RefreshVisibilityForGameObject(PlayerID player, Transform transform, NetworkIdentity parent)
        {
            if (!transform)
                return;

            bool isParentVisible = !parent || parent.IsObserverOrPending(player);

            RefreshVisibilityForGameObject(player, transform, _defaultRuleSet, isParentVisible, false);
        }

        public void ClearVisibilityForGameObject(Transform transform)
        {
            if (!transform)
                return;

            var affectedPlayers = HashSetPool<PlayerID>.Instantiate();

            ClearVisibilityForGameObject(transform, affectedPlayers);

            foreach (var player in affectedPlayers)
                visibilityChanged?.Invoke(player, transform, false);

            HashSetPool<PlayerID>.Destroy(affectedPlayers);
        }

        public void ClearVisibilityForGameObject(Transform transform, PlayerID player)
        {
            if (!transform)
                return;

            RefreshVisibilityForGameObject(transform, player);
            visibilityChanged?.Invoke(player, transform, false);
        }

        private static bool RefreshVisibilityForGameObject(Transform transform, PlayerID player)
        {
            using var identities = DisposableList<NetworkIdentity>.Create(16);
            transform.GetComponents(identities.list);

            bool removed = false;

            int ccount = identities.Count;
            if (ccount == 0)
                return removed;

            for (var i = 0; i < ccount; i++)
            {
                var identity = identities[i];
                if (identity.TryRemoveObserver(player))
                    removed = true;
            }

            var directChildren = identities[0].directChildren;
            if (directChildren == null)
                return removed;

            var dcount = directChildren.Count;

            for (var i = 0; i < dcount; i++)
            {
                if (i >= directChildren.Count)
                    break;

                var child = directChildren[i];
                if (!child)
                    continue;

                var childTransform = child.transform;
                if (!childTransform)
                    continue;

                removed |= RefreshVisibilityForGameObject(childTransform, player);
            }

            return removed;
        }

        private static void ClearVisibilityForGameObject(Transform transform, HashSet<PlayerID> players)
        {
            using var identities = DisposableList<NetworkIdentity>.Create(16);
            transform.GetComponents(identities.list);

            int ccount = identities.Count;
            if (ccount == 0)
                return;

            for (var i = 0; i < ccount; i++)
            {
                var identity = identities[i];
                var observers = identity.observers;
                players.UnionWith(observers);
                if (identity.hasPendingObservers)
                    players.UnionWith(identity.pendingObservers);
                identity.ClearObservers();
            }

            var directChildren = identities[0].directChildren;
            if (directChildren == null)
                return;

            var dcount = directChildren.Count;

            for (var i = 0; i < dcount; i++)
            {
                if (i >= directChildren.Count)
                    break;

                var child = directChildren[i];
                if (!child)
                    continue;

                var childTransform = child.transform;
                if (!childTransform)
                    continue;

                ClearVisibilityForGameObject(childTransform, players);
            }
        }

        private void RefreshVisibilityForGameObject(PlayerID player, Transform transform,
            NetworkVisibilityRuleSet rules, bool isParentVisible, bool wasParentDirtied)
        {
            using var identities = DisposableList<NetworkIdentity>.Create(16);

            transform.GetComponents(identities.list);

            if (identities.Count == 0)
                return;

            var isVisible = Evaluate(player, identities.list, ref rules, isParentVisible, out bool fullyChanged, transform);
            bool shouldTrigger = !wasParentDirtied && fullyChanged;

            if (shouldTrigger)
                wasParentDirtied = true;

            var directChildren = identities[0].directChildren;
            if (directChildren != null)
            {
                var count = directChildren.Count;

                for (var i = 0; i < count; i++)
                {
                    if (i >= directChildren.Count)
                        break;

                    var pair = directChildren[i];
                    if (!pair)
                        continue;

                    var childTransform = pair.transform;
                    if (!childTransform)
                        continue;

                    RefreshVisibilityForGameObject(player, childTransform, rules, isVisible, wasParentDirtied);
                }
            }

            if (shouldTrigger)
                visibilityChanged?.Invoke(player, transform, isVisible);
        }

        public void EvaluateAll(IReadOnlyList<PlayerID> players, List<NetworkIdentity> identities)
        {
            var hash = HashSetPool<NetworkIdentity>.Instantiate();

            for (var i = 0; i < identities.Count; i++)
            {
                var nid = identities[i];
                var root = nid.GetRootIdentity();

                if (!root)
                    continue;

                hash.Add(root);
            }


            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                foreach (var root in hash)
                {
                    RefreshVisibilityForGameObject(player, root.transform);
                }
            }

            HashSetPool<NetworkIdentity>.Destroy(hash);
        }

        /// <summary>
        /// Evaluate visibility of the object.
        /// Also adds/removes observers based on the visibility.
        /// </summary>
        private bool Evaluate(PlayerID player, List<NetworkIdentity> identities,
            ref NetworkVisibilityRuleSet rules, bool isParentVisible, out bool fullyChanged, Transform transform)
        {
            fullyChanged = false;

            if (!isParentVisible)
            {
                for (var i = 0; i < identities.Count; i++)
                    identities[i].TryRemoveObserver(player);
                return false;
            }

            bool isAnyVisible = false;

            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];

                if (identity.whitelist.Contains(player))
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                    continue;
                }

                if (identity.blacklist.Contains(player))
                {
                    if (identity.TryRemoveObserver(player))
                        fullyChanged = true;
                    continue;
                }

                var r = identity.GetOverrideOrDefault(rules);

                if (r && r.childrenInherit)
                    rules = r;

                if (!r)
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                    continue;
                }

                if (identity.owner == player)
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                    continue;
                }

                if (!r.CanSee(player, identity))
                {
                    if (identity.TryRemoveObserver(player))
                        fullyChanged = true;
                }
                else
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                }
            }

            return isAnyVisible;
        }

        private bool ShouldAddObserver(PlayerID player, NetworkIdentity identity)
        {
#if ADDRESSABLES_PURRNET_SUPPORT
            return ShouldAddObserverAddressables(player, identity);
#else
            return true;
#endif
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        private bool ShouldAddObserverAddressables(PlayerID player, NetworkIdentity identity)
        {
            if (!_manager.networkRules)
                return true;

            if (!_manager.networkRules.AddressablesWaitForLoadBeforeObserver)
                return true;

            if (!_manager.prefabResolver.TryGetAddressableGuid(identity.scopedPrefabId, out var guid))
                return true;

            if (!_manager.TryGetModule<AddressablesSyncModule>(true, out var sync))
                return true;

            if (sync.ClientHasLoaded(player, guid))
                return true;

            sync.RequestPlayerToLoad(player, guid);
            return false;
        }
#endif
    }
}
