using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public struct ServerLoadStats
{
    public double cpuPercent;
    public double avgFrameMs;
    public double minFrameMs;
    public double maxFrameMs;
    public double p95FrameMs;
    public double p99FrameMs;
    public double avgFps;
    public long peakMemoryBytes;
    public int frameCount;

    // Number of GC collections during the window. Under IL2CPP's (non-generational) Boehm GC
    // all generations report the same count, so this is the single meaningful GC-pressure proxy;
    // a true allocation rate (bytes/s) isn't available in a release IL2CPP build.
    public int gcCollections;
    public long managedHeapBytes;
}

public class ServerLoadSampler
{
    private double _startCpuSeconds;
    private double _startWallSeconds;
    private readonly List<float> _frameMs = new();

    private int _startGcCollections;

    public void Begin()
    {
        _startCpuSeconds = ReadProcessCpuSeconds();
        _startWallSeconds = NowSeconds();
        _frameMs.Clear();

        _startGcCollections = GC.CollectionCount(0);
    }

    public void SampleFrame()
    {
        _frameMs.Add(Time.unscaledDeltaTime * 1000f);
    }

    public ServerLoadStats End()
    {
        double cpu = ReadProcessCpuSeconds() - _startCpuSeconds;
        double wall = NowSeconds() - _startWallSeconds;

        var stats = new ServerLoadStats
        {
            cpuPercent = wall > 0 ? cpu / wall * 100.0 : 0,
            peakMemoryBytes = ReadPeakResidentBytes(),
            frameCount = _frameMs.Count,
            gcCollections = GC.CollectionCount(0) - _startGcCollections,
            managedHeapBytes = GC.GetTotalMemory(false)
        };

        if (_frameMs.Count > 0)
        {
            double sum = 0;
            for (int i = 0; i < _frameMs.Count; i++)
                sum += _frameMs[i];

            var sorted = new List<float>(_frameMs);
            sorted.Sort();

            stats.avgFrameMs = sum / _frameMs.Count;
            stats.minFrameMs = sorted[0];
            stats.maxFrameMs = sorted[^1];
            stats.p95FrameMs = Percentile(sorted, 0.95);
            stats.p99FrameMs = Percentile(sorted, 0.99);
            stats.avgFps = stats.avgFrameMs > 0 ? 1000.0 / stats.avgFrameMs : 0;
        }

        return stats;
    }

    private static double Percentile(List<float> sorted, double p)
    {
        if (sorted.Count == 1)
            return sorted[0];

        double rank = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi)
            return sorted[lo];

        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    private static double NowSeconds() => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

    private static double ReadProcessCpuSeconds()
    {
        try
        {
            var stat = File.ReadAllText("/proc/self/stat");
            int close = stat.LastIndexOf(')');
            var rest = stat.Substring(close + 2).Split(' ');
            double utime = double.Parse(rest[11], CultureInfo.InvariantCulture);
            double stime = double.Parse(rest[12], CultureInfo.InvariantCulture);
            return (utime + stime) / 100.0;
        }
        catch
        {
            return 0;
        }
    }

    private static long ReadPeakResidentBytes()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/self/status"))
            {
                if (!line.StartsWith("VmHWM:"))
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                return long.Parse(parts[1], CultureInfo.InvariantCulture) * 1024L;
            }
        }
        catch
        {
            // ignored
        }

        return 0;
    }
}
