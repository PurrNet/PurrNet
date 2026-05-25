public struct BenchmarkMetrics
{
    public bool measured;
    public double windowSeconds;

    public ulong windowBytesSent;
    public ulong windowBytesReceived;
    public double sentBytesPerSec;
    public double receivedBytesPerSec;

    public int connectionCount;
    public int objectCount;

    public double serverCpuPercent;
    public double avgTickMs;
    public double maxTickMs;
    public long peakMemoryBytes;

    public int rttSamples;
    public double rttP50Ms;
    public double rttP95Ms;
    public double rttP99Ms;
}
