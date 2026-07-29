using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Ensures playback actions are preserved as commands even when a clip is shorter than one
/// network tick and several actions are issued in the same frame.
/// </summary>
public class NetworkAudioSourcePlaybackScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _commandTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5840;
    private const uint ExpectedCommandCount = 3;

    private NetworkAudioSourcePlaybackRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NetworkAudioSourcePlaybackRoot));
        rootGo.SetActive(false);
        _prefab = rootGo.AddComponent<NetworkAudioSourcePlaybackRoot>();

        var audioGo = new GameObject("NetworkAudioSource");
        audioGo.transform.SetParent(rootGo.transform);

        var source = audioGo.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = 0f;
        source.clip = AudioClip.Create("ShortNetworkAudioClip", 80, 1, 8000, false);

        var networkAudio = audioGo.AddComponent<NetworkAudioSource>();
        var sourceField = typeof(NetworkAudioSource).GetField(
            "_audioSource",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (sourceField == null)
            throw new MissingFieldException(typeof(NetworkAudioSource).FullName, "_audioSource");

        sourceField.SetValue(networkAudio, source);
        _prefab.networkAudio = networkAudio;

        // Dynamic/one-shot-only sources commonly spawn without a clip. Keep one in the
        // networked hierarchy to ensure state capture never reads AudioSource.time on it.
        var emptyAudioGo = new GameObject("EmptyNetworkAudioSource");
        emptyAudioGo.transform.SetParent(rootGo.transform);

        var emptySource = emptyAudioGo.AddComponent<AudioSource>();
        emptySource.playOnAwake = false;
        emptySource.volume = 0f;

        var emptyNetworkAudio = emptyAudioGo.AddComponent<NetworkAudioSource>();
        sourceField.SetValue(emptyNetworkAudio, emptySource);

        NetworkAudioSourcePlaybackRoot.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkAudioSourcePlaybackRoot instance = null;

        if (ctx.isServer)
            instance = Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAudioSourcePlaybackRoot.LocalInstance != null
                      && NetworkAudioSourcePlaybackRoot.LocalInstance.networkAudio
                      && NetworkAudioSourcePlaybackRoot.LocalInstance.networkAudio.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("network audio source did not spawn");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            var networkAudio = NetworkAudioSourcePlaybackRoot.LocalInstance.networkAudio;

            // The clip is 10 ms long while the default network tick is 50 ms. These three
            // actions happen in one frame and must remain three distinct reliable commands.
            networkAudio.Play();
            networkAudio.Stop();
            networkAudio.Play();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () =>
                {
                    var networkAudio = NetworkAudioSourcePlaybackRoot.LocalInstance?.networkAudio;
                    if (!networkAudio)
                        return false;

                    return ctx.role == NetworkRole.Client
                        ? networkAudio.playbackCommandsApplied >= ExpectedCommandCount
                        : networkAudio.playbackCommandsSent >= ExpectedCommandCount;
                },
                _commandTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var networkAudio = NetworkAudioSourcePlaybackRoot.LocalInstance?.networkAudio;
            return ScenarioResult.Fail(
                $"playback commands did not converge: sent={networkAudio?.playbackCommandsSent ?? 0}, " +
                $"applied={networkAudio?.playbackCommandsApplied ?? 0}");
        }

        var localAudio = NetworkAudioSourcePlaybackRoot.LocalInstance.networkAudio;
        uint actualCount = ctx.role == NetworkRole.Client
            ? localAudio.playbackCommandsApplied
            : localAudio.playbackCommandsSent;

        if (actualCount != ExpectedCommandCount)
        {
            return ScenarioResult.Fail(
                $"playback command count was {actualCount}, expected {ExpectedCommandCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAudioSourcePlaybackRoot.LocalInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("network audio source did not despawn");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("short same-frame playback commands arrived exactly once");
    }
}
