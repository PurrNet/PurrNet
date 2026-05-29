using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet.Modules;
using PurrNet.Profiler;
using PurrNet.Transports;
using UnityEngine;

/// <summary>
/// Shared measurement window for benchmark scenarios: warmup, start/end barriers, the steady-state
/// sample loop, native bandwidth + RPC/broadcast attribution + frame/GC sampling, RTT pings, and the
/// final <see cref="BenchmarkMetrics"/> build. Scenarios supply the load via callbacks.
/// </summary>
public static class BenchmarkHarness
{
    private const float WARMUP_SECONDS = 2f;
    private const float BARRIER_TIMEOUT = 120f;

    // Per-datagram UDP + IPv4 header overhead, added on top of LiteNetLib's socket-byte count
    // to estimate true on-wire usage (LiteNetLib counts framing but not the IP/UDP headers).
    private const long UDP_IPV4_HEADER_BYTES = 28;

    public static async UniTask<BenchmarkMetrics> RunWindow(
        ScenarioContext ctx,
        int barrierStart,
        int barrierEnd,
        float pingsPerSecond,
        Func<UniTask> onSpawn,
        Action<float, float> onTick,
        Action onDespawn,
        Func<int> objectCount)
    {
        var transport = ctx.networkManager.transport.transport;
        var udp = transport as UDPTransport;
        udp?.SetStatisticsEnabled(true);

        // Cap the frame loop to the network tick rate so CPU% measures real work instead of an
        // uncapped spin loop. Frame p95/p99 then reports tick-budget adherence; over = overload.
        if (ctx.networkManager.TryGetModule<TickManager>(ctx.isServer, out var tickManager) && tickManager.tickRate > 0)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = tickManager.tickRate;
        }

        ulong sent = 0, received = 0;
        OnDataSent onSent = (_, d, _) => sent += (ulong)d.length;
        OnDataReceived onRecv = (_, d, _) => received += (ulong)d.length;

        if (onSpawn != null)
            await onSpawn();

        await UniTask.WaitForSeconds(WARMUP_SECONDS, cancellationToken: ctx.cancellationToken);
        await ScenarioBarrier.Wait(ctx, barrierStart, BARRIER_TIMEOUT);

        var sampler = new ServerLoadSampler();
        var cpu = new CpuProfileSampler();
        sampler.Begin();
        cpu.Begin();
        BenchmarkPing.BeginCollection();
        Statistics.BeginAggregation();

        long nativeSent0 = udp?.nativeBytesSent ?? 0;
        long nativeRecv0 = udp?.nativeBytesReceived ?? 0;
        long nativePktSent0 = udp?.nativePacketsSent ?? 0;
        long nativePktRecv0 = udp?.nativePacketsReceived ?? 0;
        long nativeLoss0 = udp?.nativePacketLoss ?? 0;

        transport.onDataSent += onSent;
        transport.onDataReceived += onRecv;

        double windowSeconds = Math.Max(1.0, ctx.benchSeconds);
        double pingInterval = pingsPerSecond > 0 ? 1.0 / pingsPerSecond : 0;
        double nextPing = 0;
        double elapsed = 0;

        try
        {
            while (elapsed < windowSeconds)
            {
                await UniTask.NextFrame(ctx.cancellationToken);
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;

                sampler.SampleFrame();
                cpu.Sample();
                onTick?.Invoke((float)elapsed, dt);

                if (ctx.isClient && pingInterval > 0)
                {
                    nextPing -= dt;
                    if (nextPing <= 0)
                    {
                        BenchmarkPing.Send();
                        nextPing = pingInterval;
                    }
                }
            }
        }
        finally
        {
            transport.onDataSent -= onSent;
            transport.onDataReceived -= onRecv;
        }

        var load = sampler.End();
        var cpuMarkers = cpu.End();
        var rtts = BenchmarkPing.StopAndGet();
        var breakdown = Statistics.EndAggregation();

        long nativeSent = (udp?.nativeBytesSent ?? 0) - nativeSent0;
        long nativeRecv = (udp?.nativeBytesReceived ?? 0) - nativeRecv0;
        long nativePktSent = (udp?.nativePacketsSent ?? 0) - nativePktSent0;
        long nativePktRecv = (udp?.nativePacketsReceived ?? 0) - nativePktRecv0;
        long nativeLoss = (udp?.nativePacketLoss ?? 0) - nativeLoss0;

        int objCount = objectCount?.Invoke() ?? 0;

        await ScenarioBarrier.Wait(ctx, barrierEnd, BARRIER_TIMEOUT);

        onDespawn?.Invoke();

        var metrics = new BenchmarkMetrics
        {
            measured = ctx.measured,
            windowSeconds = windowSeconds,
            windowBytesSent = sent,
            windowBytesReceived = received,
            sentBytesPerSec = sent / windowSeconds,
            receivedBytesPerSec = received / windowSeconds,

            nativeSentBytesPerSec = nativeSent / windowSeconds,
            nativeReceivedBytesPerSec = nativeRecv / windowSeconds,
            nativePacketsSentPerSec = nativePktSent / windowSeconds,
            nativePacketsReceivedPerSec = nativePktRecv / windowSeconds,
            onWireSentBytesPerSec = (nativeSent + nativePktSent * UDP_IPV4_HEADER_BYTES) / windowSeconds,
            onWireReceivedBytesPerSec = (nativeRecv + nativePktRecv * UDP_IPV4_HEADER_BYTES) / windowSeconds,
            framingOverheadPercent = sent > 0 ? ((double)nativeSent / sent - 1.0) * 100.0 : 0,
            packetLoss = nativeLoss,

            connectionCount = ctx.networkManager.playerCount,
            objectCount = objCount,

            serverCpuPercent = load.cpuPercent,
            avgTickMs = load.avgFrameMs,
            maxTickMs = load.maxFrameMs,
            minTickMs = load.minFrameMs,
            p95TickMs = load.p95FrameMs,
            p99TickMs = load.p99FrameMs,
            avgFps = load.avgFps,
            peakMemoryBytes = load.peakMemoryBytes,
            managedHeapBytes = load.managedHeapBytes,
            gcCollections = load.gcCollections,

            bandwidthBreakdown = breakdown.ToArray(),
            cpuMarkers = cpuMarkers
        };

        FillRttPercentiles(rtts, ref metrics);
        return metrics;
    }

    public static string Describe(in BenchmarkMetrics m) =>
        $"conns={m.connectionCount} objects={m.objectCount} payload={m.sentBytesPerSec:F0}B/s " +
        $"native={m.nativeSentBytesPerSec:F0}B/s overhead={m.framingOverheadPercent:F0}% " +
        $"cpu={m.serverCpuPercent:F1}% fps={m.avgFps:F0} rttP95={m.rttP95Ms:F1}ms loss={m.packetLoss}";

    private static void FillRttPercentiles(IReadOnlyList<double> rtts, ref BenchmarkMetrics m)
    {
        m.rttSamples = rtts.Count;
        if (rtts.Count == 0)
            return;

        var sorted = new List<double>(rtts);
        sorted.Sort();
        m.rttP50Ms = Percentile(sorted, 0.50);
        m.rttP95Ms = Percentile(sorted, 0.95);
        m.rttP99Ms = Percentile(sorted, 0.99);
    }

    private static double Percentile(List<double> sorted, double p)
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
}
