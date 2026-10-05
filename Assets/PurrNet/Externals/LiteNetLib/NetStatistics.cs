using System.Threading;

namespace LiteNetLib
{
    /// <summary>
    /// Thread-safe counter for network statistics including sent/received packets, bytes, and packet loss.
    /// </summary>
    public sealed class NetStatistics
    {
        private long _packetsSent;
        private long _packetsReceived;
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetLoss;
        private long _repairsSent;
        private long _packetsRepaired;

        /// <summary>
        /// Total number of packets sent.
        /// </summary>
        public long PacketsSent => Interlocked.Read(ref _packetsSent);

        /// <summary>
        /// Total number of packets received.
        /// </summary>
        public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

        /// <summary>
        /// Total number of bytes sent.
        /// </summary>
        public long BytesSent => Interlocked.Read(ref _bytesSent);

        /// <summary>
        /// Total number of bytes received.
        /// </summary>
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);

        /// <summary>
        /// Total number of packets lost during transmission.
        /// </summary>
        public long PacketLoss => Interlocked.Read(ref _packetLoss);

        /// <summary>
        /// Repair packets sent to cover reliable packets lost on the way.
        /// </summary>
        public long RepairsSent => Interlocked.Read(ref _repairsSent);

        /// <summary>
        /// Reliable packets rebuilt from repairs instead of waiting for a resend.
        /// </summary>
        public long PacketsRepaired => Interlocked.Read(ref _packetsRepaired);

        /// <summary>
        /// Percentage of sent packets that were lost. 
        /// Calculated as (PacketLoss * 100) / PacketsSent.
        /// </summary>
        public long PacketLossPercent
        {
            get
            {
                long sent = PacketsSent, loss = PacketLoss;

                return sent == 0 ? 0 : loss * 100 / sent;
            }
        }

        /// <summary>
        /// Resets all statistical counters to zero.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _packetsSent, 0);
            Interlocked.Exchange(ref _packetsReceived, 0);
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            Interlocked.Exchange(ref _packetLoss, 0);
            Interlocked.Exchange(ref _repairsSent, 0);
            Interlocked.Exchange(ref _packetsRepaired, 0);
        }

        /// <summary>
        /// Increments the count of sent packets by one.
        /// </summary>
        public void IncrementPacketsSent() =>
            Interlocked.Increment(ref _packetsSent);

        /// <summary>
        /// Increments the count of received packets by one.
        /// </summary>
        public void IncrementPacketsReceived() =>
            Interlocked.Increment(ref _packetsReceived);

        /// <summary>
        /// Adds a specific amount to the total bytes sent.
        /// </summary>
        /// <param name="bytesSent">Number of bytes to add.</param>
        public void AddBytesSent(long bytesSent) =>
            Interlocked.Add(ref _bytesSent, bytesSent);

        /// <summary>
        /// Adds a specific amount to the total bytes received.
        /// </summary>
        /// <param name="bytesReceived">Number of bytes to add.</param>
        public void AddBytesReceived(long bytesReceived) =>
            Interlocked.Add(ref _bytesReceived, bytesReceived);

        /// <summary>
        /// Increments the count of lost packets by one.
        /// </summary>
        public void IncrementPacketLoss() =>
            Interlocked.Increment(ref _packetLoss);

        /// <summary>
        /// Adds a specific amount to the total packet loss count.
        /// </summary>
        /// <param name="packetLoss">Number of lost packets to add.</param>
        public void AddPacketLoss(long packetLoss) =>
            Interlocked.Add(ref _packetLoss, packetLoss);

        /// <summary>
        /// Increments the count of repair packets sent by one.
        /// </summary>
        public void IncrementRepairsSent() =>
            Interlocked.Increment(ref _repairsSent);

        /// <summary>
        /// Increments the count of reliable packets rebuilt from repairs by one.
        /// </summary>
        public void IncrementPacketsRepaired() =>
            Interlocked.Increment(ref _packetsRepaired);

        /// <summary>
        /// Returns a string representation of the current network statistics.
        /// </summary>
        /// <returns>A formatted string containing bytes received/sent, packets received/sent, and loss information.</returns>
        public override string ToString()
        {
            return
                string.Format(
                    "BytesReceived: {0}\nPacketsReceived: {1}\nBytesSent: {2}\nPacketsSent: {3}\nPacketLoss: {4}\nPacketLossPercent: {5}\nRepairsSent: {6}\nPacketsRepaired: {7}\n",
                    BytesReceived,
                    PacketsReceived,
                    BytesSent,
                    PacketsSent,
                    PacketLoss,
                    PacketLossPercent,
                    RepairsSent,
                    PacketsRepaired);
        }
    }
}
