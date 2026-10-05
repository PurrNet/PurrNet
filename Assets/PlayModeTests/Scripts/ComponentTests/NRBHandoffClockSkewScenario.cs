using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class NRBHandoffClockSkewScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _warmupSeconds = 1.2f;
    [SerializeField] private float _phaseSeconds = 1.6f;

    private const int BarrierSkew = 9800;
    private const int BarrierSpawn = 9801;
    private const int BarrierMotionEnd = 9802;
    private const int BarrierEnd = 9803;

    private const double ServerSkew = 20d;
    private const double EvenClientSkew = 5.825d;
    private const double OddClientSkew = 37.3d;

    private const double LeadLimit = 0.25;
    private const float MinProgress = 1.0f;
    private const float WindowSeconds = 1.25f;

    private NRBHandoffMover _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(NRBHandoffMover));
        go.SetActive(false);
        var body = go.AddComponent<Rigidbody>();
        body.useGravity = false;
        _prefab = go.AddComponent<NRBHandoffMover>();
        go.AddComponent<NetworkRigidbody>();
        NRBHandoffMover.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkRigidbodyBase.clockSkewForTests = ctx.isServer
            ? ServerSkew
            : ctx.networkManager.localPlayer.id.value % 2 == 0 ? EvenClientSkew : OddClientSkew;

        try
        {
            return await Run(ctx);
        }
        finally
        {
            NetworkRigidbodyBase.clockSkewForTests = 0;
        }
    }

    private async UniTask<ScenarioResult> Run(ScenarioContext ctx)
    {
        await ScenarioBarrier.Wait(ctx, BarrierSkew, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab, new Vector3(0f, 50f, 200f), Quaternion.identity); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NRBHandoffMover.localInstance && NRBHandoffMover.localInstance.isSpawned,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("mover never spawned");
        }

        await ScenarioBarrier.Wait(ctx, BarrierSpawn, _barrierTimeoutSeconds);

        var mover = NRBHandoffMover.localInstance;
        mover.Begin();

        if (ctx.isServer)
        {
            var clients = new List<PlayerID>();
            var players = ctx.networkManager.players;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].isServer && players[i] != ctx.networkManager.localPlayer)
                    clients.Add(players[i]);
            }
            clients.Sort((a, b) => a.id.value.CompareTo(b.id.value));

            if (clients.Count < 2)
                return ScenarioResult.Fail($"need at least 2 external clients, got {clients.Count}");

            var nrb = mover.GetComponent<NetworkRigidbody>();

            await UniTask.WaitForSeconds(_warmupSeconds, cancellationToken: ctx.cancellationToken);
            nrb.GiveOwnership(clients[0]);
            await UniTask.WaitForSeconds(_phaseSeconds, cancellationToken: ctx.cancellationToken);
            nrb.GiveOwnership(clients[1]);
            await UniTask.WaitForSeconds(_phaseSeconds, cancellationToken: ctx.cancellationToken);
            nrb.RemoveOwnership();
            await UniTask.WaitForSeconds(_phaseSeconds, cancellationToken: ctx.cancellationToken);
        }

        await ScenarioBarrier.Wait(ctx, BarrierMotionEnd, _barrierTimeoutSeconds);

        mover.End();

        var failures = new List<string>();
        Evaluate(mover.samples, failures);

        if (ctx.isServer)
            Destroy(mover.gameObject);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !NRBHandoffMover.localInstance,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("mover never despawned");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok("observers kept one timeline across skewed-clock handoffs")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void Evaluate(List<NRBHandoffMover.Sample> samples, List<string> failures)
    {
        double maxLead = 0;
        float maxLeadTime = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].controller || samples[i].snapshotLead <= maxLead)
                continue;
            maxLead = samples[i].snapshotLead;
            maxLeadTime = samples[i].time - (samples.Count > 0 ? samples[0].time : 0f);
        }

        if (maxLead > LeadLimit)
            failures.Add($"observer buffered a snapshot {maxLead:F3}s in its future at t={maxLeadTime:F2}s (limit {LeadLimit:F3}s)");

        int transitions = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var cur = samples[i];

            if (prev.hasOwner == cur.hasOwner && (!cur.hasOwner || prev.ownerId == cur.ownerId))
                continue;

            transitions++;

            if (cur.controller)
                continue;

            EvaluateProgress(samples, i, transitions, failures);
        }

        if (transitions == 0)
            failures.Add("no ownership transitions observed");
    }

    private static void EvaluateProgress(List<NRBHandoffMover.Sample> samples, int start, int transition,
        List<string> failures)
    {
        float t0 = samples[start].time;
        float first = samples[start].x;
        float last = first;
        int observed = 0;

        for (int i = start + 1; i < samples.Count; i++)
        {
            var cur = samples[i];
            if (cur.time - t0 > WindowSeconds)
                break;
            if (cur.controller)
                continue;

            last = cur.x;
            observed++;
        }

        if (observed < 10)
        {
            failures.Add($"transition {transition}: only {observed} observer samples in the window");
            return;
        }

        float net = last - first;
        if (net < MinProgress)
            failures.Add(
                $"transition {transition}: only {net:F3}m of forward progress in {WindowSeconds:F2}s (limit {MinProgress:F3}m)");
    }
}
