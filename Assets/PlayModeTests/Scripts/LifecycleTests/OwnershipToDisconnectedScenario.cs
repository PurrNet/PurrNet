using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class OwnershipToDisconnectedScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _ownershipTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;

    // Bot-flag distinguishes this fabricated id from any real player. Picked above any reasonable
    // server-assigned id so it cannot collide with a connected client. Cookies make this kind of
    // assignment legitimate: a player who has never connected this session may still own state.
    private const ulong DisconnectedPlayerRawId = 999_001;

    private OwnershipToDisconnectedIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(OwnershipToDisconnectedScenario));
        _prefab = go.AddComponent<OwnershipToDisconnectedIdentity>();
        go.SetActive(false);
        OwnershipToDisconnectedIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
            Instantiate(_prefab);

        await UniTaskUtils.WaitWithTimeout(
            () => OwnershipToDisconnectedIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            OwnershipToDisconnectedIdentity.LocalInstance.SignalReady();

        if (!ctx.isServer)
            return ScenarioResult.Ok("client observer");

        var failures = new List<string>();
        var inst = OwnershipToDisconnectedIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnershipToDisconnectedIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {OwnershipToDisconnectedIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var disconnectedPlayer = new PlayerID(DisconnectedPlayerRawId, false);

        // Sanity: the fabricated id must not match any real connected player.
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].id.value == DisconnectedPlayerRawId)
                return ScenarioResult.Fail(
                    $"chosen DisconnectedPlayerRawId={DisconnectedPlayerRawId} collides with a real player");
        }

        inst.GiveOwnership(disconnectedPlayer);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnershipToDisconnectedIdentity.OwnerChangedCount >= 1
                      && OwnershipToDisconnectedIdentity.LastNewOwnerHasValue
                      && OwnershipToDisconnectedIdentity.LastNewOwnerId == DisconnectedPlayerRawId,
                _ownershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"OnOwnerChanged with new owner {DisconnectedPlayerRawId} did not fire " +
                $"(count={OwnershipToDisconnectedIdentity.OwnerChangedCount}, " +
                $"lastHas={OwnershipToDisconnectedIdentity.LastNewOwnerHasValue}, " +
                $"lastId={OwnershipToDisconnectedIdentity.LastNewOwnerId})");
        }

        // Let the assignment settle so isController/hasConnectedOwner observations are stable.
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!inst.hasOwner)
            failures.Add("server: hasOwner=false after GiveOwnership(disconnected)");
        if (!inst.owner.HasValue || inst.owner.Value.id.value != DisconnectedPlayerRawId)
            failures.Add(
                $"server: owner mismatch. got={(inst.owner.HasValue ? inst.owner.Value.id.value.ToString() : "null")}, " +
                $"expected {DisconnectedPlayerRawId}");
        if (inst.hasConnectedOwner)
            failures.Add("server: hasConnectedOwner=true for a never-connected player (cookie/disconnected case)");
        if (!inst.isController)
            failures.Add("server: isController=false; with no connected owner the server must remain controller");
        if (inst.isOwner)
            failures.Add("server: isOwner=true but ownership was assigned to a disconnected player");

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={DisconnectedPlayerRawId}, hasConnectedOwner=false, isController=true")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}
