using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// A TargetRpc the server sends right before despawning the identity must still reach every
/// target, the host's own client included. Remote clients get the RPC ahead of the despawn on
/// their stream; the host's client shares the server's identity, so the RPC has to be handed over
/// before the despawn removes it instead of looping through the transport.
/// </summary>
public class TargetRpcBeforeDespawnScenario : Scenario
{
    private const int Payload = 4711;

    [SerializeField] private float _observeTimeoutSeconds = 15f;
    [SerializeField] private float _replyTimeoutSeconds = 10f;

    private TargetRpcBeforeDespawn _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var go = new GameObject(nameof(TargetRpcBeforeDespawnScenario));
        _prefab = go.AddComponent<TargetRpcBeforeDespawn>();
        go.SetActive(false);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            Instantiate(_prefab);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => TargetRpcBeforeDespawn.ServerInstance &&
                          TargetRpcBeforeDespawn.ServerInstance.observers.Count >= ctx.expectedConnections,
                    _observeTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                var observed = TargetRpcBeforeDespawn.ServerInstance ? TargetRpcBeforeDespawn.ServerInstance.observers.Count : 0;
                return ScenarioResult.Fail($"only {observed}/{ctx.expectedConnections} observers before despawn");
            }

            TargetRpcBeforeDespawn.ServerInstance.ReplyToEveryoneThenDespawn(Payload);
        }

        if (!ctx.isClient)
            return ScenarioResult.Ok();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => TargetRpcBeforeDespawn.Received.HasValue, _replyTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{ctx.role} never received the TargetRpc sent before the despawn");
        }

        return TargetRpcBeforeDespawn.Received == Payload
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail($"expected {Payload}, got {TargetRpcBeforeDespawn.Received}");
    }
}
