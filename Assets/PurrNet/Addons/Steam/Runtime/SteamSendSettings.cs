#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

using System;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace PurrNet.Steam
{
    internal readonly struct SteamSendSettings
    {
        internal const int DefaultRateKiBPerSecond = 256;
        internal const int DefaultBufferSizeKiB = 512;
        internal const int ReliableMessageSizeBytes = 16 * 1024;
        internal const int MinBufferSizeKiB = ReliableMessageSizeBytes / 1024;
        internal const int MaxKiB = int.MaxValue / 1024;

        internal static SteamSendSettings defaults =>
            new SteamSendSettings(DefaultRateKiBPerSecond, DefaultBufferSizeKiB);

        internal readonly int rateBytesPerSecond;
        internal readonly int bufferSizeBytes;

        internal SteamSendSettings(int rateKiBPerSecond, int bufferSizeKiB)
        {
            rateBytesPerSecond = ToBytes(rateKiBPerSecond, nameof(rateKiBPerSecond));
            bufferSizeBytes = BufferSizeToBytes(bufferSizeKiB, nameof(bufferSizeKiB));
        }

        internal static int BufferSizeToBytes(int kibibytes, string parameterName)
        {
            // A packet larger than the native buffer can never succeed on retry.
            if (kibibytes < MinBufferSizeKiB || kibibytes > MaxKiB)
                throw new ArgumentOutOfRangeException(parameterName, kibibytes,
                    $"Steam send buffer size must be between {MinBufferSizeKiB} and {MaxKiB} KiB " +
                    "to fit a full reliable packet.");
            return checked(kibibytes * 1024);
        }

        internal static int ToBytes(int kibibytes, string parameterName)
        {
            if (kibibytes < 1 || kibibytes > MaxKiB)
                throw new ArgumentOutOfRangeException(parameterName, kibibytes,
                    $"Steam send settings must be between 1 and {MaxKiB} KiB (or KiB/s). Zero does not mean unlimited.");
            return checked(kibibytes * 1024);
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        internal SteamNetworkingConfigValue_t[] CreateOptions()
        {
            return new[]
            {
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, rateBytesPerSecond),
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, rateBytesPerSecond),
                Int32Option(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, bufferSizeBytes)
            };
        }

        private static SteamNetworkingConfigValue_t Int32Option(ESteamNetworkingConfigValue key, int value)
        {
            return new SteamNetworkingConfigValue_t
            {
                m_eValue = key,
                m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = value }
            };
        }

#endif
    }
}
