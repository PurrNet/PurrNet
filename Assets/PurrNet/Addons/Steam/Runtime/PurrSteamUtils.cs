using System.Net;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;

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

        public static ulong GetSteamID(this NetworkManager networkManager, PlayerID playerID)
        {
            bool asServer = networkManager.isServer;

            if (networkManager.TryGetModule<PlayersManager>(asServer, out var playersManager) &&
                playersManager.TryGetConnection(playerID, out var connection))
            {
                if (networkManager.transport is SteamTransport steamTransport)
                {
                    return steamTransport.GetSteamID(connection);
                }
            }

            return 0;
        }
    }
}