using JetBrains.Annotations;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine.Scripting;

namespace PurrNet
{
    [MeansImplicitUse]
    public class TargetRpcAttribute : PreserveAttribute
    {
        [UsedByIL]
        public TargetRpcAttribute(
            Channel channel = Channel.ReliableOrdered,
            bool runLocally = false,
            bool bufferLast = false,
            bool requireServer = true,
            CompressionLevel compressionLevel = CompressionLevel.None,
            float asyncTimeoutInSec = 5f,
            bool deltaPacked = false,
            MTUBehaviour mtuExceeded = MTUBehaviour.NetworkManager,
            bool immediate = false)
        {
        }
    }
}
