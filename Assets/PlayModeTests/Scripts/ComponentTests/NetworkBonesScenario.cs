using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Proves NetworkBones replication end to end in real player builds: bones gathered from a
// SkinnedMeshRenderer plus extraBones must be counted identically on every peer, and the
// server-driven local position/rotation/scale of each bone must converge on every peer
// through the delta-compressed unreliable bone stream.
public class NetworkBonesScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _syncTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5700;
    private const int SkinnedBoneCount = 3;
    private const float PositionTolerance = 0.05f;
    private const float AngleTolerance = 2f;
    private const float ScaleTolerance = 0.1f;

    private NetworkBonesRoot _prefab;

    private static Vector3 TargetPosition(int i) => new(0.5f * (i + 1), 0.25f * (i + 1), 0.1f * (i + 1));
    private static Quaternion TargetRotation(int i) => Quaternion.Euler(0f, 20f * (i + 1), 0f);
    private static Vector3 TargetScale(int i) => Vector3.one * (1f + 0.1f * (i + 1));

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NetworkBonesRoot));
        rootGo.SetActive(false);

        _prefab = rootGo.AddComponent<NetworkBonesRoot>();

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

        var extra = new GameObject("extraBone").transform;
        extra.SetParent(rootGo.transform, false);
        extra.localPosition = new Vector3(-1f, 0f, 0f);

        var networkBones = rootGo.AddComponent<NetworkBones>();
        networkBones.extraBones = new[] { extra };

        _prefab.networkBones = networkBones;
        _prefab.skinnedBones = bones;
        _prefab.extraBone = extra;

        NetworkBonesRoot.ResetAll();
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
        var local = NetworkBonesRoot.LocalInstance;
        if (local == null)
            return false;

        for (int i = 0; i < SkinnedBoneCount; i++)
        {
            if (!BoneMatches(local.skinnedBones[i], i))
                return false;
        }

        return BoneMatches(local.extraBone, SkinnedBoneCount);
    }

    private static string DescribeBones()
    {
        var local = NetworkBonesRoot.LocalInstance;
        if (local == null)
            return "no local instance";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i <= SkinnedBoneCount; i++)
        {
            var bone = i < SkinnedBoneCount ? local.skinnedBones[i] : local.extraBone;
            sb.Append($"bone{i}: pos={bone.localPosition} (want {TargetPosition(i)}), " +
                      $"rotDelta={Quaternion.Angle(bone.localRotation, TargetRotation(i)):F1}deg, " +
                      $"scale={bone.localScale} (want {TargetScale(i)}); ");
        }

        return sb.ToString();
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkBonesRoot instance = null;

        if (ctx.isServer)
            instance = Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesRoot.LocalInstance != null
                      && NetworkBonesRoot.LocalInstance.networkBones.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={NetworkBonesRoot.LocalInstance != null}");
        }

        int boneCount = NetworkBonesRoot.LocalInstance.networkBones.boneCount;
        if (boneCount != SkinnedBoneCount + 1)
            return ScenarioResult.Fail(
                $"bone gathering mismatch: gathered {boneCount}, expected {SkinnedBoneCount + 1}");

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
        {
            for (int i = 0; i < SkinnedBoneCount; i++)
            {
                var bone = instance.skinnedBones[i];
                bone.localPosition = TargetPosition(i);
                bone.localRotation = TargetRotation(i);
                bone.localScale = TargetScale(i);
            }

            instance.extraBone.localPosition = TargetPosition(SkinnedBoneCount);
            instance.extraBone.localRotation = TargetRotation(SkinnedBoneCount);
            instance.extraBone.localScale = TargetScale(SkinnedBoneCount);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AllBonesMatch(),
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"bones didn't sync: {DescribeBones()}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkBonesRoot.LocalInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("bone count and pos/rot/scale synced for skinned + extra bones");
    }
}
