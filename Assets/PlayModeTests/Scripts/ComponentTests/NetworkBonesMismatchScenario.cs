using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Owns its own signal statics so a full-suite run (where Bootstrap calls every scenario's
// Setup upfront) can't leak signals between the bone scenarios sharing NetworkBonesRoot.
public class NetworkBonesMismatchRoot : NetworkBonesRoot
{
    public static NetworkBonesMismatchRoot localInstance;
    public static int serverReadyCount;
    public static int serverDoneCount;
    public static ulong ownerId;
    public static bool ownerIdReceived;
    public static bool phaseDoneReceived;

    public static void ResetSignals()
    {
        localInstance = null;
        serverReadyCount = 0;
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

// Peers deliberately gather different bone sets (clients have one extra bone the server
// lacks). When the owning client streams its larger rig, the server must reject the
// out-of-range bone chunks with a single clear diagnostic instead of throwing or
// silently applying, and every process must stay healthy through despawn.
public class NetworkBonesMismatchScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _errorTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _driveSeconds = 1.5f;

    private const int BarrierBase = 5760;
    private const int SkinnedBoneCount = 3;
    private const string MismatchMarker = "bone set mismatch";

    private NetworkBonesMismatchRoot _prefab;
    private int _localBoneCount;
    private bool _mismatchErrorSeen;
    private int _mismatchErrorCount;

    void CreatePrefab(int extraBoneCount)
    {
        var rootGo = new GameObject(nameof(NetworkBonesMismatchScenario));
        rootGo.SetActive(false);

        _prefab = rootGo.AddComponent<NetworkBonesMismatchRoot>();

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

        var extras = new Transform[extraBoneCount];
        for (int i = 0; i < extraBoneCount; i++)
        {
            var extra = new GameObject($"extra{i}").transform;
            extra.SetParent(rootGo.transform, false);
            extra.localPosition = new Vector3(-1f - 0.1f * i, 0f, 0f);
            extras[i] = extra;
        }

        var networkBones = rootGo.AddComponent<NetworkBones>();
        networkBones.extraBones = extras;

        var all = new Transform[SkinnedBoneCount + extraBoneCount];
        for (int i = 0; i < SkinnedBoneCount; i++)
            all[i] = bones[i];
        for (int i = 0; i < extraBoneCount; i++)
            all[SkinnedBoneCount + i] = extras[i];

        _prefab.networkBones = networkBones;
        _prefab.skinnedBones = bones;
        _prefab.extraBone = extras[0];
        _prefab.allBones = all;

        _localBoneCount = SkinnedBoneCount + extraBoneCount;

        NetworkBonesMismatchRoot.ResetSignals();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        // pure clients gather one extra bone the server never has
        CreatePrefab(ctx.role == NetworkRole.Client ? 2 : 1);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error && condition.Contains(MismatchMarker))
        {
            _mismatchErrorSeen = true;
            _mismatchErrorCount++;
        }
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
    }

    private static bool BonesAtSpawnPose(NetworkBonesMismatchRoot inst)
    {
        for (int i = 0; i < SkinnedBoneCount; i++)
        {
            if (Vector3.Distance(inst.skinnedBones[i].localPosition, new Vector3(0f, 0.2f * (i + 1), 0f)) > 0.001f)
                return false;
            if (Quaternion.Angle(inst.skinnedBones[i].localRotation, Quaternion.identity) > 0.1f)
                return false;
        }

        return true;
    }

    private static Vector3 DrivenPosition(int i) => new(0.7f * (i + 1), 0.4f * (i + 1), 0f);

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _mismatchErrorSeen = false;
        _mismatchErrorCount = 0;
        Application.logMessageReceived += OnLogMessage;

        try
        {
            return await Run(ctx);
        }
        finally
        {
            Application.logMessageReceived -= OnLogMessage;
        }
    }

    private async UniTask<ScenarioResult> Run(ScenarioContext ctx)
    {
        NetworkBonesMismatchRoot instance = null;

        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { instance = Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesMismatchRoot.localInstance != null
                      && NetworkBonesMismatchRoot.localInstance.networkBones.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={NetworkBonesMismatchRoot.localInstance != null}");
        }

        int boneCount = NetworkBonesMismatchRoot.localInstance.networkBones.boneCount;
        if (boneCount != _localBoneCount)
            return ScenarioResult.Fail(
                $"bone gathering mismatch: gathered {boneCount}, expected {_localBoneCount}");

        if (ctx.isClient)
            NetworkBonesMismatchRoot.localInstance.SignalReady();

        var result = await RunSplit(ctx, RunAsClient, RunAsServer);
        if (!result.success)
            return result;

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesMismatchRoot.localInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("bone set mismatch was diagnosed and never applied");
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var inst = NetworkBonesMismatchRoot.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesMismatchRoot.serverReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {NetworkBonesMismatchRoot.serverReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the rig");

        inst.networkBones.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _mismatchErrorSeen,
                _errorTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("server never diagnosed the mismatch from the owner stream");
        }

        if (_mismatchErrorCount > 1)
            return ScenarioResult.Fail($"mismatch error logged {_mismatchErrorCount} times, expected once");

        // non-controller UpdateVisuals rewrites from the (empty) interpolation buffers,
        // so a correct rejection settles the bones back on the spawn pose
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => BonesAtSpawnPose(inst),
                _errorTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("server applied bone data despite the mismatch");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesMismatchRoot.serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"done timeout: {NetworkBonesMismatchRoot.serverDoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"owner={owner.Value.id.value}, errors={_mismatchErrorCount}");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var inst = NetworkBonesMismatchRoot.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesMismatchRoot.ownerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designated = ctx.role == NetworkRole.Client
                          && ctx.networkManager.isLocalPlayerReady
                          && ctx.networkManager.localPlayer.id.value == NetworkBonesMismatchRoot.ownerId;

        if (designated)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.networkBones.isOwner,
                    _errorTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail("designated owner never became isOwner");
            }

            float elapsed = 0f;
            while (elapsed < _driveSeconds)
            {
                if (inst == null || !inst.networkBones.isSpawned)
                    break;

                for (int i = 0; i < SkinnedBoneCount; i++)
                    inst.skinnedBones[i].localPosition = DrivenPosition(i) + Vector3.up * Mathf.PingPong(elapsed, 0.5f);

                await UniTask.Yield(PlayerLoopTiming.Update, ctx.cancellationToken);
                elapsed += Time.unscaledDeltaTime;
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesMismatchRoot.phaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastPhaseDone");
        }

        if (NetworkBonesMismatchRoot.localInstance != null)
            NetworkBonesMismatchRoot.localInstance.SignalDone();

        return ScenarioResult.Ok(designated ? "owner" : "observer");
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
