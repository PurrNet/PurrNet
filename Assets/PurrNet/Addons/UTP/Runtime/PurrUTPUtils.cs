using System.Net;
using JetBrains.Annotations;
using PurrNet.Logging;

namespace PurrNet.UTP
{
    /// <summary>
    /// Utility methods for PurrNet UTP transport operations.
    /// </summary>
    public static class PurrUTPUtils
    {
        /// <summary>
        /// Converts an IPv4 address string to its 32-bit unsigned integer representation.
        /// </summary>
        /// <param name="address">The IPv4 address string to convert (e.g., "192.168.1.1").</param>
        /// <returns>The 32-bit unsigned integer representation of the IPv4 address, or 0 if parsing fails.</returns>
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
                
                if (result.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) 
                {
                    PurrLogger.LogError($"Address {address} is not a valid IPv4 address.");
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