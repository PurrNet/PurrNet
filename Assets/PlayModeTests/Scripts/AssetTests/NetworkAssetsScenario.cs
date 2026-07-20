using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class NetworkAssetsScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _receiveTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    public const int SoValue = 4242;

    private NetworkAssetCarrier _prefab;

    private NetworkAssetTestSO _so;
    private AudioClip _clip;
    private Texture2D _tex;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(NetworkAssetsScenario));
        _prefab = go.AddComponent<NetworkAssetCarrier>();
        go.SetActive(false);
        NetworkAssetCarrier.ResetAll();
    }

    void CreateAssets(NetworkManager manager)
    {
        var na = ScriptableObject.CreateInstance<NetworkAssets>();
        var linked = ScriptableObject.CreateInstance<NetworkAssets>();

        _so = ScriptableObject.CreateInstance<NetworkAssetTestSO>();
        _so.name = "NetworkAssetTestSO";
        _so.value = SoValue;

        _clip = AudioClip.Create("NetworkAssetTestClip", 512, 1, 8000, false);

        _tex = new Texture2D(4, 4) { name = "NetworkAssetTestTex" };

        na.AddAsset(_so);
        na.AddAsset(_clip);

        linked.AddAsset(_tex);
        na.linkedNetworkAssets.Add(linked);
        na.Refresh();

        manager.networkAssets = na;

        NetworkAssetCarrier.SerializeAsset = _so;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        CreateAssets(manager);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            var inst = Instantiate(_prefab);
            inst.SendAssets(_so, _clip, _tex, null);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAssetCarrier.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("carrier never spawned");
        }

        var failures = new List<string>();

        if (ctx.role == NetworkRole.Client)
            await VerifyClient(ctx, failures);

        if (ctx.isServer)
            return await RunAsServer(ctx, failures);

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask VerifyClient(ScenarioContext ctx, List<string> failures)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAssetCarrier.ReceivedCount >= 1,
                _receiveTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("SendAssets never arrived on the client");
            return;
        }

        var inst = NetworkAssetCarrier.LocalInstance;

        if (NetworkAssetCarrier.ReceivedCount != 1)
            failures.Add($"SendAssets arrived {NetworkAssetCarrier.ReceivedCount} time(s), expected 1");

        if (!inst.recvSo)
            failures.Add("ScriptableObject ref resolved to null");
        else
        {
            if (inst.recvSo != _so)
                failures.Add("ScriptableObject did not resolve to this peer's registered instance");
            if (inst.recvSo.value != SoValue)
                failures.Add($"ScriptableObject value mismatch: got {inst.recvSo.value}, expected {SoValue}");
        }

        if (!inst.recvClip)
            failures.Add("AudioClip ref resolved to null");
        else if (inst.recvClip != _clip)
            failures.Add("AudioClip did not resolve to this peer's registered instance");

        if (!inst.recvTex)
            failures.Add("Texture2D (linked NetworkAssets) ref resolved to null");
        else if (inst.recvTex != _tex)
            failures.Add("Texture2D from linked NetworkAssets did not resolve to this peer's registered instance");

        if (inst.recvMaybeNull || !inst.nullArrivedNull)
            failures.Add("null asset ref did not round-trip as null");

        if (NetworkAssetCarrier.DeserializeCount != 1)
            failures.Add($"OnDeserialize ran {NetworkAssetCarrier.DeserializeCount} time(s), expected 1");

        if (!inst.recvSerializedSo)
            failures.Add("OnSerialize-carried asset resolved to null");
        else
        {
            if (inst.recvSerializedSo != _so)
                failures.Add("OnSerialize-carried asset did not resolve to this peer's registered instance");
            if (inst.recvSerializedSo.value != SoValue)
                failures.Add($"OnSerialize-carried asset value mismatch: got {inst.recvSerializedSo.value}, expected {SoValue}");
        }

        if (failures.Count == 0)
            inst.SignalReceivedOk();
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx, List<string> failures)
    {
        int expected = ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAssetCarrier.ServerOkCount >= expected,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-ok timeout: got {NetworkAssetCarrier.ServerOkCount}/{expected}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"ok={NetworkAssetCarrier.ServerOkCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}
