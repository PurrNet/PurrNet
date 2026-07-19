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
            MTUExceededBehaviourOverride mtuExceededBehaviour = MTUExceededBehaviourOverride.NetworkManager)
        {
        }
    }
}
