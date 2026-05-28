using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Profiler;
using PurrNet.Transports;
using UnityEngine;

public class BenchmarkScenario : Scenario, IBenchmarkScenario
{
    private const int BARRIER_START = 9001;
    private const int BARRIER_END = 9002;
    private const float WARMUP_SECONDS = 2f;
    private const float BARRIER_TIMEOUT = 120f;

    [SerializeField] private int _objectCount = 50;
    [SerializeField] private float _pingsPerSecond = 10f;

    private NetworkTransform _prefab;
    private readonly List<NetworkTransform> _spawned = new();

    public BenchmarkMetrics? LastMetrics { get; private set; }

    public void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        if (objectCount is > 0)
            _objectCount = objectCount.Value;
        if (pingsPerSecond is > 0)
            _pingsPerSecond = pingsPerSecond.Value;
    }

    void CreatePrefab()
    {
        var go = new GameObject(nameof(BenchmarkScenario) + "_Obj");
        _prefab = go.AddComponent<NetworkTransform>();
        go.SetActive(false);
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    // Per-datagram UDP + IPv4 header overhead, added on top of LiteNetLib's socket-byte count
    // to estimate true on-wire usage. LiteNetLib counts framing but not the IP/UDP headers.
    private const long UDP_IPV4_HEADER_BYTES = 28;

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var transport = ctx.networkManager.transport.transport;
        var udp = transport as UDPTransport;
        udp?.SetStatisticsEnabled(true);

        if (ctx.networkManager.TryGetModule<TickManager>(ctx.isServer, out var tickManager) && tickManager.tickRate > 0)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = tickManager.tickRate;
        }

        ulong sent = 0, received = 0;
        OnDataSent onSent = (_, d, _) => sent += (ulong)d.length;
        OnDataReceived onRecv = (_, d, _) => received += (ulong)d.length;

        if (ctx.isServer)
            SpawnObjects();

        await UniTask.WaitForSeconds(WARMUP_SECONDS, cancellationToken: ctx.cancellationToken);
        await ScenarioBarrier.Wait(ctx, BARRIER_START, BARRIER_TIMEOUT);

        var sampler = new ServerLoadSampler();
        sampler.Begin();
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
        double pingInterval = _pingsPerSecond > 0 ? 1.0 / _pingsPerSecond : 0;
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

                if (ctx.isServer)
                    MutateObjects((float)elapsed);

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
        var rtts = BenchmarkPing.StopAndGet();
        var breakdown = Statistics.EndAggregation();

        long nativeSent = (udp?.nativeBytesSent ?? 0) - nativeSent0;
        long nativeRecv = (udp?.nativeBytesReceived ?? 0) - nativeRecv0;
        long nativePktSent = (udp?.nativePacketsSent ?? 0) - nativePktSent0;
        long nativePktRecv = (udp?.nativePacketsReceived ?? 0) - nativePktRecv0;
        long nativeLoss = (udp?.nativePacketLoss ?? 0) - nativeLoss0;

        int replicatedObjects = ctx.isServer ? _spawned.Count : _objectCount;

        await ScenarioBarrier.Wait(ctx, BARRIER_END, BARRIER_TIMEOUT);

        if (ctx.isServer)
            DespawnObjects();

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
            objectCount = replicatedObjects,

            serverCpuPercent = load.cpuPercent,
            avgTickMs = load.avgFrameMs,
            maxTickMs = load.maxFrameMs,
            minTickMs = load.minFrameMs,
            p95TickMs = load.p95FrameMs,
            p99TickMs = load.p99FrameMs,
            avgFps = load.avgFps,
            peakMemoryBytes = load.peakMemoryBytes,
            managedHeapBytes = load.managedHeapBytes,

            gcGen0 = load.gcGen0,
            gcGen1 = load.gcGen1,
            gcGen2 = load.gcGen2,
            mainThreadAllocBytesPerSec = load.mainThreadAllocBytes / windowSeconds,

            bandwidthBreakdown = breakdown.ToArray()
        };

        FillRttPercentiles(rtts, ref metrics);
        LastMetrics = metrics;

        return ScenarioResult.Ok(
            $"conns={metrics.connectionCount} payload={metrics.sentBytesPerSec:F0}B/s native={metrics.nativeSentBytesPerSec:F0}B/s " +
            $"overhead={metrics.framingOverheadPercent:F0}% cpu={metrics.serverCpuPercent:F1}% fps={metrics.avgFps:F0} rttP95={metrics.rttP95Ms:F1}ms");
    }

    private void SpawnObjects()
    {
        HierarchyV2.SupressAutoOwner();
        try
        {
            for (int i = 0; i < _objectCount; i++)
            {
                var inst = Instantiate(_prefab);
                inst.gameObject.SetActive(true);
                inst.transform.position = new Vector3(i, 0, 0);
                _spawned.Add(inst);
            }
        }
        finally
        {
            HierarchyV2.ResumeAutoOwner();
        }
    }

    private void MutateObjects(float t)
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            var inst = _spawned[i];
            if (!inst)
                continue;
            float phase = i * 0.3f;
            inst.transform.position = new Vector3(
                Mathf.Sin(t + phase) * 5f,
                Mathf.Cos(t * 0.5f + phase) * 5f,
                i);
        }
    }

    private void DespawnObjects()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i])
                Destroy(_spawned[i].gameObject);
        }

        _spawned.Clear();
    }

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
