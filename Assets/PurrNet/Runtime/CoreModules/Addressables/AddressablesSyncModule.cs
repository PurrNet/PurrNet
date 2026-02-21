#if ADDRESSABLES_PURRNET_SUPPORT
using System.Collections.Generic;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Modules
{
    public class AddressablesSyncModule : INetworkModule
    {
        private readonly NetworkManager _manager;
        private readonly PlayersManager _playersManager;

        private readonly Dictionary<PlayerID, HashSet<string>> _clientLoadedGuids = new();

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

            await _manager.addressableNetworkPrefabs.LoadPrefabByGuidAsync(guid);
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

            if (packet.loaded)
                set.Add(packet.guid.value ?? string.Empty);
            else
                set.Remove(packet.guid.value ?? string.Empty);

            if (packet.loaded && _manager.TryGetModule<HierarchyFactory>(true, out var factory))
                factory.EvaluateVisibilityForPlayer(player);
        }

        public void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!asServer)
                return;
            
            _clientLoadedGuids.Remove(player);
        }

        public bool ClientHasLoaded(PlayerID player, string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid))
                return true;

            if (!_clientLoadedGuids.TryGetValue(player, out var set))
                return false;

            return set.Contains(assetGuid);
        }

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
