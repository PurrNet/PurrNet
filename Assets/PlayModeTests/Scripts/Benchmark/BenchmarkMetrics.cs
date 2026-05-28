using PurrNet.Profiler;

public struct BenchmarkMetrics
{
    public bool measured;
    public double windowSeconds;

    // PurrNet application-level payload (what was handed to the transport, pre-framing).
    public ulong windowBytesSent;
    public ulong windowBytesReceived;
    public double sentBytesPerSec;
    public double receivedBytesPerSec;

    // LiteNetLib socket-level traffic (framing + ACKs included, UDP/IP headers excluded).
    public double nativeSentBytesPerSec;
    public double nativeReceivedBytesPerSec;
    public double nativePacketsSentPerSec;
    public double nativePacketsReceivedPerSec;
    // Socket bytes plus an estimate of the per-datagram UDP/IPv4 header overhead (28B).
    public double onWireSentBytesPerSec;
    public double onWireReceivedBytesPerSec;
    // native sent / payload sent - 1, in percent. How much the protocol adds on top of payload.
    public double framingOverheadPercent;
    public long packetLoss;

    public int connectionCount;
    public int objectCount;

    public double serverCpuPercent;
    public double avgTickMs;
    public double maxTickMs;
    public double minTickMs;
    public double p95TickMs;
    public double p99TickMs;
    public double avgFps;
    public long peakMemoryBytes;
    public long managedHeapBytes;
    public int gcCollections;

    public int rttSamples;
    public double rttP50Ms;
    public double rttP95Ms;
    public double rttP99Ms;

    public BandwidthEntry[] bandwidthBreakdown;
}
