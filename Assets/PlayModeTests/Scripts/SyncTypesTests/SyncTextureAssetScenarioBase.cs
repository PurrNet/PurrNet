using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Every process validates one callback per replacement, transported JPEG bytes and decoded pixels.
/// Host has one shared instance and one subscription; a dedicated server validates independently.
/// </summary>
public abstract class SyncTextureAssetScenarioBase : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _transferTimeoutSeconds = 30f;

    protected abstract bool ownerAuthority { get; }
    protected abstract int barrierBase { get; }
    private SyncTextureAssetTestIdentity _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var go = new GameObject(GetType().Name);
        _prefab = go.AddComponent<SyncTextureAssetTestIdentity>();
        _prefab.Configure(ownerAuthority);
        go.SetActive(false);
        SyncTextureAssetTestIdentity.ResetLocalInstance(ownerAuthority);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var evidence = new List<string>();
        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => SyncTextureAssetTestIdentity.GetLocalInstance(ownerAuthority) != null,
            _spawnTimeoutSeconds, ctx.cancellationToken);
        var inst = SyncTextureAssetTestIdentity.GetLocalInstance(ownerAuthority);

        // All peer instances and callback subscriptions exist before ownership or data changes.
        await ScenarioBarrier.Wait(ctx, barrierBase, _readyTimeoutSeconds);
        if (ownerAuthority)
        {
            if (ctx.isServer)
            {
                var owner = PickRemoteOwner(ctx);
                if (!owner.HasValue)
                    return ScenarioResult.Fail("no remote client is available to own the texture");
                inst.GiveOwnership(owner.Value);
            }
            await UniTaskUtils.WaitWithTimeout(
                () => inst.owner.HasValue, _readyTimeoutSeconds, ctx.cancellationToken);
        }

        bool sender = ownerAuthority ? inst.isOwner : ctx.isServer;
        if (inst.IsController(ownerAuthority) != sender)
            failures.Add($"unexpected controller: sender={sender}, controller={inst.IsController(ownerAuthority)}");

        for (int phase = 0; phase < SyncTextureAssetTestData.PhaseCount; phase++)
        {
            inst.Prepare(phase);
            await ScenarioBarrier.Wait(ctx, barrierBase + phase * 2 + 1, _readyTimeoutSeconds);

            if (sender)
            {
                inst.Send();
                if (inst.CallbackCount(phase) != 1)
                    failures.Add($"phase {phase + 1}: sender callback was not synchronous with assetToSync assignment");
            }

            try
            {
                int expectedPhase = phase;
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.CallbackCount(expectedPhase) > 0,
                    _transferTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add($"onDataChanged timeout: {inst.Describe(phase)}");
            }

            // Hold the sender until every client and the server have inspected the current phase.
            // This also checks dedicated servers, which have no client half in RunSplit.
            await ScenarioBarrier.Wait(ctx, barrierBase + phase * 2 + 2, _transferTimeoutSeconds + _readyTimeoutSeconds);
            string failure = inst.Validate(phase);
            if (failure != null)
                failures.Add($"phase {phase + 1}: {failure}");
            evidence.Add(inst.Describe(phase));
            Debug.Log($"[SyncTextureAsset] role={ctx.role}, ownerAuth={ownerAuthority}, sender={sender}, {inst.Describe(phase)}");
        }

        string detail = $"role={ctx.role}, sender={sender}; {string.Join("; ", evidence)}";
        return failures.Count == 0
            ? ScenarioResult.Ok(detail)
            : ScenarioResult.Fail($"{string.Join(" | ", failures)} | {detail}");
    }

    private static PlayerID? PickRemoteOwner(ScenarioContext ctx)
    {
        PlayerID? best = null;
        var manager = ctx.networkManager;
        foreach (var player in manager.players)
        {
            if (player.isServer || (ctx.role == NetworkRole.Host && player == manager.localPlayer))
                continue;
            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }
        return best;
    }
}
