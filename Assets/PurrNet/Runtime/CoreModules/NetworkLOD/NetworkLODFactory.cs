using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Modules
{
    public class NetworkLODFactory : SceneScopedFactory<NetworkLODModule>, IFixedUpdate, IPromoteToServerModule
    {
        private readonly NetworkManager _manager;
        private readonly ScenePlayersModule _scenePlayers;
        private readonly Dictionary<PlayerID, List<Transform>> _anchors = new Dictionary<PlayerID, List<Transform>>();

        /// <summary>
        /// Maximum number of LOD-enabled identities evaluated per scene per FixedUpdate.
        /// </summary>
        public int sweepBudgetPerTick { get; set; } = 64;

        public NetworkLODFactory(NetworkManager manager, ScenesModule scenes, ScenePlayersModule scenePlayers)
            : base(scenes)
        {
            _manager = manager;
            _scenePlayers = scenePlayers;
        }

        protected override NetworkLODModule CreateModule(SceneID scene, bool asServer)
        {
            return new NetworkLODModule(_manager, this, scenes, _scenePlayers, scene);
        }

        public void FixedUpdate()
        {
            for (var i = 0; i < modules.Count; i++)
                modules[i].Sweep();
        }

        public void PromoteToServerModule()
        {
            for (var i = 0; i < modules.Count; i++)
                modules[i].PromoteToServerModule();
        }

        public void PostPromoteToServerModule()
        {
            for (var i = 0; i < modules.Count; i++)
                modules[i].PostPromoteToServerModule();
        }

        /// <summary>
        /// Registers a transform as a distance anchor for the given player.
        /// Registered anchors take precedence over the player's owned identities.
        /// A player can have multiple anchors; the closest one decides the tier.
        /// </summary>
        public void RegisterAnchor(PlayerID player, Transform anchor)
        {
            if (!anchor)
                return;

            if (!_anchors.TryGetValue(player, out var list))
            {
                list = new List<Transform>();
                _anchors.Add(player, list);
            }

            if (!list.Contains(anchor))
                list.Add(anchor);
        }

        public bool UnregisterAnchor(PlayerID player, Transform anchor)
        {
            if (!_anchors.TryGetValue(player, out var list))
                return false;

            bool removed = list.Remove(anchor);

            if (list.Count == 0)
                _anchors.Remove(player);

            return removed;
        }

        internal bool TryGetAnchors(PlayerID player, out List<Transform> anchors)
        {
            return _anchors.TryGetValue(player, out anchors);
        }

        internal void RemovePlayerAnchors(PlayerID player)
        {
            _anchors.Remove(player);
        }
    }
}
