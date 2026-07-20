using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Owns its own signal statics so a full-suite run (where Bootstrap calls every scenario's
// Setup upfront) can't leak signals between the bone scenarios sharing NetworkBonesRoot.
public class NetworkBonesOwnerRoot : NetworkBonesRoot
{
    public static NetworkBonesOwnerRoot localInstance;
    public static int serverReadyCount;
    public static int convergedCount;
    public static int serverDoneCount;
    public static ulong ownerId;
    public static bool ownerIdReceived;
    public static bool phaseDoneReceived;

    public static void ResetSignals()
    {
        localInstance = null;
        serverReadyCount = 0;
        convergedCount = 0;
        serverDoneCount = 0;
        ownerId = 0;
        ownerIdReceived = false;
        phaseDoneReceived = false;
        ResetAll();
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        localInstance = this;
    }

    protected override void OnDespawned()
    {
        base.OnDespawned();
        if (localInstance == this)
            localInstance = null;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => serverReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalConverged(RPCInfo info = default) => convergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => serverDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastOwner(ulong owner)
    {
        ownerId = owner;
        ownerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => phaseDoneReceived = true;
}

// Covers the owner-auth direction of NetworkBones that NetworkBonesScenario does not: a
// client-owned rig driven continuously by the owning client must converge on the server
// (client -> server ServerRpc bone stream) and on every other observer (server relay).
// Stresses the reported real-world regime: ~100 bones (multi-RPC, near-MTU packets),
// 30Hz send rate, and a server throttled to 10 FPS while receiving (slow editor host).
public class NetworkBonesOwnerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _syncTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _driveSeconds = 3f;
    [SerializeField] private int _serverFpsWhileReceiving = 10;
    [SerializeField] private int _sendRatePerSecond = 30;

    private const int BarrierBase = 5750;
    private const int SkinnedBoneCount = 3;
    private const int ExtraBoneCount = 97;
    private const int TotalBoneCount = SkinnedBoneCount + ExtraBoneCount;
    private const float PositionTolerance = 0.05f;
    private const float AngleTolerance = 2f;
    private const float ScaleTolerance = 0.1f;

    private NetworkBonesOwnerRoot _prefab;

    private static Vector3 TargetPosition(int i) => new(-0.5f * (i + 1), 0.3f * (i % 7), 0.2f * (i % 5));
    private static Quaternion TargetRotation(int i) => Quaternion.Euler(15f * (i % 9), 0f, 10f * (i % 11));
    private static Vector3 TargetScale(int i) => Vector3.one * (1f + 0.05f * (i % 13));

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NetworkBonesOwnerAuthScenario));
        rootGo.SetActive(false);

        _prefab = rootGo.AddComponent<NetworkBonesOwnerRoot>();

        var bones = new Transform[SkinnedBoneCount];
        var parent = rootGo.transform;
        for (int i = 0; i < SkinnedBoneCount; i++)
        {
            var bone = new GameObject($"bone{i}").transform;
            bone.SetParent(parent, false);
            bone.localPosition = new Vector3(0f, 0.2f * (i + 1), 0f);
            parent = bone;
            bones[i] = bone;
        }

        var meshGo = new GameObject("SkinnedMesh");
        meshGo.transform.SetParent(rootGo.transform, false);
        var smr = meshGo.AddComponent<SkinnedMeshRenderer>();

        var mesh = new Mesh();
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        var weights = new BoneWeight[3];
        for (int i = 0; i < 3; i++)
            weights[i] = new BoneWeight { boneIndex0 = i, weight0 = 1f };
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
        smr.sharedMesh = mesh;
        smr.bones = bones;
        smr.rootBone = bones[0];

        var extras = new Transform[ExtraBoneCount];
        for (int i = 0; i < ExtraBoneCount; i++)
        {
            var extra = new GameObject($"extra{i}").transform;
            extra.SetParent(rootGo.transform, false);
            extra.localPosition = new Vector3(-1f - 0.1f * i, 0f, 0f);
            extras[i] = extra;
        }

        var networkBones = rootGo.AddComponent<NetworkBones>();
        networkBones.extraBones = extras;

        typeof(NetworkBones)
            .GetField("_sendRatePerSecond", BindingFlags.Instance | BindingFlags.NonPublic)
            !.SetValue(networkBones, _sendRatePerSecond);

        var all = new Transform[TotalBoneCount];
        for (int i = 0; i < SkinnedBoneCount; i++)
            all[i] = bones[i];
        for (int i = 0; i < ExtraBoneCount; i++)
            all[SkinnedBoneCount + i] = extras[i];

        _prefab.networkBones = networkBones;
        _prefab.skinnedBones = bones;
        _prefab.extraBone = extras[0];
        _prefab.allBones = all;

        NetworkBonesOwnerRoot.ResetSignals();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    private static bool BoneMatches(Transform bone, int i)
    {
        return Vector3.Distance(bone.localPosition, TargetPosition(i)) <= PositionTolerance
               && Quaternion.Angle(bone.localRotation, TargetRotation(i)) <= AngleTolerance
               && Vector3.Distance(bone.localScale, TargetScale(i)) <= ScaleTolerance;
    }

    private static bool AllBonesMatch()
    {
        var local = NetworkBonesOwnerRoot.localInstance;
        if (local == null)
            return false;

        for (int i = 0; i < TotalBoneCount; i++)
        {
            if (!BoneMatches(local.allBones[i], i))
                return false;
        }

        return true;
    }

    private static string DescribeBones()
    {
        var local = NetworkBonesOwnerRoot.localInstance;
        if (local == null)
            return "no local instance";

        int mismatched = 0;
        int firstBad = -1;
        for (int i = 0; i < TotalBoneCount; i++)
        {
            if (BoneMatches(local.allBones[i], i))
                continue;
            mismatched++;
            if (firstBad < 0)
                firstBad = i;
        }

        if (mismatched == 0)
            return "all bones match";

        var bone = local.allBones[firstBad];
        return $"{mismatched}/{TotalBoneCount} bones mismatched; first bone{firstBad}: " +
               $"pos={bone.localPosition} (want {TargetPosition(firstBad)}), " +
               $"rotDelta={Quaternion.Angle(bone.localRotation, TargetRotation(firstBad)):F1}deg, " +
               $"scale={bone.localScale} (want {TargetScale(firstBad)})";
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkBonesOwnerRoot instance = null;

        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { instance = Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.localInstance != null
                      && NetworkBonesOwnerRoot.localInstance.networkBones.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={NetworkBonesOwnerRoot.localInstance != null}");
        }

        int boneCount = NetworkBonesOwnerRoot.localInstance.networkBones.boneCount;
        if (boneCount != TotalBoneCount)
            return ScenarioResult.Fail(
                $"bone gathering mismatch: gathered {boneCount}, expected {TotalBoneCount}");

        if (ctx.isClient)
            NetworkBonesOwnerRoot.localInstance.SignalReady();

        var result = await RunSplit(ctx, RunAsClient, RunAsServer);
        if (!result.success)
            return result;

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.localInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("client-owned bone stream converged on server and observers");
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var inst = NetworkBonesOwnerRoot.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.serverReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {NetworkBonesOwnerRoot.serverReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the rig");

        inst.networkBones.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        int prevTargetFrameRate = Application.targetFrameRate;
        if (_serverFpsWhileReceiving > 0)
            Application.targetFrameRate = _serverFpsWhileReceiving;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AllBonesMatch(),
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server never converged to owner-driven pose: {DescribeBones()}");
        }
        finally
        {
            Application.targetFrameRate = prevTargetFrameRate;
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.convergedCount >= ctx.expectedConnections,
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"observer convergence timeout: {NetworkBonesOwnerRoot.convergedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"done timeout: {NetworkBonesOwnerRoot.serverDoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"owner={owner.Value.id.value}");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var inst = NetworkBonesOwnerRoot.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.ownerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designated = ctx.networkManager.isLocalPlayerReady
                          && ctx.networkManager.localPlayer.id.value == NetworkBonesOwnerRoot.ownerId;

        if (designated)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.networkBones.isOwner,
                    _syncTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail("designated owner never became isOwner");
            }

            await DriveBones(ctx, inst);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AllBonesMatch(),
                _syncTimeoutSeconds,
                ctx.cancellationToken);
            NetworkBonesOwnerRoot.localInstance.SignalConverged();
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"never converged to owner pose: {DescribeBones()}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesOwnerRoot.phaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastPhaseDone");
        }

        if (NetworkBonesOwnerRoot.localInstance != null)
            NetworkBonesOwnerRoot.localInstance.SignalDone();

        return ScenarioResult.Ok(designated ? "owner" : "observer");
    }

    // Continuously animates every bone toward its target, like an IK solver would, then
    // settles on the exact target so the stream goes idle at the final pose.
    private async UniTask DriveBones(ScenarioContext ctx, NetworkBonesOwnerRoot inst)
    {
        var startPos = new Vector3[TotalBoneCount];
        var startRot = new Quaternion[TotalBoneCount];
        var startScale = new Vector3[TotalBoneCount];

        for (int i = 0; i < TotalBoneCount; i++)
        {
            var bone = inst.allBones[i];
            startPos[i] = bone.localPosition;
            startRot[i] = bone.localRotation;
            startScale[i] = bone.localScale;
        }

        float elapsed = 0f;
        while (elapsed < _driveSeconds)
        {
            if (inst == null || !inst.networkBones.isSpawned)
                return;

            float t = Mathf.Clamp01(elapsed / _driveSeconds);
            for (int i = 0; i < TotalBoneCount; i++)
            {
                var bone = inst.allBones[i];
                bone.localPosition = Vector3.Lerp(startPos[i], TargetPosition(i), t);
                bone.localRotation = Quaternion.Slerp(startRot[i], TargetRotation(i), t);
                bone.localScale = Vector3.Lerp(startScale[i], TargetScale(i), t);
            }

            await UniTask.Yield(PlayerLoopTiming.Update, ctx.cancellationToken);
            elapsed += Time.unscaledDeltaTime;
        }

        for (int i = 0; i < TotalBoneCount; i++)
        {
            var bone = inst.allBones[i];
            bone.localPosition = TargetPosition(i);
            bone.localRotation = TargetRotation(i);
            bone.localScale = TargetScale(i);
        }
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
