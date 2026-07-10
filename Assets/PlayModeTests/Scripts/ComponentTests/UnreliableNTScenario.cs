using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Proves the unreliable NetworkTransform sync end to end: a server-controlled NT must converge
// on every peer via the acked-baseline delta stream, keep converging after a reparent under
// another NetworkIdentity (frame-tagged samples decode in the parent's frame instead of relying
// on hierarchy-message timing), and despawn cleanly.
public class UnreliableNTScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _convergeTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierSpawn = 7700;
    private const int BarrierPhaseOne = 7701;
    private const int BarrierPhaseTwo = 7702;
    private const int BarrierEnd = 7703;

    private const float PositionEpsilon = 0.05f;
    private const float RotationEpsilonDegrees = 2f;
    private const float ScaleEpsilon = 0.1f;

    private static readonly Vector3 AnchorPos = new(8f, 0f, 3f);
    private static readonly Vector3 AnchorScale = new(2f, 1f, 2f);
    private static readonly Vector3 MoverStart = new(0f, 1f, 0f);
    private static readonly Vector3 PhaseOnePos = new(4f, 1f, -2f);
    private static readonly Quaternion PhaseOneRot = Quaternion.Euler(0f, 90f, 0f);
    private static readonly Vector3 PhaseTwoPos = AnchorPos + new Vector3(1.5f, 0.5f, 0f);

    // Trips if world scale visibly deviates during the reparent transition (frame/data coherency bug).
    private bool _sawScaleGlitch;
    private Vector3 _worstScale;

    private UnreliableNTMover _moverPrefab;
    private UnreliableNTAnchor _anchorPrefab;

    void CreatePrefabs()
    {
        var moverGo = new GameObject(nameof(UnreliableNTMover));
        moverGo.SetActive(false);
        _moverPrefab = moverGo.AddComponent<UnreliableNTMover>();
        moverGo.AddComponent<NetworkTransform>();

        var anchorGo = new GameObject(nameof(UnreliableNTAnchor));
        anchorGo.SetActive(false);
        anchorGo.transform.localScale = AnchorScale;
        _anchorPrefab = anchorGo.AddComponent<UnreliableNTAnchor>();
        anchorGo.AddComponent<NetworkTransform>();

        UnreliableNTMover.ResetAll();
        UnreliableNTAnchor.ResetAll();
        _sawScaleGlitch = false;
        _worstScale = Vector3.one;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefabs();
        manager.prefabProvider.AddRuntimePrefab(_moverPrefab.name, _moverPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_anchorPrefab.name, _anchorPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        UnreliableNTMover mover = null;
        UnreliableNTAnchor anchor = null;

        if (ctx.isServer)
        {
            anchor = Instantiate(_anchorPrefab, AnchorPos, Quaternion.identity);
            mover = Instantiate(_moverPrefab, MoverStart, Quaternion.identity);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => UnreliableNTMover.localInstance && UnreliableNTAnchor.localInstance,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: mover={(bool)UnreliableNTMover.localInstance}, " +
                $"anchor={(bool)UnreliableNTAnchor.localInstance}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierSpawn, _barrierTimeoutSeconds);

        if (ctx.isServer)
            await MoveOverFrames(mover.transform, PhaseOnePos, PhaseOneRot, ctx);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsConverged(PhaseOnePos, PhaseOneRot),
                _convergeTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"phase 1 never converged: pos={DescribeMover()}, expected={PhaseOnePos}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierPhaseOne, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            mover.transform.SetParent(anchor.transform, true);
            await MoveOverFrames(mover.transform, PhaseTwoPos, PhaseOneRot, ctx);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () =>
                {
                    TrackScaleGlitch();
                    return IsConverged(PhaseTwoPos, PhaseOneRot) && IsParentedToAnchor() && HasWorldScaleOne();
                },
                _convergeTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"phase 2 never converged: pos={DescribeMover()}, expected={PhaseTwoPos}, " +
                $"parented={IsParentedToAnchor()}, worldScale={WorldScale():F3}");
        }

        if (_sawScaleGlitch)
            return ScenarioResult.Fail(
                $"mover world scale glitched during the reparent transition: worst={_worstScale:F3}");

        await ScenarioBarrier.Wait(ctx, BarrierPhaseTwo, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            Destroy(mover.gameObject);
            Destroy(anchor.gameObject);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => UnreliableNTMover.aliveCount == 0 && UnreliableNTAnchor.aliveCount == 0,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"despawn incomplete: movers={UnreliableNTMover.aliveCount}, " +
                $"anchors={UnreliableNTAnchor.aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("unreliable NT converged in world frame, identity frame, and despawned");
    }

    private static async UniTask MoveOverFrames(Transform trs, Vector3 targetPos, Quaternion targetRot,
        ScenarioContext ctx)
    {
        var startPos = trs.position;
        var startRot = trs.rotation;
        const int steps = 90;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            trs.position = Vector3.Lerp(startPos, targetPos, t);
            trs.rotation = Quaternion.Slerp(startRot, targetRot, t);
            await UniTask.Yield(ctx.cancellationToken);
        }
    }

    private static bool IsConverged(Vector3 expectedPos, Quaternion expectedRot)
    {
        var mover = UnreliableNTMover.localInstance;
        if (!mover)
            return false;

        return (mover.transform.position - expectedPos).magnitude <= PositionEpsilon &&
               Quaternion.Angle(mover.transform.rotation, expectedRot) <= RotationEpsilonDegrees;
    }

    private static bool IsParentedToAnchor()
    {
        var mover = UnreliableNTMover.localInstance;
        var anchor = UnreliableNTAnchor.localInstance;
        return mover && anchor && mover.transform.parent == anchor.transform;
    }

    private static Vector3 WorldScale()
    {
        var mover = UnreliableNTMover.localInstance;
        return mover ? mover.transform.lossyScale : Vector3.zero;
    }

    private static bool HasWorldScaleOne()
    {
        var scale = WorldScale();
        return Mathf.Abs(scale.x - 1f) <= ScaleEpsilon &&
               Mathf.Abs(scale.y - 1f) <= ScaleEpsilon &&
               Mathf.Abs(scale.z - 1f) <= ScaleEpsilon;
    }

    private void TrackScaleGlitch()
    {
        var scale = WorldScale();
        if (scale == Vector3.zero)
            return;

        const float glitchThreshold = 0.4f;
        if (Mathf.Abs(scale.x - 1f) > glitchThreshold ||
            Mathf.Abs(scale.y - 1f) > glitchThreshold ||
            Mathf.Abs(scale.z - 1f) > glitchThreshold)
        {
            _sawScaleGlitch = true;
            _worstScale = scale;
        }
    }

    private static string DescribeMover()
    {
        var mover = UnreliableNTMover.localInstance;
        return mover ? mover.transform.position.ToString("F3") : "<no instance>";
    }
}
