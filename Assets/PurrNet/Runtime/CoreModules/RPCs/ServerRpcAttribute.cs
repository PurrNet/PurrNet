using JetBrains.Annotations;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine.Scripting;

namespace PurrNet
{
    public enum CompressionLevel : int
    {
        None,
        Fast,
        Balanced,
        Best
    }

    [MeansImplicitUse]
    public class ServerRpcAttribute : PreserveAttribute
    {
        [UsedByIL]
        public ServerRpcAttribute(
            Channel channel = Channel.ReliableOrdered,
            bool runLocally = false,
            bool requireOwnership = true,
            CompressionLevel compressionLevel = CompressionLevel.None,
            float asyncTimeoutInSec = 5f,
            StripCodeModeOverride stripCode = StripCodeModeOverride.Settings,
            bool deltaPacked = false,
            MTUBehaviour mtuExceeded = MTUBehaviour.NetworkManager,
            bool immediate = false)
        {
        }
    }
}
