using System.Net;
using JetBrains.Annotations;
using PurrNet.Logging;

namespace PurrNet.UTP
{
    public static class PurrUTPUtils
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
    }
}