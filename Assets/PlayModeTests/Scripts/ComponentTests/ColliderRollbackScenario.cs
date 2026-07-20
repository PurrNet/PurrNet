using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Deterministic ground-truth coverage for the 3D rollback module. A kinematic box is driven
// along a scripted tick-indexed path (with a disabled-collider window) while the scenario keeps
// its own tick -> pose ledger; a second thin box swaps rotation mid-run. After the mover parks
// far away, history is interrogated: state replay + interpolation must match the ledger, rays
// aimed at historical positions must hit there (with the hit point on the historical surface)
// and must miss at ticks where the box was elsewhere or disabled, rays at the present position
// must miss for old ticks, untracked colliders keep present-time behavior, destroyed colliders
// prune cleanly, and on host the client factory must resolve the server's module instance.
public class ColliderRollbackScenario : Scenario
{
    [SerializeField] private float _phaseTimeoutSeconds = 30f;

    private const int MoveTicks = 40;
    private const int DisabledFrom = 15;
    private const int DisabledTo = 20;
    private const int RotSwapIndex = 20;
    private const float StepX = 0.5f;
    private const float PosEpsilon = 1e-3f;
    private const float HitEpsilon = 0.02f;

    private static readonly Vector3 LaneStart = new(0f, 2f, 500f);
    private static readonly Vector3 RotBoxPos = new(0f, 2f, 520f);
    private static readonly Vector3 UntrackedPos = new(0f, 2f, 540f);
    private static readonly Vector3 ParkPos = new(1000f, 2f, 500f);

    private TickManager _tick;
    private Transform _mover;
    private BoxCollider _moverCollider;
    private Transform _rotBox;

    private readonly Dictionary<uint, Vector3> _ledger = new();
    private readonly Dictionary<uint, bool> _enabledLedger = new();
    private readonly Dictionary<uint, bool> _rotatedLedger = new();
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
        _enabledLedger.Clear();
        _rotatedLedger.Clear();
        _failures.Clear();
        _driving = false;

        GameObject moverGo = null, rotGo = null, untrackedGo = null;

        try
        {
            moverGo = new GameObject("RollbackMover");
            moverGo.transform.position = LaneStart;
            _mover = moverGo.transform;
            _moverCollider = moverGo.AddComponent<BoxCollider>();
            moverGo.AddComponent<ColliderRollback>();

            rotGo = new GameObject("RollbackRotBox");
            rotGo.transform.position = RotBoxPos;
            _rotBox = rotGo.transform;
            var rotCollider = rotGo.AddComponent<BoxCollider>();
            rotCollider.size = new Vector3(2f, 0.2f, 2f);
            rotGo.AddComponent<ColliderRollback>();

            untrackedGo = new GameObject("RollbackUntracked");
            untrackedGo.transform.position = UntrackedPos;
            var untrackedCollider = untrackedGo.AddComponent<BoxCollider>();

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

            if (ctx.role == NetworkRole.Host)
                CheckHostSharedModule(nm, module);

            await CheckDestroyedPrune(module, ctx);

            if (_failures.Count > 0)
                return ScenarioResult.Fail(Summarize());

            return ScenarioResult.Ok($"rollback history matched ledger over {_ledger.Count} ticks");
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
        var pos = LaneStart + Vector3.right * (StepX * index);
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

    private static Ray RayAt(Vector3 target)
    {
        return new Ray(new Vector3(target.x, target.y, target.z - 5f), Vector3.forward);
    }

    private void CheckCasts(RollbackModule module)
    {
        bool sphereCastChecked = false;

        foreach (var (t, pos) in _ledger)
        {
            var ray = RayAt(pos);
            bool hitSomething = module.Raycast(t, ray, out var hit, 10f);
            bool hitMover = hitSomething && hit.collider == _moverCollider;

            if (_enabledLedger[t])
            {
                if (!hitMover)
                    _failures.Add($"raycast missed mover at tick {t} ({pos:F2})");
                else if (Mathf.Abs(hit.point.z - (pos.z - 0.5f)) > HitEpsilon)
                    _failures.Add($"hit.point at {t}: {hit.point:F4}, expected z {pos.z - 0.5f:F4}");

                if (!sphereCastChecked)
                {
                    sphereCastChecked = true;
                    if (!module.SphereCast(t, ray, 0.3f, out var sphereHit, 10f) ||
                        sphereHit.collider != _moverCollider)
                        _failures.Add($"spherecast missed mover at tick {t}");
                }
            }
            else if (hitMover)
            {
                _failures.Add($"raycast hit disabled mover at tick {t}");
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
        if (module.Raycast(tLast, rayFirst, out var hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"ray at tick {tFirst} pos hit mover when queried at tick {tLast}");

        var rayLast = RayAt(_ledger[tLast]);
        if (module.Raycast(tFirst, rayLast, out hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"ray at tick {tLast} pos hit mover when queried at tick {tFirst}");
    }

    private void CheckPresentVsPast(RollbackModule module)
    {
        if (!TryFindRecentTick(module, out var now))
        {
            _failures.Add("no recent snapshot of the parked mover found");
            return;
        }

        var parkRay = RayAt(ParkPos);
        if (!module.Raycast(now, parkRay, out var hit, 10f) || hit.collider != _moverCollider)
            _failures.Add("present-tick raycast missed the parked mover");

        if (!TryGetEnabledExtremes(out var tFirst, out _))
            return;

        if (module.Raycast(tFirst, parkRay, out hit, 10f) && hit.collider == _moverCollider)
            _failures.Add($"past-tick {tFirst} raycast hit the mover at its present (parked) position");
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

        var probe = RotBoxPos + new Vector3(0f, 0.8f, 0f);
        if (module.CheckSphere(tUpright, probe, 0.05f))
            _failures.Add($"probe overlapped the upright thin box at tick {tUpright}");
        if (!module.CheckSphere(tRotated, probe, 0.05f))
            _failures.Add($"probe missed the rotated thin box at tick {tRotated}");
    }

    private void CheckUntracked(RollbackModule module, Collider untrackedCollider)
    {
        if (!TryGetEnabledExtremes(out var tFirst, out _))
            return;

        var ray = RayAt(UntrackedPos);
        if (!module.Raycast(tFirst, ray, out var hit, 10f) || hit.collider != untrackedCollider)
            _failures.Add("untracked collider was not hit at its present position for a past tick");
    }

    private void CheckHostSharedModule(NetworkManager nm, RollbackModule serverModule)
    {
        bool shared = nm.TryGetModule<ScenesModule>(false, out var clientScenes) &&
                      clientScenes.TryGetSceneID(gameObject.scene, out var clientSceneId) &&
                      nm.TryGetModule<ColliderRollbackFactory>(false, out var clientFactory) &&
                      clientFactory.TryGetModule(clientSceneId, out var clientModule) &&
                      ReferenceEquals(clientModule, serverModule);

        if (!shared)
            _failures.Add("host client factory did not resolve the server rollback module");
    }

    private async UniTask CheckDestroyedPrune(RollbackModule module, ScenarioContext ctx)
    {
        if (!TryGetEnabledExtremes(out _, out var tLast))
            return;

        var lastPos = _ledger[tLast];
        Destroy(_moverCollider);
        await WaitUntilTick(_tick.localTick + 2, ctx);

        if (module.TryGetColliderState(tLast, _moverCollider, out _))
            _failures.Add("collider state survived component destruction");
        if (module.Raycast(tLast, RayAt(lastPos), out _, 10f))
            _failures.Add("raycast still hit something after the tracked collider was destroyed");
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
