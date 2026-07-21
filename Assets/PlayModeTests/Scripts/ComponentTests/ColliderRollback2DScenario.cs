using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// 2D counterpart of ColliderRollbackScenario: a kinematic BoxCollider2D walks a scripted
// tick-indexed path (with a disabled-collider window) while the scenario ledgers tick -> pose;
// a second thin box swaps rotation mid-run. History is checked via state replay, interpolation,
// per-tick raycasts (hit.point back-transformed to the historical surface), a circle cast,
// rotation-sensitive CheckCircle probes, cross-tick and present-position misses, untracked
// present-time behavior, and destroyed-collider pruning.
public class ColliderRollback2DScenario : Scenario
{
    [SerializeField] private float _phaseTimeoutSeconds = 30f;

    private const int MoveTicks = 40;
    private const int DisabledFrom = 15;
    private const int DisabledTo = 20;
    private const int RotSwapIndex = 20;
    private const float StepX = 0.5f;
    private const float PosEpsilon = 1e-3f;
    private const float HitEpsilon = 0.02f;

    private static readonly Vector2 LaneStart = new(0f, 500f);
    private static readonly Vector2 RotBoxPos = new(0f, 520f);
    private static readonly Vector2 UntrackedPos = new(0f, 540f);
    private static readonly Vector2 ParkPos = new(1000f, 500f);

    private TickManager _tick;
    private Transform _mover;
    private BoxCollider2D _moverCollider;
    private Transform _rotBox;

    private readonly Dictionary<uint, Vector2> _ledger = new();
    private readonly Dictionary<uint, bool> _enabledLedger = new();
    private readonly Dictionary<uint, bool> _rotatedLedger = new();
    private readonly List<string> _failures = new();

    private uint _startTick;
    private uint _preCreateTick;
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
        _enabledLedger.Clear();
        _rotatedLedger.Clear();
        _failures.Clear();
        _driving = false;

        GameObject moverGo = null, rotGo = null, untrackedGo = null;

        try
        {
            _preCreateTick = _tick.localTick;

            moverGo = new GameObject("RollbackMover2D");
            moverGo.transform.position = LaneStart;
            _mover = moverGo.transform;
            _moverCollider = moverGo.AddComponent<BoxCollider2D>();
            moverGo.AddComponent<ColliderRollback>();

            rotGo = new GameObject("RollbackRotBox2D");
            rotGo.transform.position = RotBoxPos;
            _rotBox = rotGo.transform;
            var rotCollider = rotGo.AddComponent<BoxCollider2D>();
            rotCollider.size = new Vector2(2f, 0.2f);
            rotGo.AddComponent<ColliderRollback>();

            untrackedGo = new GameObject("RollbackUntracked2D");
            untrackedGo.transform.position = UntrackedPos;
            var untrackedCollider = untrackedGo.AddComponent<BoxCollider2D>();

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
            CheckRotation(module);
            CheckUntracked(module, untrackedCollider);
            CheckGapFallback(module);
            await CheckDestroyedPrune(module, ctx);

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
            if (rotGo)
                Destroy(rotGo);
            if (untrackedGo)
                Destroy(untrackedGo);
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
            _moverCollider.enabled = true;
            _driving = false;
            return;
        }

        bool colliderEnabled = index < DisabledFrom || index >= DisabledTo;
        var pos = LaneStart + Vector2.right * (StepX * index);
        _mover.position = pos;
        _moverCollider.enabled = colliderEnabled;
        _ledger[t] = pos;
        _enabledLedger[t] = colliderEnabled;

        bool rotated = index >= RotSwapIndex;
        _rotBox.rotation = rotated ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
        _rotatedLedger[t] = rotated;
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
            if (state.enabled != _enabledLedger[t])
                _failures.Add($"state enabled at {t}: {state.enabled} vs ledger {_enabledLedger[t]}");
        }
    }

    private void CheckInterpolation(RollbackModule module)
    {
        foreach (var (t, pos) in _ledger)
        {
            if (!_ledger.TryGetValue(t + 1, out var next) || !_enabledLedger[t] || !_enabledLedger[t + 1])
                continue;

            var mid = (pos + next) * 0.5f;
            if (!module.TryGetColliderState(t + 0.5, _moverCollider, out var state))
                _failures.Add($"no interpolated state at {t}+0.5");
            else if ((state.position - mid).magnitude > PosEpsilon)
                _failures.Add($"interp pos at {t}+0.5: {state.position:F4} vs {mid:F4}");
            return;
        }

        _failures.Add("no consecutive enabled tick pair for interpolation check");
    }

    private static Ray2D RayAt(Vector2 target)
    {
        return new Ray2D(new Vector2(target.x, target.y - 5f), Vector2.up);
    }

    private void CheckCasts(RollbackModule module)
    {
        bool circleCastChecked = false;

        foreach (var (t, pos) in _ledger)
        {
            var ray = RayAt(pos);
            var expectedPoint = new Vector2(pos.x, pos.y - 0.5f);
            bool hitSomething = module.Raycast(t, ray, out RaycastHit2D hit, 10f);
            bool hitMover = hitSomething && hit.collider == _moverCollider;

            if (_enabledLedger[t])
            {
                if (!hitMover)
                    _failures.Add($"2D raycast missed mover at tick {t} ({pos:F2})");
                else if ((hit.point - expectedPoint).magnitude > HitEpsilon)
                    _failures.Add($"2D hit.point at {t}: {hit.point:F4}, expected {expectedPoint:F4}");

                if (!circleCastChecked)
                {
                    circleCastChecked = true;
                    if (!module.CircleCast(t, ray, 0.3f, out RaycastHit2D circleHit, 10f) ||
                        circleHit.collider != _moverCollider)
                        _failures.Add($"2D circle cast missed mover at tick {t}");
                }
            }
            else if (hitMover)
            {
                _failures.Add($"2D raycast hit disabled mover at tick {t}");
            }
        }
    }

    private void CheckNegativeCrossTick(RollbackModule module)
    {
        if (!TryGetEnabledExtremes(out var tFirst, out var tLast))
        {
            _failures.Add("no enabled ticks for cross-tick check");
            return;
        }

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

        if (!TryGetEnabledExtremes(out var tFirst, out _))
            return;

        if (module.Raycast(tFirst, parkRay, out hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"past-tick {tFirst} 2D raycast hit the mover at its present (parked) position");
    }

    private void CheckRotation(RollbackModule module)
    {
        uint tUpright = 0, tRotated = 0;
        bool hasUpright = false, hasRotated = false;

        foreach (var (t, rotated) in _rotatedLedger)
        {
            if (rotated && !hasRotated)
            {
                tRotated = t;
                hasRotated = true;
            }
            else if (!rotated && !hasUpright)
            {
                tUpright = t;
                hasUpright = true;
            }
        }

        if (!hasUpright || !hasRotated)
        {
            _failures.Add("rotation ledger missing an upright or rotated tick");
            return;
        }

        var probe = RotBoxPos + new Vector2(0f, 0.8f);
        if (module.CheckCircle(tUpright, probe, 0.05f))
            _failures.Add($"probe overlapped the upright thin 2D box at tick {tUpright}");
        if (!module.CheckCircle(tRotated, probe, 0.05f))
            _failures.Add($"probe missed the rotated thin 2D box at tick {tRotated}");
    }

    private void CheckUntracked(RollbackModule module, Collider2D untrackedCollider)
    {
        if (!TryGetEnabledExtremes(out var tFirst, out _))
            return;

        var ray = RayAt(UntrackedPos);
        if (!module.Raycast(tFirst, ray, out RaycastHit2D hit, 10f) || hit.collider != untrackedCollider)
            _failures.Add("untracked 2D collider was not hit at its present position for a past tick");
    }

    private void CheckGapFallback(RollbackModule module)
    {
        if (!module.TryGetColliderState((double)_preCreateTick - 4, _moverCollider, out var state))
            _failures.Add("no 2D fallback state just before the oldest snapshot");
        else if ((state.position - LaneStart).magnitude > PosEpsilon)
            _failures.Add($"2D fallback state pos {state.position:F4}, expected oldest {LaneStart:F4}");

        if (module.TryGetColliderState((double)_preCreateTick - 30, _moverCollider, out _))
            _failures.Add("2D fallback returned state far beyond the sample gap limit");
    }

    private async UniTask CheckDestroyedPrune(RollbackModule module, ScenarioContext ctx)
    {
        if (!TryGetEnabledExtremes(out _, out var tLast))
            return;

        var lastPos = _ledger[tLast];
        Destroy(_moverCollider);
        await WaitUntilTick(_tick.localTick + 2, ctx);

        if (module.TryGetColliderState(tLast, _moverCollider, out _))
            _failures.Add("2D collider state survived component destruction");
        if (module.Raycast(tLast, RayAt(lastPos), out RaycastHit2D _, 10f))
            _failures.Add("2D raycast still hit something after the tracked collider was destroyed");
    }

    private bool TryGetEnabledExtremes(out uint first, out uint last)
    {
        first = uint.MaxValue;
        last = 0;

        foreach (var (t, colliderEnabled) in _enabledLedger)
        {
            if (!colliderEnabled)
                continue;
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
