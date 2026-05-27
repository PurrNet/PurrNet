using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public class ServerLoadSampler
{
    private double _startCpuSeconds;
    private double _startWallSeconds;
    private float _maxFrameMs;
    private double _sumFrameMs;
    private int _frames;

    public void Begin()
    {
        _startCpuSeconds = ReadProcessCpuSeconds();
        _startWallSeconds = NowSeconds();
        _maxFrameMs = 0;
        _sumFrameMs = 0;
        _frames = 0;
    }

    public void SampleFrame()
    {
        float ms = Time.unscaledDeltaTime * 1000f;
        _sumFrameMs += ms;
        _frames++;
        if (ms > _maxFrameMs)
            _maxFrameMs = ms;
    }

    public void End(out double cpuPercent, out double avgTickMs, out double maxTickMs, out long peakMemoryBytes)
    {
        double cpu = ReadProcessCpuSeconds() - _startCpuSeconds;
        double wall = NowSeconds() - _startWallSeconds;
        cpuPercent = wall > 0 ? cpu / wall * 100.0 : 0;
        avgTickMs = _frames > 0 ? _sumFrameMs / _frames : 0;
        maxTickMs = _maxFrameMs;
        peakMemoryBytes = ReadPeakResidentBytes();
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
        }

        return 0;
    }
}
