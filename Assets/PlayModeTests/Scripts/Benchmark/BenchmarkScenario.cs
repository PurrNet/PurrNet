using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
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

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var transport = ctx.networkManager.transport.transport;

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

                if (ctx.isServer)
                {
                    sampler.SampleFrame();
                    MutateObjects((float)elapsed);
                }

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

        sampler.End(out var cpu, out var avgTick, out var maxTick, out var peakMem);
        var rtts = BenchmarkPing.StopAndGet();

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
            connectionCount = ctx.networkManager.playerCount,
            objectCount = ctx.isServer ? _spawned.Count : _objectCount,
            serverCpuPercent = ctx.isServer ? cpu : 0,
            avgTickMs = avgTick,
            maxTickMs = maxTick,
            peakMemoryBytes = peakMem
        };

        FillRttPercentiles(rtts, ref metrics);
        LastMetrics = metrics;

        return ScenarioResult.Ok(
            $"conns={metrics.connectionCount} sent={metrics.sentBytesPerSec:F0}B/s recv={metrics.receivedBytesPerSec:F0}B/s cpu={metrics.serverCpuPercent:F1}% rttP95={metrics.rttP95Ms:F1}ms");
    }

    private void SpawnObjects()
    {
        HierarchyV2.SupressAutoOwner();
        try
        {
            for (int i = 0; i < _objectCount; i++)
            {
                var inst = Instantiate(_prefab);
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
