using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

#if PURRNET_HAS_INSTANTIATE_ASYNC

/// <summary>
/// End-to-end coverage for UnityEngine.Object.InstantiateAsync interception.
///
/// The scenario intentionally registers the normal and client-authoritative prefabs as pooled.
/// Every async result must nevertheless be marked non-pooled and destroyed after despawn.
/// </summary>
public sealed class AsyncInstantiateScenario : Scenario
{
    [Tooltip("Rules with client spawn authority (the PlayModeTests AnyoneCanSpawn asset).")]
    [SerializeField] private NetworkRules _clientSpawnRules;

    [Header("Load")]
    [SerializeField] private int _stressCycles = 3;
    [SerializeField] private int _stressInstancesPerCycle = 24;
    [SerializeField] private int _rapidDespawnInstances = 32;
    [SerializeField] private int _clientInstances = 4;
    [SerializeField] private int _nonNetworkInstances = 12;
    [SerializeField] private int _cancellationInstances = 256;

    [Header("Awake shape mismatch")]
    [SerializeField] private AsyncInstantiateAwakeMutation _awakeMutation =
        AsyncInstantiateAwakeMutation.AddNetworkIdentity;

    [Header("Timeouts")]
    [SerializeField] private float _operationTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 30f;
    [SerializeField] private float _stateAndRpcTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 90f;

    private const int BarrierBase = 7900;
    private const int ServerTokenBase = 100000;
    private const int ClientTokenBase = 200000;

    private AsyncInstantiateProbe _serverPrefab;
    private AsyncInstantiateProbe _clientPrefab;
    private AsyncInstantiateCancellationIdentity _cancellationPrefab;
    private AsyncInstantiateAwakeShapeIdentity _shapePrefab;
    private GameObject _nonNetworkTemplate;

    private bool _shapeDiagnosticSeen;
    private bool _runClientAuthoritative;
    private bool _prepared;

    private static bool _cancellationOutcomeReceived;
    private static bool _cancellationSucceeded;
    private static string _cancellationDetail;

    private static bool _rapidDespawnOutcomeReceived;
    private static bool _rapidDespawnSucceeded;
    private static string _rapidDespawnDetail;

    private static bool _shapeOutcomeReceived;
    private static bool _shapeSucceeded;
    private static string _shapeDetail;

    private static bool _spawnerReceived;
    private static bool _hasSpawner;
    private static ulong _spawnerId;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        if (CommandLineUtils.TryGetArgument("-scenario", out var scenario) &&
            scenario == nameof(AsyncInstantiateScenario))
            Prepare(manager, true);
    }

    private void Prepare(NetworkManager manager, bool allowRuleOverride)
    {
        if (_prepared)
            return;
        _prepared = true;

        CreateRuntimeTemplates();

        AsyncInstantiateProbe.ResetAll();
        AsyncInstantiateCancellationIdentity.ResetAll();
        AsyncInstantiateAwakeShapeIdentity.ResetAll();
        AsyncInstantiateAwakeShapeMutator.ResetAll();

        if (_clientSpawnRules)
        {
            _clientPrefab.SetNetworkRules(_clientSpawnRules);

            // Client spawn validation uses manager-level rules on the server. Only replace the
            // manager rules when this is the sole requested scenario; every full-suite Setup runs
            // before connecting, so changing them there would affect unrelated scenarios.
            if (allowRuleOverride)
                manager.SetNetworkRules(_clientSpawnRules);
        }
        else
        {
            Debug.LogError(
                "[AsyncInstantiateScenario] _clientSpawnRules is missing; the client-authoritative phase will fail.");
        }

        _runClientAuthoritative = manager.networkRules &&
                                  manager.networkRules.HasSpawnAuthority(manager, false);

        // These two are deliberately configured as pooled. InstantiateAsync must override the
        // provider setting on every peer and destroy the resulting instances on despawn.
        manager.prefabProvider.AddRuntimePrefab(_serverPrefab.name, _serverPrefab.gameObject, true, 2);
        manager.prefabProvider.AddRuntimePrefab(_clientPrefab.name, _clientPrefab.gameObject, true, 2);
        manager.prefabProvider.AddRuntimePrefab(_cancellationPrefab.name, _cancellationPrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(_shapePrefab.name, _shapePrefab.gameObject, false);

        _cancellationOutcomeReceived = false;
        _rapidDespawnOutcomeReceived = false;
        _shapeOutcomeReceived = false;
        _spawnerReceived = false;
    }

    private void CreateRuntimeTemplates()
    {
        _serverPrefab = CreateProbePrefab("AsyncInstantiateServerPooledPrefab");
        _clientPrefab = CreateProbePrefab("AsyncInstantiateClientPooledPrefab");

        var cancellationGo = new GameObject("AsyncInstantiateCancellationPrefab");
        _cancellationPrefab = cancellationGo.AddComponent<AsyncInstantiateCancellationIdentity>();
        // Give the worker thread meaningful clone work so allowSceneActivation can reliably
        // hold the operation before Cancel is exercised.
        for (int i = 0; i < 12; i++)
        {
            var child = new GameObject($"Payload_{i}");
            child.transform.SetParent(cancellationGo.transform, false);
        }
        cancellationGo.SetActive(false);

        var shapeGo = new GameObject("AsyncInstantiateAwakeShapePrefab");
        _shapePrefab = shapeGo.AddComponent<AsyncInstantiateAwakeShapeIdentity>();
        shapeGo.AddComponent<AsyncInstantiateAwakeShapeMutator>();
        var expectedChild = new GameObject("ExpectedNetworkChild");
        expectedChild.transform.SetParent(shapeGo.transform, false);
        expectedChild.AddComponent<NetworkIdentity>();
        shapeGo.SetActive(false);

        _nonNetworkTemplate = new GameObject("AsyncInstantiateNonNetworkTemplate");
        _nonNetworkTemplate.SetActive(false);
    }

    private static AsyncInstantiateProbe CreateProbePrefab(string name)
    {
        var go = new GameObject(name);
        var result = go.AddComponent<AsyncInstantiateProbe>();

        // Exercise framework ordering, nested identity IDs, and parent resolution on every
        // successful async spawn instead of only testing the trivial one-component shape.
        var networkChild = new GameObject("NestedNetworkIdentity");
        networkChild.transform.SetParent(go.transform, false);
        networkChild.AddComponent<NetworkIdentity>();
        for (var i = 0; i < 12; i++)
        {
            var payload = new GameObject($"AsyncPayload_{i}");
            payload.transform.SetParent(networkChild.transform, false);
        }

        go.SetActive(false);
        return result;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

        Prepare(ctx.networkManager, false);
        await SafeBarrier(ctx, BarrierBase - 2, "runtime prefab setup", failures);

        await RunPhase(ctx, BarrierBase - 1, "proxy overload surface", TestProxyOverloadSurface, failures);
        await RunPhase(ctx, BarrierBase + 0, "non-network passthrough", TestNonNetworkPassthrough, failures);
        await RunPhase(ctx, BarrierBase + 1, "pending observer storage", TestPendingObserverStorage, failures);
        await RunServerStress(ctx, failures);
        await RunPhase(ctx, BarrierBase + 30, "despawn during remote async work", TestRapidDespawn, failures);
        await RunPhase(ctx, BarrierBase + 40, "cancellation", TestCancellation, failures);
        await RunClientAuthoritative(ctx, failures);
        await RunPhase(ctx, BarrierBase + 70, "Awake shape mismatch", TestAwakeShapeMismatch, failures);

        return failures.Count == 0
            ? ScenarioResult.Ok(
                $"server={_stressCycles}x{_stressInstancesPerCycle}, " +
                $"client={(_runClientAuthoritative ? _clientInstances.ToString() : "skipped")}, " +
                $"rapidDespawn={_rapidDespawnInstances}, cancel={_cancellationInstances}, shape={_awakeMutation}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private UniTask<string> TestProxyOverloadSurface(ScenarioContext ctx)
    {
        Func<GameObject, AsyncInstantiateOperation<GameObject>> methodGroup =
            UnityEngine.Object.InstantiateAsync;
        if (methodGroup.Method.DeclaringType != typeof(UnityProxy))
            return UniTask.FromResult<string>("InstantiateAsync method group was not retargeted to UnityProxy");

        var nativeMethods = typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var proxyMethods = typeof(UnityProxy).GetMethods(BindingFlags.Public | BindingFlags.Static);

        for (var i = 0; i < nativeMethods.Length; i++)
        {
            var native = nativeMethods[i];
            if (native.Name != "InstantiateAsync")
                continue;

            bool matched = false;
            for (var j = 0; j < proxyMethods.Length; j++)
            {
                var proxy = proxyMethods[j];
                if (proxy.Name != native.Name ||
                    proxy.GetGenericArguments().Length != native.GetGenericArguments().Length)
                    continue;

                var nativeParameters = native.GetParameters();
                var proxyParameters = proxy.GetParameters();
                if (nativeParameters.Length != proxyParameters.Length)
                    continue;

                matched = true;
                for (var parameterIndex = 0; parameterIndex < nativeParameters.Length; parameterIndex++)
                {
                    if (ReflectionTypesMatch(nativeParameters[parameterIndex].ParameterType,
                            proxyParameters[parameterIndex].ParameterType))
                        continue;
                    matched = false;
                    break;
                }

                if (matched)
                    break;
            }

            if (!matched)
                return UniTask.FromResult<string>($"Unity overload has no proxy match: {native}");
        }

        return UniTask.FromResult<string>(null);
    }

    private static bool ReflectionTypesMatch(Type left, Type right)
    {
        if (left.IsGenericParameter || right.IsGenericParameter)
            return left.IsGenericParameter && right.IsGenericParameter &&
                   left.GenericParameterPosition == right.GenericParameterPosition;
        if (left.IsArray || right.IsArray)
            return left.IsArray && right.IsArray && left.GetArrayRank() == right.GetArrayRank() &&
                   ReflectionTypesMatch(left.GetElementType(), right.GetElementType());
        if (!left.IsGenericType || !right.IsGenericType)
            return left == right;
        if (left.GetGenericTypeDefinition() != right.GetGenericTypeDefinition())
            return false;

        var leftArguments = left.GetGenericArguments();
        var rightArguments = right.GetGenericArguments();
        if (leftArguments.Length != rightArguments.Length)
            return false;
        for (var i = 0; i < leftArguments.Length; i++)
        {
            if (!ReflectionTypesMatch(leftArguments[i], rightArguments[i]))
                return false;
        }
        return true;
    }

    private async UniTask RunPhase(
        ScenarioContext ctx,
        int barrier,
        string name,
        Func<ScenarioContext, UniTask<string>> phase,
        List<string> failures)
    {
        try
        {
            var failure = await phase(ctx);
            if (!string.IsNullOrEmpty(failure))
                failures.Add($"{name}: {failure}");
        }
        catch (Exception e)
        {
            failures.Add($"{name}: {e.GetType().Name}: {e.Message}");
            Debug.LogException(e);
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, barrier, _barrierTimeoutSeconds);
        }
        catch (Exception e)
        {
            failures.Add($"{name} barrier: {e.Message}");
        }
    }

    private async UniTask<string> TestNonNetworkPassthrough(ScenarioContext ctx)
    {
        var parent = new GameObject("AsyncInstantiateNonNetworkParent");
        var operation = UnityEngine.Object.InstantiateAsync(
            _nonNetworkTemplate,
            _nonNetworkInstances,
            parent.transform);

        if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
        {
            UnityProxy.DestroyDirectly(parent);
            return "native operation timed out";
        }

        var results = operation.Result;
        var failures = new List<string>();

        if (results == null || results.Length != _nonNetworkInstances)
            failures.Add($"result count was {results?.Length ?? -1}/{_nonNetworkInstances}");

        if (results != null)
        {
            for (int i = 0; i < results.Length; i++)
            {
                var result = results[i];
                if (!result)
                {
                    failures.Add($"result {i} was null");
                    continue;
                }

                if (result.transform.parent != parent.transform)
                    failures.Add($"result {i} lost its requested parent");
                if (result.GetComponentInChildren<NetworkIdentity>(true))
                    failures.Add($"result {i} unexpectedly gained a NetworkIdentity");

                UnityProxy.DestroyDirectly(result);
            }
        }

        UnityProxy.DestroyDirectly(parent);
        await UniTask.NextFrame(ctx.cancellationToken);

        return failures.Count == 0 ? null : string.Join(", ", failures);
    }

    private static UniTask<string> TestPendingObserverStorage(ScenarioContext ctx)
    {
        var testObject = new GameObject("AsyncInstantiatePendingObserverStorageTest");
        var identity = testObject.AddComponent<NetworkIdentity>();
        var player = new PlayerID(987654, false);

        try
        {
            if (identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("storage was allocated before first async observer");

            if (!identity.TryAddObserver(player) || identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("ordinary observer add allocated pending storage");

            if (!identity.TryMoveObserverToPending(player) ||
                !identity.pendingObserverStorageAllocated ||
                !identity.hasPendingObservers ||
                !identity.IsObserverOrPending(player))
                return UniTask.FromResult<string>("moving an observer to pending did not allocate valid storage");

            if (!identity.TryPromotePendingObserver(player) ||
                identity.pendingObserverStorageAllocated ||
                !identity.IsObserver(player))
                return UniTask.FromResult<string>("promotion did not release pending storage");

            if (!identity.TryMoveObserverToPending(player) ||
                !identity.TryRemovePendingObserver(player) ||
                identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("explicit pending removal did not release storage");

            if (!identity.TryAddObserver(player) ||
                !identity.TryMoveObserverToPending(player) ||
                !identity.TryRemoveObserver(player) ||
                identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("generic observer removal did not release pending storage");

            if (!identity.TryAddObserver(player) || !identity.TryMoveObserverToPending(player))
                return UniTask.FromResult<string>("failed to prepare clear-observers lifecycle check");
            identity.ClearObservers();
            if (identity.pendingObserverStorageAllocated || identity.IsObserverOrPending(player))
                return UniTask.FromResult<string>("ClearObservers did not release pending storage");

            return UniTask.FromResult<string>(null);
        }
        finally
        {
            UnityProxy.DestroyDirectly(testObject);
        }
    }

    private async UniTask RunServerStress(ScenarioContext ctx, List<string> failures)
    {
        for (int cycle = 0; cycle < _stressCycles; cycle++)
        {
            AsyncInstantiateProbe.ResetCycle();
            AsyncInstantiateProbe[] serverResults = null;
            int tokenBase = ServerTokenBase + cycle * 1000;

            await SafeBarrier(ctx, BarrierBase + 20 + cycle,
                $"server cycle {cycle} reset", failures);

            if (ctx.isServer)
            {
                try
                {
                    // Count plus transform arguments exercises a different overload than the
                    // single shape operation and the client count-only operation.
                    var operation = UnityEngine.Object.InstantiateAsync(
                        _serverPrefab,
                        _stressInstancesPerCycle,
                        new Vector3(cycle * 10f, 0f, 0f),
                        Quaternion.identity);

                    if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                    {
                        failures.Add($"server cycle {cycle}: native operation timed out");
                    }
                    else
                    {
                        serverResults = operation.Result;
                        if (serverResults == null || serverResults.Length != _stressInstancesPerCycle)
                        {
                            failures.Add(
                                $"server cycle {cycle}: operation returned " +
                                $"{serverResults?.Length ?? -1}/{_stressInstancesPerCycle} results");
                        }

                        bool sourceSpawned = await WaitUntil(
                            () => AllResultsSpawned(serverResults, _stressInstancesPerCycle),
                            _spawnTimeoutSeconds,
                            ctx);

                        if (!sourceSpawned)
                            failures.Add($"server cycle {cycle}: source results did not become spawned");

                        if (serverResults != null)
                        {
                            for (int i = 0; i < serverResults.Length; i++)
                            {
                                var result = serverResults[i];
                                if (result && result.isSpawned)
                                    result.SetStateAndBroadcast(tokenBase + i);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"server cycle {cycle}: {e.GetType().Name}: {e.Message}");
                    Debug.LogException(e);
                }
            }

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.AllInstancesAreSpawnedAndUnpooled(_stressInstancesPerCycle),
                    _spawnTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: local async spawn incomplete/unpooled check failed " +
                    $"(alive={AsyncInstantiateProbe.aliveCount}, pooled={AsyncInstantiateProbe.sawPooledAsyncInstance})");
            }

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.observerRpcTokenCount == _stressInstancesPerCycle &&
                          AsyncInstantiateProbe.AllExpectedStatesApplied(_stressInstancesPerCycle),
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: immediate traffic incomplete " +
                    $"(rpc={AsyncInstantiateProbe.observerRpcTokenCount}/{_stressInstancesPerCycle})");
            }

            if (AsyncInstantiateProbe.stateMissingAtSpawn)
                failures.Add($"server cycle {cycle}: post-operation state was absent during remote OnSpawned");

            if (ctx.isClient && !await WaitUntil(
                    () => AsyncInstantiateProbe.targetRpcTokenCount == _stressInstancesPerCycle,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: target traffic incomplete " +
                    $"({AsyncInstantiateProbe.targetRpcTokenCount}/{_stressInstancesPerCycle})");
            }

            if (ctx.isServer)
            {
                int expectedObserverPairs = _stressInstancesPerCycle * ctx.expectedConnections;
                if (!await WaitUntil(
                        () => AsyncInstantiateProbe.ObserverPairCount() >= expectedObserverPairs,
                        _stateAndRpcTimeoutSeconds,
                        ctx))
                {
                    failures.Add(
                        $"server cycle {cycle}: observer confirmations incomplete " +
                        $"({AsyncInstantiateProbe.ObserverPairCount()}/{expectedObserverPairs})");
                }
            }

            await SafeBarrier(ctx, BarrierBase + 10 + cycle * 2, $"server cycle {cycle} ready", failures);

            if (!AsyncInstantiateProbe.AllPendingObserverStorageReleased())
                failures.Add($"server cycle {cycle}: pending observer storage was retained after confirmation");

            if (ctx.isServer)
            {
                var instances = AsyncInstantiateProbe.SnapshotInstances();
                for (int i = 0; i < instances.Length; i++)
                {
                    if (instances[i])
                        instances[i].Despawn();
                }
            }

            if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
                failures.Add($"server cycle {cycle}: despawn incomplete ({AsyncInstantiateProbe.aliveCount} alive)");

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.AllDespawnedObjectsDestroyed(_stressInstancesPerCycle),
                    _despawnTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: async objects were retained after despawn " +
                    $"({AsyncInstantiateProbe.despawnedObjectCount}/{_stressInstancesPerCycle} tracked)");
            }

            if (AsyncInstantiateProbe.sawPooledAsyncInstance)
                failures.Add($"server cycle {cycle}: provider pooling leaked onto an async result");
            if (AsyncInstantiateProbe.reusedPooledInstance)
                failures.Add($"server cycle {cycle}: an async result was taken from PurrNet's warm pool");

            await SafeBarrier(ctx, BarrierBase + 11 + cycle * 2, $"server cycle {cycle} despawn", failures);
        }
    }

    private async UniTask<string> TestRapidDespawn(ScenarioContext ctx)
    {
        _rapidDespawnOutcomeReceived = false;
        AsyncInstantiateProbe.ResetCycle();

        // Reset the cross-process outcome before the authoritative peer starts work.
        await ScenarioBarrier.Wait(ctx, BarrierBase + 29, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bool success = true;
            string detail = string.Empty;
            AsyncInstantiateProbe[] results = null;

            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_serverPrefab, _rapidDespawnInstances);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                {
                    success = false;
                    detail = "native operation timed out";
                }
                else
                {
                    results = operation.Result;
                    if (!await WaitUntil(
                            () => AllResultsSpawned(results, _rapidDespawnInstances),
                            _spawnTimeoutSeconds,
                            ctx))
                    {
                        success = false;
                        detail = "source results did not become spawned";
                    }

                    // Do not wait for observers. Spawn and despawn packets can land in the same
                    // receive drain while native remote cloning is still in flight.
                    if (results != null)
                    {
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (results[i])
                                results[i].Despawn();
                        }
                    }

                    if (!await WaitUntil(
                            () => AllResultsDestroyed(results, _rapidDespawnInstances),
                            _despawnTimeoutSeconds,
                            ctx))
                    {
                        success = false;
                        detail = "source results were retained after immediate despawn";
                    }
                }
            }
            catch (Exception e)
            {
                success = false;
                detail = $"{e.GetType().Name}: {e.Message}";
            }

            BroadcastRapidDespawnOutcome(success, detail);
        }

        if (!await WaitUntil(() => _rapidDespawnOutcomeReceived, _operationTimeoutSeconds, ctx))
            return "server did not broadcast its rapid-despawn outcome";

        // Give a cancelled native operation ample opportunity to complete late. A leaked pending
        // transaction would resurrect its identity after the despawn packet was already handled.
        await UniTask.WaitForSeconds(1.5f, cancellationToken: ctx.cancellationToken);

        if (AsyncInstantiateProbe.aliveCount != 0)
            return $"{AsyncInstantiateProbe.aliveCount} identities resurrected after rapid despawn";

        return _rapidDespawnSucceeded ? null : _rapidDespawnDetail;
    }

    private async UniTask<string> TestCancellation(ScenarioContext ctx)
    {
        _cancellationOutcomeReceived = false;
        AsyncInstantiateCancellationIdentity.ResetAll();

        // Ensure every process cleared the previous static outcome before the server can
        // broadcast the new one.
        await ScenarioBarrier.Wait(ctx, BarrierBase + 39, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bool success = true;
            string detail = string.Empty;

            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(
                    _cancellationPrefab,
                    _cancellationInstances);
                operation.allowSceneActivation = false;

                bool reachedGate = await WaitUntil(
                    () => operation.IsWaitingForSceneActivation() || operation.isDone,
                    _operationTimeoutSeconds,
                    ctx);

                if (!reachedGate)
                {
                    success = false;
                    detail = "operation never reached the integration gate";
                }
                else if (operation.isDone)
                {
                    success = false;
                    detail = "operation completed before cancellation could be exercised";
                }
                else
                {
                    operation.Cancel();
                    // Unity documents cancellation itself as asynchronous and does not guarantee
                    // when isDone flips after Cancel. The contract we care about is that no result
                    // reaches Awake/network spawn and that any integrated clone is released.
                }

                await UniTask.WaitForSeconds(1f, cancellationToken: ctx.cancellationToken);

                if (AsyncInstantiateCancellationIdentity.everSpawnedCount != 0)
                {
                    success = false;
                    detail = $"{AsyncInstantiateCancellationIdentity.everSpawnedCount} cancelled identities ever spawned";
                }
                else if (AsyncInstantiateCancellationIdentity.liveCloneCount != 0)
                {
                    success = false;
                    detail = $"{AsyncInstantiateCancellationIdentity.liveCloneCount} cancelled clones leaked";
                }
            }
            catch (Exception e)
            {
                success = false;
                detail = $"{e.GetType().Name}: {e.Message}";
            }

            BroadcastCancellationOutcome(success, detail);
        }

        if (!await WaitUntil(() => _cancellationOutcomeReceived, _operationTimeoutSeconds, ctx))
            return "server did not broadcast its cancellation outcome";

        if (AsyncInstantiateCancellationIdentity.everSpawnedCount != 0)
            return $"local peer ever spawned {AsyncInstantiateCancellationIdentity.everSpawnedCount} cancelled identities";
        if (AsyncInstantiateCancellationIdentity.liveCloneCount != 0)
            return $"local peer leaked {AsyncInstantiateCancellationIdentity.liveCloneCount} cancelled clones";

        return _cancellationSucceeded ? null : _cancellationDetail;
    }

    private async UniTask RunClientAuthoritative(ScenarioContext ctx, List<string> failures)
    {
        if (!_runClientAuthoritative)
            return;

        AsyncInstantiateProbe.ResetCycle();
        _spawnerReceived = false;
        _hasSpawner = false;
        _spawnerId = 0;

        await SafeBarrier(ctx, BarrierBase + 50, "client spawn selection", failures);

        if (ctx.isServer)
        {
            var spawner = PickClientSpawner(ctx);
            BroadcastSpawner(spawner.HasValue, spawner.HasValue ? spawner.Value.id.value : 0);
        }

        if (!await WaitUntil(() => _spawnerReceived, _operationTimeoutSeconds, ctx))
        {
            failures.Add("client spawn: spawner selection was not received");
            await SafeBarrier(ctx, BarrierBase + 51, "client spawn failed selection", failures);
            return;
        }

        if (!_hasSpawner)
        {
            // Dedicated server runs with zero clients are valid for local smoke runs, but cannot
            // exercise client authority. Every normal multi-peer run takes the branch below.
            await SafeBarrier(ctx, BarrierBase + 51, "client spawn skipped", failures);
            return;
        }

        AsyncInstantiateProbe[] localResults = null;
        bool localSpawner = ctx.networkManager.isLocalPlayerReady &&
                            ctx.networkManager.localPlayer.id.value == _spawnerId;

        if (localSpawner)
        {
            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_clientPrefab, _clientInstances);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                {
                    failures.Add("client spawn: native operation timed out on the designated spawner");
                }
                else
                {
                    localResults = operation.Result;
                    if (localResults == null || localResults.Length != _clientInstances)
                        failures.Add($"client spawn: operation returned {localResults?.Length ?? -1}/{_clientInstances}");

                    if (!await WaitUntil(
                            () => AllResultsSpawned(localResults, _clientInstances),
                            _spawnTimeoutSeconds,
                            ctx))
                    {
                        failures.Add("client spawn: designated spawner results did not become spawned");
                    }

                    if (localResults != null)
                    {
                        for (int i = 0; i < localResults.Length; i++)
                        {
                            var result = localResults[i];
                            if (result && result.isSpawned)
                                result.SetStateAndSignalServer(ClientTokenBase + i);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                failures.Add($"client spawn: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.AllInstancesAreSpawnedAndUnpooled(_clientInstances),
                _spawnTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: local spawn incomplete/unpooled check failed " +
                $"(alive={AsyncInstantiateProbe.aliveCount}, pooled={AsyncInstantiateProbe.sawPooledAsyncInstance})");
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.AllInstancesOwnedBy(_spawnerId),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add($"client spawn: not every result became owned by spawner {_spawnerId}");
        }

        if (!await WaitUntil(
                () => (ctx.isServer || localSpawner
                          ? AsyncInstantiateProbe.clientEchoTokenCount == _clientInstances
                          : AsyncInstantiateProbe.observerRpcTokenCount == _clientInstances) &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(_clientInstances),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: bootstrap traffic/state incomplete " +
                $"(echo={AsyncInstantiateProbe.clientEchoTokenCount}, " +
                $"observer={AsyncInstantiateProbe.observerRpcTokenCount}, expected={_clientInstances})");
        }

        if (AsyncInstantiateProbe.stateMissingAtSpawn)
            failures.Add("client spawn: post-operation state was absent during remote OnSpawned");

        if (ctx.isClient && !await WaitUntil(
                () => AsyncInstantiateProbe.targetRpcTokenCount == _clientInstances,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: target traffic incomplete " +
                $"({AsyncInstantiateProbe.targetRpcTokenCount}/{_clientInstances})");
        }


        if ((ctx.role == NetworkRole.Server || localSpawner) && !await WaitUntil(
                () => AsyncInstantiateProbe.forwardedRpcTokenCount == _clientInstances,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: forwarded observer traffic incomplete " +
                $"({AsyncInstantiateProbe.forwardedRpcTokenCount}/{_clientInstances})");
        }

        if (ctx.isServer)
        {
            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.serverRpcTokenCount == _clientInstances,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"client spawn: server received {AsyncInstantiateProbe.serverRpcTokenCount}/{_clientInstances} " +
                    "immediate ServerRpcs");
            }

            int expectedObserverPairs = _clientInstances * ctx.expectedConnections;
            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.ObserverPairCount() >= expectedObserverPairs,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"client spawn: observer confirmations incomplete " +
                    $"({AsyncInstantiateProbe.ObserverPairCount()}/{expectedObserverPairs})");
            }
        }

        await SafeBarrier(ctx, BarrierBase + 51, "client spawn ready", failures);

        if (!AsyncInstantiateProbe.AllPendingObserverStorageReleased())
            failures.Add("client spawn: pending observer storage was retained after confirmation");

        if (ctx.isServer)
        {
            var instances = AsyncInstantiateProbe.SnapshotInstances();
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i])
                    instances[i].Despawn();
            }
        }

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"client spawn: despawn incomplete ({AsyncInstantiateProbe.aliveCount} alive)");

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.AllDespawnedObjectsDestroyed(_clientInstances),
                _despawnTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: async objects were retained after despawn " +
                $"({AsyncInstantiateProbe.despawnedObjectCount}/{_clientInstances} tracked)");
        }


        if (AsyncInstantiateProbe.sawPooledAsyncInstance)
            failures.Add("client spawn: provider pooling leaked onto an async result");
        if (AsyncInstantiateProbe.reusedPooledInstance)
            failures.Add("client spawn: an async result was taken from PurrNet's warm pool");

        await SafeBarrier(ctx, BarrierBase + 52, "client spawn despawn", failures);
    }

    private async UniTask<string> TestAwakeShapeMismatch(ScenarioContext ctx)
    {
        _shapeOutcomeReceived = false;
        AsyncInstantiateAwakeShapeIdentity.ResetAll();

        await ScenarioBarrier.Wait(ctx, BarrierBase + 69, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bool success = true;
            var details = new List<string>();
            AsyncInstantiateAwakeShapeIdentity result = null;

            _shapeDiagnosticSeen = false;
            Application.logMessageReceived += CaptureShapeDiagnostic;

            try
            {
                AsyncInstantiateAwakeShapeMutator.mutation = _awakeMutation;
                AsyncInstantiateAwakeShapeMutator.mutationEnabled = true;

                // The template is only active for this call. The mutator ignores the template
                // itself and changes the clone in Awake during native async integration.
                _shapePrefab.gameObject.SetActive(true);
                var operation = UnityEngine.Object.InstantiateAsync(_shapePrefab);

                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                {
                    success = false;
                    details.Add("native operation timed out");
                }
                else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                {
                    success = false;
                    details.Add("native operation did not preserve its local result");
                }
                else
                {
                    result = operation.Result[0];
                    await UniTask.NextFrame(ctx.cancellationToken);

                    if (AsyncInstantiateAwakeShapeMutator.mutatedCloneCount != 1)
                    {
                        success = false;
                        details.Add(
                            $"Awake mutation ran {AsyncInstantiateAwakeShapeMutator.mutatedCloneCount} times");
                    }

                    int identityCount = result.GetComponentsInChildren<NetworkIdentity>(true).Length;
                    if (identityCount == 2)
                    {
                        success = false;
                        details.Add("clone topology did not change");
                    }

                    if (result.isSpawned || AsyncInstantiateAwakeShapeIdentity.spawnedCount != 0)
                    {
                        success = false;
                        details.Add("shape-mismatched clone was network-spawned");
                    }

                    var resultIdentities = result.GetComponentsInChildren<NetworkIdentity>(true);
                    for (var i = 0; i < resultIdentities.Length; i++)
                    {
                        if (!resultIdentities[i].isSpawned)
                            continue;
                        success = false;
                        details.Add("a mutated NetworkIdentity was network-spawned");
                        break;
                    }
                    if (AsyncInstantiateAwakeShapeMutator.anyMutatedIdentitySpawned)
                    {
                        success = false;
                        details.Add("a detached Awake-mutated identity was network-spawned");
                    }

                    if (!await WaitUntil(() => _shapeDiagnosticSeen, 5f, ctx))
                    {
                        success = false;
                        details.Add("no diagnostic named InstantiateAsync and the prefab");
                    }
                }
            }
            catch (Exception e)
            {
                success = false;
                details.Add($"{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _shapePrefab.gameObject.SetActive(false);
                AsyncInstantiateAwakeShapeMutator.mutationEnabled = false;
                Application.logMessageReceived -= CaptureShapeDiagnostic;

                if (result)
                {
                    if (result.isSpawned)
                        result.Despawn();
                    else
                        UnityProxy.DestroyDirectly(result.gameObject);
                }
                AsyncInstantiateAwakeShapeMutator.CleanupDetachedObjects();
            }

            BroadcastShapeOutcome(success, string.Join(", ", details));
        }

        if (!await WaitUntil(() => _shapeOutcomeReceived, _operationTimeoutSeconds, ctx))
            return "server did not broadcast its shape-validation outcome";

        await UniTask.WaitForSeconds(0.5f, cancellationToken: ctx.cancellationToken);

        if (AsyncInstantiateAwakeShapeIdentity.spawnedCount != 0)
            return $"local peer spawned {AsyncInstantiateAwakeShapeIdentity.spawnedCount} shape-mismatched identities";

        return _shapeSucceeded ? null : _shapeDetail;
    }

    private void CaptureShapeDiagnostic(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;

        if (condition.IndexOf("InstantiateAsync", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (condition.IndexOf(_shapePrefab.name, StringComparison.OrdinalIgnoreCase) < 0)
            return;

        _shapeDiagnosticSeen = true;
    }

    private static bool AllResultsSpawned(AsyncInstantiateProbe[] results, int expectedCount)
    {
        if (results == null || results.Length != expectedCount)
            return false;

        for (int i = 0; i < results.Length; i++)
        {
            if (!results[i] || !results[i].isSpawned || results[i].shouldBePooled)
                return false;
        }

        return true;
    }

    private static bool AllResultsDestroyed(AsyncInstantiateProbe[] results, int expectedCount)
    {
        if (results == null || results.Length != expectedCount)
            return false;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i])
                return false;
        }

        return true;
    }

    private static async UniTask<bool> WaitUntil(
        Func<bool> predicate,
        float timeoutSeconds,
        ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(predicate, timeoutSeconds, ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async UniTask SafeBarrier(
        ScenarioContext ctx,
        int id,
        string name,
        List<string> failures)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, id, _barrierTimeoutSeconds);
        }
        catch (Exception e)
        {
            failures.Add($"{name} barrier: {e.Message}");
        }
    }

    private static PlayerID? PickClientSpawner(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        PlayerID? hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? remote = null;
        PlayerID? fallback = null;
        var players = manager.players;

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            if (!fallback.HasValue || player.id.value < fallback.Value.id.value)
                fallback = player;

            if (hostLocal.HasValue && player == hostLocal.Value)
                continue;

            if (!remote.HasValue || player.id.value < remote.Value.id.value)
                remote = player;
        }

        return remote ?? fallback;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastCancellationOutcome(bool success, string detail)
    {
        _cancellationSucceeded = success;
        _cancellationDetail = detail;
        _cancellationOutcomeReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastRapidDespawnOutcome(bool success, string detail)
    {
        _rapidDespawnSucceeded = success;
        _rapidDespawnDetail = detail;
        _rapidDespawnOutcomeReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastShapeOutcome(bool success, string detail)
    {
        _shapeSucceeded = success;
        _shapeDetail = detail;
        _shapeOutcomeReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastSpawner(bool hasSpawner, ulong spawnerId)
    {
        _hasSpawner = hasSpawner;
        _spawnerId = spawnerId;
        _spawnerReceived = true;
    }
}

#else

/// <summary>Keeps the PlayMode scene compatible with Unity versions before 2022.3.20.</summary>
public sealed class AsyncInstantiateScenario : Scenario
{
    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return UniTask.FromResult(ScenarioResult.Ok(
            "Object.InstantiateAsync is unavailable before Unity 2022.3.20"));
    }
}

#endif
