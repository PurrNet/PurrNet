using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// ValidatedSyncVar contract: the server can authoritatively set accepted values, the owner can
/// optimistically propose values, accepted owner proposals converge everywhere, and rejected owner
/// proposals roll back to the server-authoritative value with a validation-fail callback.
/// </summary>
public class ValidatedSyncVarScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _postRejectVerifyDelaySeconds = 0.25f;

    private ValidatedSyncVarIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ValidatedSyncVarScenario));
        _prefab = go.AddComponent<ValidatedSyncVarIdentity>();
        go.SetActive(false);
        ValidatedSyncVarIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => ValidatedSyncVarIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            ValidatedSyncVarIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = ValidatedSyncVarIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {ValidatedSyncVarIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        inst.SetServerAccepted();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.ServerAcceptedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-accepted timeout: accepted={ValidatedSyncVarIdentity.ServerAcceptedCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        if (!inst.MatchesServerAccepted())
            failures.Add($"server local value != server accepted value: {inst.Describe()}");

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the validated sync var");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.OwnerAcceptedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"owner-accepted timeout: accepted={ValidatedSyncVarIdentity.OwnerAcceptedCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        if (!inst.MatchesOwnerAccepted())
            failures.Add($"server local value != owner accepted value: {inst.Describe()}");

        if (inst.IsController(true))
            failures.Add("server reports IsController(ownerAuth:true)=true for a client-owned validated sync var");

        inst.BroadcastRejectCommand();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.OwnerRejectResolvedCount > 0,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"owner rejection did not resolve; server={inst.Describe()}");
        }

        if (!inst.MatchesOwnerAccepted())
            failures.Add($"rejected value changed server authoritative value: {inst.Describe()}");

        inst.BroadcastVerifyRejected();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.RejectedVerifiedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"rejected verification timeout: verified={ValidatedSyncVarIdentity.RejectedVerifiedCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: {ValidatedSyncVarIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = ValidatedSyncVarIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesServerAccepted(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalServerAccepted();
        }
        catch (TimeoutException)
        {
            failures.Add($"never saw server accepted value; got {inst.Describe()}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designatedOwner = ctx.networkManager.isLocalPlayerReady
                               && ctx.networkManager.localPlayer.id.value == ValidatedSyncVarIdentity.OwnerId;

        if (designatedOwner)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.isOwner,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
                inst.SetOwnerAccepted();
            }
            catch (TimeoutException)
            {
                failures.Add("designated owner never became isOwner");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesOwnerAccepted(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);

            if (designatedOwner)
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.SawOwnerAcceptedValidated,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
            }

            inst.SignalOwnerAccepted();
        }
        catch (TimeoutException)
        {
            failures.Add($"never saw owner accepted value/ack; owner={designatedOwner}, got {inst.Describe()}");
        }

        if (inst.IsController(true) != inst.isOwner)
            failures.Add($"IsController(true)={inst.IsController(true)} but isOwner={inst.isOwner}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.RejectCommandReceived,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastRejectCommand");
        }

        if (designatedOwner)
        {
            inst.SetRejected();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.SawValidationFail && inst.MatchesOwnerAccepted(),
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);

                if (inst.FailedValue != ValidatedSyncVarIdentity.RejectedValue)
                    failures.Add($"validation failed value mismatch: {inst.Describe()}");

                if (inst.AuthoritativeAfterFail != ValidatedSyncVarIdentity.OwnerAcceptedValue)
                    failures.Add($"validation authoritative value mismatch: {inst.Describe()}");

                inst.SignalOwnerRejectResolved();
            }
            catch (TimeoutException)
            {
                failures.Add($"owner rejected value did not roll back; got {inst.Describe()}");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.VerifyRejectedReceived,
                _stateTimeoutSeconds,
                ctx.cancellationToken);

            await UniTask.WaitForSeconds(_postRejectVerifyDelaySeconds, cancellationToken: ctx.cancellationToken);

            if (!inst.MatchesOwnerAccepted())
                failures.Add($"rejected value changed client value; got {inst.Describe()}");

            inst.SignalRejectedVerified();
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastVerifyRejected");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ValidatedSyncVarIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (ValidatedSyncVarIdentity.LocalInstance != null)
            ValidatedSyncVarIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(designatedOwner ? "owner" : "observer")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static PlayerID? PickOwner(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.isServer) continue;
            if (hostLocal.HasValue && hostLocal.Value == p) continue;
            if (!best.HasValue || p.id.value < best.Value.id.value)
                best = p;
        }

        return best;
    }
}
