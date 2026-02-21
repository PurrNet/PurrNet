using System.Net;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;

namespace PurrNet.Steam
{
    public static class PurrSteamUtils
    {
        [UsedImplicitly]
        public static uint GetIPv4(this string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                if (!IPAddress.TryParse(address, out var result))
                {
                    PurrLogger.LogError($"Could not parse address {address} to IPAddress.");
                    return 0;
                }

                var bytes = result.GetAddressBytes();
                int ip = bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3];
                return (uint)ip;
            }

            return 0;
        }
        
        public static bool TryGetSteamID(PlayerID playerID, out ulong steamID)
        {
            steamID = default;

            var networkManager = NetworkManager.main;
            if (networkManager == null)
                return false;

            bool asServer = networkManager.isServer;

            if (networkManager.TryGetModule<PlayersManager>(asServer, out var playersManager) &&
                playersManager.TryGetConnection(playerID, out var connection))
            {
                if (networkManager.transport is SteamTransport steamTransport)
                {
                    steamID = steamTransport.GetSteamID(connection);
                    return steamID != 0;
                }
            }

            return false;
        }
    }
}