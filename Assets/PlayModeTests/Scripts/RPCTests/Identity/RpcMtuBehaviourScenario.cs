using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

// Proves the per-RPC mtuExceededBehaviour override end to end over the real transport in
// player builds: with the NetworkManager global set to Drop, an oversized unreliable
// ObserversRpc declared Fragment must arrive intact through real fragmentation, one
// declared UpgradeToReliable must arrive, and one without an override must be dropped;
// the client -> server leg is covered by an oversized Fragment-declared ServerRpc.
public class RpcMtuBehaviourScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _deliveryTimeoutSeconds = 20f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _dropGraceSeconds = 2f;

    private const int BarrierBase = 5770;

    private IdentityMtuBehaviourRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(RpcMtuBehaviourScenario));
        go.SetActive(false);
        _prefab = go.AddComponent<IdentityMtuBehaviourRpcs>();
        IdentityMtuBehaviourRpcs.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    static async UniTask WaitSeconds(float seconds, CancellationToken ct)
    {
        float t = 0f;
        while (t < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            t += Time.unscaledDeltaTime;
        }
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        IdentityMtuBehaviourRpcs instance = null;

        if (ctx.isServer)
            instance = Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityMtuBehaviourRpcs.localInstance != null
                      && IdentityMtuBehaviourRpcs.localInstance.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={IdentityMtuBehaviourRpcs.localInstance != null}");
        }

        var manager = ctx.networkManager;
        var prevBehaviour = manager.mtuExceededBehaviour;
        manager.mtuExceededBehaviour = MTUExceededBehaviour.Drop;

        ScenarioResult result;
        try
        {
            if (ctx.isClient)
                IdentityMtuBehaviourRpcs.localInstance.SignalReady();

            result = await RunSplit(ctx, RunAsClient, RunAsServer);
        }
        finally
        {
            manager.mtuExceededBehaviour = prevBehaviour;
        }

        if (!result.success)
            return result;

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityMtuBehaviourRpcs.localInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("per-RPC MTU behaviours held over the real transport in both directions");
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var inst = IdentityMtuBehaviourRpcs.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityMtuBehaviourRpcs.serverReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {IdentityMtuBehaviourRpcs.serverReadyCount}/{ctx.expectedConnections}");
        }

        var defaultPayload = IdentityMtuBehaviourRpcs.MakePayload(IdentityMtuBehaviourRpcs.DefaultSeed);
        var fragmentPayload = IdentityMtuBehaviourRpcs.MakePayload(IdentityMtuBehaviourRpcs.FragmentSeed);
        var upgradePayload = IdentityMtuBehaviourRpcs.MakePayload(IdentityMtuBehaviourRpcs.UpgradeSeed);

        // resend every second so a rare dropped unreliable fragment can't flake the run;
        // clients each answer with SendToServerFragment once their own checks pass
        float waited = 0f;
        while (IdentityMtuBehaviourRpcs.serverFragmentSenders.Count < ctx.expectedConnections)
        {
            if (waited >= _deliveryTimeoutSeconds)
                return ScenarioResult.Fail(
                    $"client->server fragment timeout: {IdentityMtuBehaviourRpcs.serverFragmentSenders.Count}/{ctx.expectedConnections}");

            inst.SendDefaultOversized(defaultPayload);
            inst.SendFragmentOversized(fragmentPayload);
            inst.SendUpgradeOversized(upgradePayload);

            await WaitSeconds(1f, ctx.cancellationToken);
            waited += 1f;
        }

        if (IdentityMtuBehaviourRpcs.serverFragmentCorrupt)
            return ScenarioResult.Fail("client->server fragmented payload arrived corrupted");

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityMtuBehaviourRpcs.serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"done timeout: {IdentityMtuBehaviourRpcs.serverDoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"fragments from {IdentityMtuBehaviourRpcs.serverFragmentSenders.Count} clients");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var inst = IdentityMtuBehaviourRpcs.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityMtuBehaviourRpcs.fragmentReceived && IdentityMtuBehaviourRpcs.upgradeReceived,
                _deliveryTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"oversized delivery timeout: fragment={IdentityMtuBehaviourRpcs.fragmentReceived}, " +
                $"upgrade={IdentityMtuBehaviourRpcs.upgradeReceived}");
        }

        if (IdentityMtuBehaviourRpcs.fragmentCorrupt)
            return ScenarioResult.Fail("fragmented payload arrived corrupted");

        if (IdentityMtuBehaviourRpcs.upgradeCorrupt)
            return ScenarioResult.Fail("upgraded payload arrived corrupted");

        await WaitSeconds(_dropGraceSeconds, ctx.cancellationToken);

        // host loopback bypasses the transport MTU, so only pure clients can prove the drop
        if (ctx.role == NetworkRole.Client && IdentityMtuBehaviourRpcs.defaultReceived)
            return ScenarioResult.Fail("oversized RPC without an override was delivered despite global Drop");

        var toServerPayload = IdentityMtuBehaviourRpcs.MakePayload(IdentityMtuBehaviourRpcs.ToServerSeed);

        float waited = 0f;
        while (!IdentityMtuBehaviourRpcs.phaseDoneReceived)
        {
            if (waited >= _deliveryTimeoutSeconds)
                return ScenarioResult.Fail("client did not receive BroadcastPhaseDone");

            inst.SendToServerFragment(toServerPayload);
            await WaitSeconds(1f, ctx.cancellationToken);
            waited += 1f;
        }

        IdentityMtuBehaviourRpcs.localInstance.SignalDone();

        return ScenarioResult.Ok();
    }
}
