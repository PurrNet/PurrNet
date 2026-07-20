using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// 2D counterpart of ColliderRollbackScenario: a kinematic BoxCollider2D walks a scripted
// tick-indexed path while the scenario ledgers tick -> position, then history is checked via
// state replay, interpolation, and the single-hit Raycast(Ray2D) overload — rays must hit at
// historical positions, miss at ticks where the box was elsewhere, and miss the present
// (parked) position for old ticks.
public class ColliderRollback2DScenario : Scenario
{
    [SerializeField] private float _phaseTimeoutSeconds = 30f;

    private const int MoveTicks = 40;
    private const float StepX = 0.5f;
    private const float PosEpsilon = 1e-3f;
    private const float HitEpsilon = 0.02f;

    private static readonly Vector2 LaneStart = new(0f, 500f);
    private static readonly Vector2 ParkPos = new(1000f, 500f);

    private TickManager _tick;
    private Transform _mover;
    private BoxCollider2D _moverCollider;

    private readonly Dictionary<uint, Vector2> _ledger = new();
    private readonly List<string> _failures = new();

    private uint _startTick;
    private bool _driving;

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (!ctx.isServer)
            return ScenarioResult.Ok("client idle");

        var nm = ctx.networkManager;

        if (!nm.TryGetModule<TickManager>(true, out _tick))
            return ScenarioResult.Fail("no server TickManager");
        if (!nm.TryGetModule<ScenesModule>(true, out var scenes))
            return ScenarioResult.Fail("no server ScenesModule");
        if (!scenes.TryGetSceneID(gameObject.scene, out var sceneId))
            return ScenarioResult.Fail("scene has no SceneID");
        if (!nm.TryGetModule<ColliderRollbackFactory>(true, out var factory) ||
            !factory.TryGetModule(sceneId, out var module))
            return ScenarioResult.Fail("no server RollbackModule");

        _ledger.Clear();
        _failures.Clear();
        _driving = false;

        GameObject moverGo = null;

        try
        {
            moverGo = new GameObject("RollbackMover2D");
            moverGo.transform.position = LaneStart;
            _mover = moverGo.transform;
            _moverCollider = moverGo.AddComponent<BoxCollider2D>();
            moverGo.AddComponent<ColliderRollback>();

            await WaitUntilTick(_tick.localTick + 3, ctx);

            _startTick = _tick.localTick + 1;
            _driving = true;
            _tick.onPreTick += OnPreTick;

            await WaitUntilTick(_startTick + MoveTicks + 3, ctx);

            if (_ledger.Count < MoveTicks / 2)
                return ScenarioResult.Fail($"only {_ledger.Count}/{MoveTicks} ticks recorded");

            CheckStateReplay(module);
            CheckInterpolation(module);
            CheckCasts(module);
            CheckNegativeCrossTick(module);
            CheckPresentVsPast(module);

            if (_failures.Count > 0)
                return ScenarioResult.Fail(Summarize());

            return ScenarioResult.Ok($"2D rollback history matched ledger over {_ledger.Count} ticks");
        }
        finally
        {
            if (_tick != null)
                _tick.onPreTick -= OnPreTick;
            if (moverGo)
                Destroy(moverGo);
        }
    }

    private void OnPreTick()
    {
        if (!_driving)
            return;

        uint t = _tick.localTick;
        if (t < _startTick)
            return;

        uint index = t - _startTick;
        if (index >= MoveTicks)
        {
            _mover.position = ParkPos;
            _driving = false;
            return;
        }

        var pos = LaneStart + Vector2.right * (StepX * index);
        _mover.position = pos;
        _ledger[t] = pos;
    }

    private void CheckStateReplay(RollbackModule module)
    {
        foreach (var (t, pos) in _ledger)
        {
            if (!module.TryGetColliderState(t, _moverCollider, out var state))
            {
                _failures.Add($"no state at tick {t}");
                continue;
            }

            if ((state.position - pos).magnitude > PosEpsilon)
                _failures.Add($"state pos at {t}: {state.position:F4} vs ledger {pos:F4}");
            if (!state.enabled)
                _failures.Add($"state disabled at {t}");
        }
    }

    private void CheckInterpolation(RollbackModule module)
    {
        foreach (var (t, pos) in _ledger)
        {
            if (!_ledger.TryGetValue(t + 1, out var next))
                continue;

            var mid = (pos + next) * 0.5f;
            if (!module.TryGetColliderState(t + 0.5, _moverCollider, out var state))
                _failures.Add($"no interpolated state at {t}+0.5");
            else if ((state.position - mid).magnitude > PosEpsilon)
                _failures.Add($"interp pos at {t}+0.5: {state.position:F4} vs {mid:F4}");
            return;
        }

        _failures.Add("no consecutive tick pair for interpolation check");
    }

    private static Ray2D RayAt(Vector2 target)
    {
        return new Ray2D(new Vector2(target.x, target.y - 5f), Vector2.up);
    }

    private void CheckCasts(RollbackModule module)
    {
        foreach (var (t, pos) in _ledger)
        {
            var ray = RayAt(pos);
            bool hitSomething = module.Raycast(t, ray, out RaycastHit2D hit, 10f);

            if (!hitSomething || hit.collider != _moverCollider)
                _failures.Add($"2D raycast missed mover at tick {t} ({pos:F2})");
            else if (Mathf.Abs(hit.point.y - (pos.y - 0.5f)) > HitEpsilon)
                _failures.Add($"2D hit.point at {t}: {hit.point:F4}, expected y {pos.y - 0.5f:F4}");
        }
    }

    private void CheckNegativeCrossTick(RollbackModule module)
    {
        if (!TryGetExtremes(out var tFirst, out var tLast))
            return;

        var rayFirst = RayAt(_ledger[tFirst]);
        if (module.Raycast(tLast, rayFirst, out RaycastHit2D hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"2D ray at tick {tFirst} pos hit mover when queried at tick {tLast}");

        var rayLast = RayAt(_ledger[tLast]);
        if (module.Raycast(tFirst, rayLast, out hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"2D ray at tick {tLast} pos hit mover when queried at tick {tFirst}");
    }

    private void CheckPresentVsPast(RollbackModule module)
    {
        if (!TryFindRecentTick(module, out var now))
        {
            _failures.Add("no recent snapshot of the parked 2D mover found");
            return;
        }

        var parkRay = RayAt(ParkPos);
        if (!module.Raycast(now, parkRay, out RaycastHit2D hit, 10f) || hit.collider != _moverCollider)
            _failures.Add("present-tick 2D raycast missed the parked mover");

        if (!TryGetExtremes(out var tFirst, out _))
            return;

        if (module.Raycast(tFirst, parkRay, out hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"past-tick {tFirst} 2D raycast hit the mover at its present (parked) position");
    }

    private bool TryGetExtremes(out uint first, out uint last)
    {
        first = uint.MaxValue;
        last = 0;

        foreach (var t in _ledger.Keys)
        {
            if (t < first) first = t;
            if (t > last) last = t;
        }

        return first != uint.MaxValue && last > first;
    }

    private bool TryFindRecentTick(RollbackModule module, out uint tick)
    {
        uint now = _tick.localTick;
        for (uint back = 0; back < 5 && back <= now; back++)
        {
            tick = now - back;
            if (module.TryGetColliderState(tick, _moverCollider, out var state) &&
                (state.position - ParkPos).magnitude <= PosEpsilon)
                return true;
        }

        tick = 0;
        return false;
    }

    private string Summarize()
    {
        int shown = Mathf.Min(_failures.Count, 8);
        var msg = string.Join(" | ", _failures.GetRange(0, shown));
        return _failures.Count > shown ? $"{msg} | (+{_failures.Count - shown} more)" : msg;
    }

    private async UniTask WaitUntilTick(uint target, ScenarioContext ctx)
    {
        await UniTaskUtils.WaitWithTimeout(() => _tick.localTick >= target, _phaseTimeoutSeconds,
            ctx.cancellationToken);
    }
}
