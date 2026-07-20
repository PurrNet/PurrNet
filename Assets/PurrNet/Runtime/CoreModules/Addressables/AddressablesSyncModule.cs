#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Modules
{
    public class AddressablesSyncModule : INetworkModule
    {
        public delegate void OnClientAddressableLoadStateChanged(PlayerID player, string guid, bool loaded);
        
        private readonly NetworkManager _manager;
        private readonly PlayersManager _playersManager;

        private readonly Dictionary<PlayerID, HashSet<string>> _clientLoadedGuids = new();

        /// <summary>
        /// Fired on the server when a client reports that an Addressable asset has been loaded or unloaded.
        /// </summary>
        public event OnClientAddressableLoadStateChanged onClientLoadStateChanged;

        public AddressablesSyncModule(NetworkManager manager, PlayersManager playersManager)
        {
            _manager = manager;
            _playersManager = playersManager;
        }

        public void Enable(bool asServer)
        {
            if (asServer)
            {
                _playersManager.Subscribe<AddressableLoadStatePacket>(OnLoadStateReceived);
                _playersManager.onPlayerLeft += OnPlayerLeft;
            }
            else
            {
                if (_manager.networkRules && _manager.networkRules.AddressablesSyncLoadState &&
                    _manager.addressableNetworkPrefabs)
                {
                    _playersManager.Subscribe<AddressableLoadRequestPacket>(OnLoadRequestReceived);
                    AddressableNetworkPrefabs.onLoadStateChanged += OnClientLoadStateChanged;
                    foreach (var guid in _manager.addressableNetworkPrefabs.GetLoadedGuids())
                        SendLoadState(guid, true);
                }
            }
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _playersManager.Unsubscribe<AddressableLoadStatePacket>(OnLoadStateReceived);
                _playersManager.onPlayerLeft -= OnPlayerLeft;
                _clientLoadedGuids.Clear();
            }
            else
            {
                _playersManager.Unsubscribe<AddressableLoadRequestPacket>(OnLoadRequestReceived);
                AddressableNetworkPrefabs.onLoadStateChanged -= OnClientLoadStateChanged;
            }
        }

        private async void OnLoadRequestReceived(PlayerID sender, AddressableLoadRequestPacket packet, bool asServer)
        {
            if (asServer || !_manager.addressableNetworkPrefabs)
                return;

            var guid = packet.guid.value ?? string.Empty;
            if (string.IsNullOrEmpty(guid))
                return;

            var prefabData = await _manager.addressableNetworkPrefabs.LoadPrefabByGuidAsync(guid);
            if (prefabData.prefab)
                SendLoadState(guid, true);
        }

        private void OnClientLoadStateChanged(string guid, bool loaded)
        {
            if (!_manager.networkRules || !_manager.networkRules.AddressablesSyncLoadState)
                return;

            SendLoadState(guid, loaded);
        }

        private void SendLoadState(string guid, bool loaded)
        {
            _playersManager.SendToServer(new AddressableLoadStatePacket
            {
                guid = new StringUTF8(guid ?? string.Empty),
                loaded = loaded
            });
        }

        private void OnLoadStateReceived(PlayerID player, AddressableLoadStatePacket packet, bool asServer)
        {
            if (!asServer)
                return;

            if (!_clientLoadedGuids.TryGetValue(player, out var set))
            {
                set = new HashSet<string>();
                _clientLoadedGuids[player] = set;
            }

            var guid = packet.guid.value ?? string.Empty;

            if (packet.loaded)
                set.Add(guid);
            else
                set.Remove(guid);

            onClientLoadStateChanged?.Invoke(player, guid, packet.loaded);

            if (packet.loaded && _manager.TryGetModule<HierarchyFactory>(true, out var factory))
                factory.EvaluateVisibilityForPlayer(player);
        }
        
        /// <summary>
        /// Gets which GUIDs the given client has loaded
        /// </summary>
        /// <param name="player">Player to search for loaded GUIDs</param>
        /// <returns>Which GUIDs the player has confirmed loaded</returns>
        public IReadOnlyCollection<string> GetLoadedGuidsForPlayer(PlayerID player)
        {
            return _clientLoadedGuids.TryGetValue(player, out var set) ? set : (IReadOnlyCollection<string>)Array.Empty<string>();
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!asServer)
                return;
            
            _clientLoadedGuids.Remove(player);
        }
        
        /// <summary>
        /// Returns all players that have reported the given Addressable GUID as loaded.
        /// </summary>
        public IReadOnlyList<PlayerID> GetPlayersWithGuidLoaded(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return Array.Empty<PlayerID>();

            var result = new List<PlayerID>();
            foreach (var (player, guids) in _clientLoadedGuids)
            {
                if (guids.Contains(guid))
                    result.Add(player);
            }
            return result;
        }

        /// <summary>
        /// Checks whether a client has loaded a specific GUID
        /// </summary>
        public bool ClientHasLoaded(PlayerID player, string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid))
                return true;

            if (!_clientLoadedGuids.TryGetValue(player, out var set))
                return false;

            return set.Contains(assetGuid);
        }

        /// <summary>
        /// Sends the request to a player to prepare/warmup/fetch a specific GUID
        /// </summary>
        public void RequestPlayerToLoad(PlayerID player, string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || player.isServer)
                return;

            _playersManager.Send(player, new AddressableLoadRequestPacket
            {
                guid = new StringUTF8(assetGuid)
            });
        }
    }
}
#endif
