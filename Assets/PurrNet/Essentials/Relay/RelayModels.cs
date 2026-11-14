using System;

namespace PurrNet.Essentials.Relay
{
    public enum RelayDelivery : byte
    {
        ReliableUnordered = 0,
        Sequenced = 1,
        ReliableOrdered = 2,
        ReliableSequenced = 3,
        Unreliable = 4
    }

    public enum RelayState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public enum RelayCloseReason
    {
        Stopped,
        ConnectionLost,
        AuthenticationFailed,
        Refused,
        Error
    }

    [Serializable]
    public struct RelayEndpoint
    {
        public string apiEndpoint;
        public string host;
        public int udpPortV2;
        public int webSocketsPort;
        public string region;
    }

    [Serializable]
    public class RelayBudget
    {
        [Serializable]
        public class Players
        {
            public int used;
            public int allowed;
        }

        [Serializable]
        public class Traffic
        {
            public long usedBytes;
            public long allowedBytes;
            public string month;
        }

        public Players players;
        public Traffic traffic;

        public bool isValid => players != null && traffic != null && !string.IsNullOrEmpty(traffic.month);
    }

    [Serializable]
    public struct RelayJoinDetails
    {
        public bool ssl;
        public string secret;
        public string host;
        public int port;
        public int udpPortV2;
        public string roomName;
        public string webRtcUrl;
        public RelayBudget usage;
        public int migrationGeneration;
    }

    public sealed class RelayRefusedException : Exception
    {
        public string code { get; }
        public RelayBudget budget { get; }

        public RelayRefusedException(string code, string message, RelayBudget budget) : base(message)
        {
            this.code = code;
            this.budget = budget;
        }
    }

    public sealed class RelayOptions
    {
        public const string DefaultMasterServer = "https://purrtransport.purrservers.com/";

        public string masterServer = DefaultMasterServer;
        public string roomName;
        public string region;
        public string host;
        public string projectKey;
        public float timeoutSeconds = 5f;
        public bool forceWebSocket;
        public float keepAliveSeconds = 5f;
    }
}
