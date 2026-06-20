using System;
using System.Collections.Generic;
using PurrNet.Transports;

namespace PurrNet.HostMigration
{
    public sealed class HostMigrationContext
    {
        private readonly PlayerID[] _players;

        internal HostMigrationContext(NetworkManager networkManager, PlayerID localPlayer,
            bool hasLocalPlayer, PlayerID[] players, bool wasHost,
            ConnectionState clientState, ConnectionState serverState)
        {
            this.networkManager = networkManager;
            this.localPlayer = localPlayer;
            this.hasLocalPlayer = hasLocalPlayer;
            _players = players ?? Array.Empty<PlayerID>();
            this.wasHost = wasHost;
            this.clientState = clientState;
            this.serverState = serverState;
        }

        public NetworkManager networkManager { get; }

        public PlayerID localPlayer { get; }

        public bool hasLocalPlayer { get; }

        public IReadOnlyList<PlayerID> players => _players;

        public bool wasHost { get; }

        public ConnectionState clientState { get; }

        public ConnectionState serverState { get; }

        public bool ContainsPlayer(PlayerID player)
        {
            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == player)
                    return true;
            }

            return false;
        }
    }
}
