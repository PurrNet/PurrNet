using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// How the client connection ended.
    /// </summary>
    public readonly struct ClientDisconnectInfo
    {
        /// <summary>The connection that ended.</summary>
        public Connection connection { get; }

        /// <summary>The reason reported by the transport.</summary>
        public DisconnectReason reason { get; }

        /// <summary>
        /// True when this process asked for the disconnect (StopClient, or a promotion or transfer that restarted
        /// the client). False when the server or the network ended the connection.
        /// </summary>
        public bool wasLocalRequest { get; }

        public ClientDisconnectInfo(Connection connection, DisconnectReason reason, bool wasLocalRequest)
        {
            this.connection = connection;
            this.reason = reason;
            this.wasLocalRequest = wasLocalRequest;
        }
    }

    public sealed partial class NetworkManager
    {
        /// <summary>
        /// Fires when the client connection ends, with whether this process asked for it. A lost host shows up
        /// here with <see cref="ClientDisconnectInfo.wasLocalRequest"/> false.
        /// </summary>
        public event Action<ClientDisconnectInfo> onClientDisconnected;

        // Set by every local request to stop the client; consumed by the next client disconnect.
        private bool _clientStopRequested;

        private void RaiseClientDisconnected(Connection conn, DisconnectReason reason)
        {
            bool local = _clientStopRequested;
            _clientStopRequested = false;

            try
            {
                onClientDisconnected?.Invoke(new ClientDisconnectInfo(conn, reason, local));
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        /// <summary>
        /// When true, a PromoteToServer or TransferToNewServer that gives up (time limit, CancelHostMigration, or
        /// an error) keeps the client state preserved as if <see cref="PreserveClientStateForHostMigration"/> had
        /// been called again, so another attempt can start from the same world. Call
        /// <see cref="ReleaseClientStateForHostMigration"/> to give up for good. False, the default, cleans the
        /// client up after a failed attempt.
        /// </summary>
        public bool keepClientStateOnFailedHostMigration { get; set; }

        /// <summary>True while client state is kept for a host migration that has not started or completed.</summary>
        public bool isClientStatePreservedForHostMigration => _preserveClientStateForHostMigration;

        // ---- Roster of the players a promoted host waits for ----

        private readonly List<PlayerID> _hostMigrationPendingPlayers = new List<PlayerID>();
        private double _hostMigrationRosterDeadline;
        private int _hostMigrationRosterVersion;

        /// <summary>
        /// On a promoted host: how long players from the old session have to reconnect before they count as
        /// departed, in seconds. 0 waits until each is confirmed with
        /// <see cref="ConfirmHostMigrationPlayerDeparture"/>. Defaults to 30.
        /// </summary>
        public float hostMigrationRejoinTimeout { get; set; } = 30f;

        /// <summary>
        /// On a promoted host: players from the old session that have not reconnected yet, the promoted player's
        /// own client included when it migrates as host. Empty once the roster settled, and on every other peer.
        /// A listen host's own player is in it too, since a client cannot tell which player hosted: a migration
        /// system that knows the old host is gone confirms it with <see cref="ConfirmHostMigrationPlayerDeparture"/>.
        /// </summary>
        public IReadOnlyList<PlayerID> hostMigrationPendingPlayers => _hostMigrationPendingPlayers;

        /// <summary>True on a promoted host while some players from the old session have not reconnected or departed.</summary>
        public bool isHostMigrationRosterPending => _hostMigrationPendingPlayers.Count > 0;

        /// <summary>
        /// On a promoted host: a player from the old session reconnected. Fires after the player joined event.
        /// </summary>
        public event Action<PlayerID> onHostMigrationPlayerReturned;

        /// <summary>
        /// On a promoted host: a player from the old session will not be waited for any longer, because the
        /// rejoin timeout passed or <see cref="ConfirmHostMigrationPlayerDeparture"/> was called.
        /// </summary>
        public event Action<PlayerID> onHostMigrationPlayerDeparted;

        /// <summary>
        /// On a promoted host: every player from the old session has returned or departed. Also fires right after
        /// the promotion when nobody else was in the session.
        /// </summary>
        public event Action onHostMigrationRosterSettled;

        /// <summary>True when <paramref name="player"/> is from the old session and has not reconnected to this promoted host yet.</summary>
        public bool IsHostMigrationPlayerPending(PlayerID player) => _hostMigrationPendingPlayers.Contains(player);

        /// <summary>
        /// Stops waiting for <paramref name="player"/> on a promoted host, for example when the lobby says they left.
        /// Returns false when the player was not pending.
        /// </summary>
        public bool ConfirmHostMigrationPlayerDeparture(PlayerID player)
        {
            if (!_hostMigrationPendingPlayers.Remove(player))
                return false;

            RaiseRosterEvent(onHostMigrationPlayerDeparted, player);
            SettleRosterIfDone();
            return true;
        }

        // Taken while the promoted client still sees the old session's player list. The promoted player is
        // expected too when its own client reconnects as host.
        private void CaptureHostMigrationRoster(PlayerID? promotedLocalPlayer)
        {
            _hostMigrationPendingPlayers.Clear();
            _hostMigrationRosterVersion++;

            bool localRejoins = _networkRules && _networkRules.ShouldMigrateAsHost();
            var known = players;
            for (var i = 0; i < known.Count; i++)
            {
                var player = known[i];
                if (player.isBot || player.isServer)
                    continue;
                if (!localRejoins && promotedLocalPlayer.HasValue && player == promotedLocalPlayer.Value)
                    continue;
                if (!_hostMigrationPendingPlayers.Contains(player))
                    _hostMigrationPendingPlayers.Add(player);
            }
        }

        private void StartHostMigrationRoster()
        {
            if (_hostMigrationPendingPlayers.Count == 0)
            {
                RaiseRosterSettled();
                return;
            }

            _hostMigrationRosterDeadline = hostMigrationRejoinTimeout > 0
                ? Time.unscaledTimeAsDouble + hostMigrationRejoinTimeout
                : double.MaxValue;
            WatchHostMigrationRoster(_hostMigrationRosterVersion);
        }

        private async void WatchHostMigrationRoster(int version)
        {
            try
            {
                while (version == _hostMigrationRosterVersion && _hostMigrationPendingPlayers.Count > 0)
                {
                    if (!this || serverState != ConnectionState.Connected)
                    {
                        _hostMigrationPendingPlayers.Clear();
                        return;
                    }

                    if (Time.unscaledTimeAsDouble >= _hostMigrationRosterDeadline)
                    {
                        using var departed = Pooling.DisposableList<PlayerID>.Create(_hostMigrationPendingPlayers);
                        _hostMigrationPendingPlayers.Clear();
                        for (var i = 0; i < departed.Count; i++)
                            RaiseRosterEvent(onHostMigrationPlayerDeparted, departed[i]);
                        RaiseRosterSettled();
                        return;
                    }

                    await UnityLatestUpdate.Yield();
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        // Server-side player joined: a returning player leaves the pending roster.
        private void NoteHostMigrationPlayerJoined(PlayerID player)
        {
            if (!_hostMigrationPendingPlayers.Remove(player))
                return;

            RaiseRosterEvent(onHostMigrationPlayerReturned, player);
            SettleRosterIfDone();
        }

        private void SettleRosterIfDone()
        {
            if (_hostMigrationPendingPlayers.Count == 0)
                RaiseRosterSettled();
        }

        private void RaiseRosterSettled()
        {
            _hostMigrationRosterVersion++;
            try
            {
                onHostMigrationRosterSettled?.Invoke();
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        private static void RaiseRosterEvent(Action<PlayerID> evt, PlayerID player)
        {
            try
            {
                evt?.Invoke(player);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        private void ClearHostMigrationRoster()
        {
            _hostMigrationPendingPlayers.Clear();
            _hostMigrationRosterVersion++;
        }
    }
}
