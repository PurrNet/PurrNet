namespace PurrNet.Steam
{
    /// <summary>Queued bytes exclude Steam's native send queue.</summary>
    [System.Serializable]
    public struct SteamSendStatistics
    {
        public long acceptedMessages;
        public long rejectedSendAttempts;
        public long droppedUnreliableMessages;
        public long deferredReliableMessages;
        public int queuedReliableMessages;
        public int queuedReliableBytes;
        public int lastSendResult;
    }
}
