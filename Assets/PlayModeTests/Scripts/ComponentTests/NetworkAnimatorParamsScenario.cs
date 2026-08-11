using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Proves NetworkAnimator parameter replication end to end: a server-controlled animator's
// float/bool/int/trigger writes (manual API, including the damped SetFloat overload) must land on
// every peer with the LAST value winning. Also covers authoritative writes while the local Animator
// is disabled: they must not warn, must survive auto-sync, and must still reach observers.
public class NetworkAnimatorParamsScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _syncTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5500;

    private static readonly int FloatA = Animator.StringToHash("FloatA");
    private static readonly int FloatB = Animator.StringToHash("FloatB");
    private static readonly int BoolA = Animator.StringToHash("BoolA");
    private static readonly int IntA = Animator.StringToHash("IntA");
    private static readonly int TrigA = Animator.StringToHash("TrigA");
    private static readonly int TrigB = Animator.StringToHash("TrigB");

    private NetworkAnimatorRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NetworkAnimatorRoot));
        rootGo.SetActive(false);

        _prefab = rootGo.AddComponent<NetworkAnimatorRoot>();

        var animator = rootGo.AddComponent<Animator>();
        animator.runtimeAnimatorController =
            Resources.Load<RuntimeAnimatorController>("NetAnimTestController");

        var networkAnimator = rootGo.AddComponent<NetworkAnimator>();
        networkAnimator.SetAnimator(animator);

        _prefab.animator = animator;
        _prefab.networkAnimator = networkAnimator;

        NetworkAnimatorRoot.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkAnimatorRoot instance = null;

        if (ctx.isServer)
            instance = Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAnimatorRoot.LocalInstance != null
                      && NetworkAnimatorRoot.LocalInstance.animator.runtimeAnimatorController != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={NetworkAnimatorRoot.LocalInstance != null}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
        {
            var na = instance.networkAnimator;
            // same-tick interleaved writes: the LAST FloatA value must win on every peer
            na.SetFloat(FloatA, 1f);
            na.SetBool(BoolA, true);
            na.SetFloat(FloatA, 2.5f);
            na.SetInteger(IntA, 7);
            na.SetTrigger(TrigA);
            na.SetTrigger(TrigB);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () =>
                {
                    var local = NetworkAnimatorRoot.LocalInstance;
                    if (local == null)
                        return false;

                    var anim = local.animator;
                    return Mathf.Approximately(anim.GetFloat(FloatA), 2.5f)
                           && anim.GetBool(BoolA)
                           && anim.GetInteger(IntA) == 7
                           && anim.GetBool(TrigA)
                           && anim.GetBool(TrigB);
                },
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var anim = NetworkAnimatorRoot.LocalInstance ? NetworkAnimatorRoot.LocalInstance.animator : null;
            return ScenarioResult.Fail(anim == null
                ? "params never synced: no local instance"
                : $"params didn't sync: FloatA={anim.GetFloat(FloatA)} (want 2.5), " +
                  $"BoolA={anim.GetBool(BoolA)} (want true), IntA={anim.GetInteger(IntA)} (want 7), " +
                  $"TrigA={anim.GetBool(TrigA)} (want true), TrigB={anim.GetBool(TrigB)} (want true)");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
        {
            var na = instance.networkAnimator;
            // consecutive resets of DIFFERENT triggers must both replicate
            na.ResetTrigger(TrigA);
            na.ResetTrigger(TrigB);
            // damped write: Unity damping lerps by dt/(dampTime+dt), so one call with
            // dampTime << deltaTime lands near (not exactly at) the target: 10 * 5/5.05 = 9.901
            na.SetFloat(FloatB, 10f, 0.05f, 5f);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () =>
                {
                    var local = NetworkAnimatorRoot.LocalInstance;
                    if (local == null)
                        return false;

                    var anim = local.animator;
                    return !anim.GetBool(TrigA)
                           && !anim.GetBool(TrigB)
                           && Mathf.Abs(anim.GetFloat(FloatB) - 10f) < 0.5f;
                },
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var anim = NetworkAnimatorRoot.LocalInstance ? NetworkAnimatorRoot.LocalInstance.animator : null;
            return ScenarioResult.Fail(anim == null
                ? "trigger resets / damped float never synced: no local instance"
                : $"resets/damped float didn't sync: TrigA={anim.GetBool(TrigA)} (want false), " +
                  $"TrigB={anim.GetBool(TrigB)} (want false), FloatB={anim.GetFloat(FloatB)} (want 10±0.5)");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
        {
            var anim = instance.animator;
            var na = instance.networkAnimator;
            int disabledAnimatorWarnings = 0;

            void OnLogMessage(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("Animator is not playing an AnimatorController"))
                    disabledAnimatorWarnings++;
            }

            anim.enabled = false;
            Application.logMessageReceived += OnLogMessage;
            try
            {
                na.SetFloat(FloatA, 4.25f);
                na.SetBool(BoolA, false);
                na.SetInteger(IntA, 42);
                na.SetTrigger(TrigA);
            }
            finally
            {
                Application.logMessageReceived -= OnLogMessage;
            }

            if (disabledAnimatorWarnings != 0)
                return ScenarioResult.Fail(
                    $"disabled Animator parameter writes logged {disabledAnimatorWarnings} warning(s)");

            if (!Mathf.Approximately(na.GetFloat(FloatA), 4.25f) ||
                na.GetBool(BoolA) || na.GetInteger(IntA) != 42)
                return ScenarioResult.Fail("disabled Animator shadow parameter state was not retained");
        }

        if (!ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () =>
                    {
                        var local = NetworkAnimatorRoot.LocalInstance;
                        if (local == null)
                            return false;

                        var anim = local.animator;
                        return Mathf.Approximately(anim.GetFloat(FloatA), 4.25f)
                               && !anim.GetBool(BoolA)
                               && anim.GetInteger(IntA) == 42
                               && anim.GetBool(TrigA);
                    },
                    _syncTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                var anim = NetworkAnimatorRoot.LocalInstance ? NetworkAnimatorRoot.LocalInstance.animator : null;
                return ScenarioResult.Fail(anim == null
                    ? "disabled Animator writes never synced: no local instance"
                    : $"disabled writes didn't sync: FloatA={anim.GetFloat(FloatA)} (want 4.25), " +
                      $"BoolA={anim.GetBool(BoolA)} (want false), IntA={anim.GetInteger(IntA)} (want 42), " +
                      $"TrigA={anim.GetBool(TrigA)} (want true)");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 4, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
        {
            instance.animator.enabled = true;
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => Mathf.Approximately(instance.animator.GetFloat(FloatA), 4.25f)
                          && !instance.animator.GetBool(BoolA)
                          && instance.animator.GetInteger(IntA) == 42,
                    _syncTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail("shadow parameter state was not restored after enabling the Animator");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 5, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAnimatorRoot.LocalInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 6, _barrierTimeoutSeconds);

        return ScenarioResult.Ok(
            "params, disabled Animator writes, last-write-wins, trigger resets, and damped float all synced");
    }
}
