#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using System;
using Steamworks;

namespace PurrNet.Steam
{
    /// <summary>
    /// The Steam networking calls that decide when and why a connection ends: sending on it, reading its status and
    /// closing it. SteamClient and SteamServer make them through this so tests can stand in for Steam.
    /// </summary>
    internal interface ISteamSockets
    {
        EResult SendMessageToConnection(HSteamNetConnection conn, IntPtr data, uint length, int flags);

        EResult GetConnectionRealTimeStatus(HSteamNetConnection conn, ref SteamNetConnectionRealTimeStatus_t status);

        void CloseConnection(HSteamNetConnection conn, string reason);
    }

    internal sealed class SteamSockets : ISteamSockets
    {
        private static readonly SteamSockets _client = new SteamSockets(false);
        private static readonly SteamSockets _gameServer = new SteamSockets(true);

        private readonly bool _dedicated;

        private SteamSockets(bool dedicated)
        {
            _dedicated = dedicated;
        }

        public static ISteamSockets For(bool dedicated) => dedicated ? _gameServer : _client;

        public EResult SendMessageToConnection(HSteamNetConnection conn, IntPtr data, uint length, int flags)
        {
            return _dedicated
                ? SteamGameServerNetworkingSockets.SendMessageToConnection(conn, data, length, flags, out _)
                : SteamNetworkingSockets.SendMessageToConnection(conn, data, length, flags, out _);
        }

        public EResult GetConnectionRealTimeStatus(HSteamNetConnection conn, ref SteamNetConnectionRealTimeStatus_t status)
        {
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return _dedicated
                ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes)
                : SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes);
        }

        public void CloseConnection(HSteamNetConnection conn, string reason)
        {
            if (_dedicated)
                SteamGameServerNetworkingSockets.CloseConnection(conn, 0, reason, false);
            else SteamNetworkingSockets.CloseConnection(conn, 0, reason, false);
        }
    }
}
#endif
