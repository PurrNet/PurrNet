using System;
using System.Collections.Generic;
using PurrNet;

public static class BenchmarkPing
{
    private static readonly List<double> _rtts = new();
    private static bool _collecting;

    public static void BeginCollection()
    {
        _collecting = true;
        _rtts.Clear();
    }

    public static void Send()
    {
        Ping(DateTime.UtcNow.Ticks);
    }

    public static IReadOnlyList<double> StopAndGet()
    {
        _collecting = false;
        return _rtts;
    }

    [ServerRpc(requireOwnership: false)]
    private static void Ping(long clientTicks, RPCInfo info = default)
    {
        Pong(info.sender, clientTicks);
    }

    [TargetRpc]
    private static void Pong(PlayerID target, long clientTicks)
    {
        if (!_collecting)
            return;
        _rtts.Add((DateTime.UtcNow.Ticks - clientTicks) / (double)TimeSpan.TicksPerMillisecond);
    }
}
