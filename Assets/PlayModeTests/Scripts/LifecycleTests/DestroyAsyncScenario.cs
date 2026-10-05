using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class DestroyAsyncScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _destroyTimeoutSeconds = 60f;
    [SerializeField] private int _groups = 30;
    [SerializeField] private int _leavesPerGroup = 100;
    [SerializeField] private int _childLeaves = 3000;

    private DestroyAsyncRoot _rootPrefab;
    private DestroyAsyncParent _parentPrefab;
    private DestroyAsyncChild _childPrefab;

    void CreatePrefabs()
    {
        var rootGo = new GameObject(nameof(DestroyAsyncScenario) + "_Root");
        _rootPrefab = rootGo.AddComponent<DestroyAsyncRoot>();
        rootGo.AddComponent<DestroyAsyncExtra>();

        var nestedGo = new GameObject("Nested");
        nestedGo.transform.SetParent(rootGo.transform);
        nestedGo.AddComponent<DestroyAsyncExtra>();

        for (var g = 0; g < _groups; g++)
        {
            var group = new GameObject("Group");
            group.transform.SetParent(rootGo.transform);

            for (var l = 0; l < _leavesPerGroup; l++)
                new GameObject("Leaf").transform.SetParent(group.transform);
        }

        rootGo.SetActive(false);

        var parentGo = new GameObject(nameof(DestroyAsyncScenario) + "_Parent");
        _parentPrefab = parentGo.AddComponent<DestroyAsyncParent>();
        parentGo.SetActive(false);

        var childGo = new GameObject(nameof(DestroyAsyncScenario) + "_Child");
        _childPrefab = childGo.AddComponent<DestroyAsyncChild>();

        for (var l = 0; l < _childLeaves; l++)
            new GameObject("Leaf").transform.SetParent(childGo.transform);

        childGo.SetActive(false);

        DestroyAsyncRoot.ResetAll();
        DestroyAsyncExtra.ResetAll();
        DestroyAsyncParent.ResetAll();
        DestroyAsyncChild.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefabs();
        manager.prefabProvider.AddRuntimePrefab(_rootPrefab.name, _rootPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_parentPrefab.name, _parentPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_childPrefab.name, _childPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

        var rootResult = await RunRootPhase(ctx, failures);
        if (rootResult.HasValue)
            return rootResult.Value;

        var parentedResult = await RunParentedPhase(ctx, failures);
        if (parentedResult.HasValue)
            return parentedResult.Value;

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    async UniTask<ScenarioResult?> RunRootPhase(ScenarioContext ctx, List<string> failures)
    {
        if (ctx.isServer)
            Instantiate(_rootPrefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => DestroyAsyncRoot.LocalInstance != null,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("root spawn timeout");
        }

        var root = DestroyAsyncRoot.LocalInstance;

        if (ctx.isClient)
            root.SignalReady();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => DestroyAsyncRoot.ServerReadyCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"root ready timeout: got {DestroyAsyncRoot.ServerReadyCount}/{ctx.expectedConnections}");
            }

            UnityProxy.DestroyAsync(root.gameObject, 0.01f);

            if (DestroyAsyncRoot.OnDespawnedNoArg != 1)
                failures.Add($"root OnDespawned() must fire inside DestroyAsync, got {DestroyAsyncRoot.OnDespawnedNoArg}");
            if (DestroyAsyncExtra.OnDespawnedNoArg != 2)
                failures.Add($"extra OnDespawned() must fire inside DestroyAsync, got {DestroyAsyncExtra.OnDespawnedNoArg}/2");
            if (!root || !root.isDestroyingAsync || root.isSpawned || root.id.HasValue)
                failures.Add("root must be an unspawned id-less shell right after DestroyAsync");
            if (root && root.gameObject.activeSelf)
                failures.Add("root shell must be inactive right after DestroyAsync");
            if (AsyncDestroyer.pendingCount != 1)
                failures.Add($"expected exactly one pending shell, got {AsyncDestroyer.pendingCount}");

            if (root)
            {
                root.Despawn();
                UnityProxy.Destroy(root.gameObject);
            }

            await UniTask.NextFrame(ctx.cancellationToken);
            await UniTask.NextFrame(ctx.cancellationToken);

            if (!root)
                failures.Add("repeated Despawn/Destroy on the shell must not bypass the async destroy");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => DestroyAsyncRoot.DestroyFrame >= 0 && DestroyAsyncExtra.Destroyed >= 2 &&
                      AsyncDestroyer.pendingCount == 0,
                _destroyTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"root destroy timeout: despawned={DestroyAsyncRoot.OnDespawnedNoArg}, " +
                $"destroyFrame={DestroyAsyncRoot.DestroyFrame}, extraDestroyed={DestroyAsyncExtra.Destroyed}, " +
                $"pending={AsyncDestroyer.pendingCount}");
        }

        if (DestroyAsyncRoot.OnDespawnedNoArg != 1)
            failures.Add($"root OnDespawned() expected 1, got {DestroyAsyncRoot.OnDespawnedNoArg}");
        if (DestroyAsyncExtra.OnDespawnedNoArg != 2)
            failures.Add($"extra OnDespawned() expected 2, got {DestroyAsyncExtra.OnDespawnedNoArg}");
        if (DestroyAsyncRoot.DestroyFrame - DestroyAsyncRoot.DespawnFrame < 2)
            failures.Add(
                $"root destroy must span frames: despawn={DestroyAsyncRoot.DespawnFrame}, destroy={DestroyAsyncRoot.DestroyFrame}");
        if (DestroyAsyncRoot.ChildrenAtDestroy != 0)
            failures.Add($"root must be destroyed last, had {DestroyAsyncRoot.ChildrenAtDestroy} children");
        if (DestroyAsyncRoot.ActiveAtDestroy)
            failures.Add("root was active when destroyed");
        if (!DestroyAsyncRoot.FlaggedAtDestroy)
            failures.Add("root was not flagged isDestroyingAsync when destroyed");

        return null;
    }

    async UniTask<ScenarioResult?> RunParentedPhase(ScenarioContext ctx, List<string> failures)
    {
        if (ctx.isServer)
        {
            var parent = Instantiate(_parentPrefab);
            Instantiate(_childPrefab, parent.transform);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => DestroyAsyncParent.LocalInstance != null && DestroyAsyncChild.First != null,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"parented spawn timeout: parent={DestroyAsyncParent.LocalInstance != null}, " +
                $"child={DestroyAsyncChild.First != null}");
        }

        var localParent = DestroyAsyncParent.LocalInstance;

        if (DestroyAsyncChild.First.transform.parent != localParent.transform)
            failures.Add("first child did not spawn under the parent");

        if (ctx.isClient)
            localParent.SignalReady();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => DestroyAsyncParent.ServerReadyCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"parented ready timeout: got {DestroyAsyncParent.ServerReadyCount}/{ctx.expectedConnections}");
            }

            var first = DestroyAsyncChild.First;
            UnityProxy.DestroyAsync(first.gameObject, 0.01f);

            if (!first || first.transform.parent)
                failures.Add("child shell must be detached from its network parent right after DestroyAsync");
            if (localParent.directChildren.Count != 0)
                failures.Add($"parent still lists {localParent.directChildren.Count} direct children after DestroyAsync");
            if (localParent.transform.childCount != 0)
                failures.Add($"parent still has {localParent.transform.childCount} transform children after DestroyAsync");

            Instantiate(_childPrefab, localParent.transform);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => DestroyAsyncChild.Second != null && DestroyAsyncChild.DestroyFrame >= 0 &&
                      AsyncDestroyer.pendingCount == 0,
                _destroyTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"parented destroy timeout: second={DestroyAsyncChild.Second != null}, " +
                $"despawned={DestroyAsyncChild.OnDespawnedNoArg}, destroyFrame={DestroyAsyncChild.DestroyFrame}, " +
                $"pending={AsyncDestroyer.pendingCount}");
        }

        if (DestroyAsyncChild.OnDespawnedNoArg != 1)
            failures.Add($"child OnDespawned() expected 1, got {DestroyAsyncChild.OnDespawnedNoArg}");
        if (!DestroyAsyncChild.DetachedAtDestroy)
            failures.Add("child shell was still parented when destroyed");
        if (DestroyAsyncChild.DestroyFrame - DestroyAsyncChild.DespawnFrame < 2)
            failures.Add(
                $"child destroy must span frames: despawn={DestroyAsyncChild.DespawnFrame}, destroy={DestroyAsyncChild.DestroyFrame}");

        var second = DestroyAsyncChild.Second;
        if (!second || !second.isSpawned)
            failures.Add("second child is not spawned");
        else if (second.transform.parent != localParent.transform)
            failures.Add("second child did not spawn under the parent");
        else if (localParent.transform.childCount != 1)
            failures.Add($"parent expected exactly 1 transform child, got {localParent.transform.childCount}");

        if (ctx.isClient)
            localParent.SignalDone();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => DestroyAsyncParent.ServerDoneCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"parented done timeout: got {DestroyAsyncParent.ServerDoneCount}/{ctx.expectedConnections}");
            }

            Destroy(localParent.gameObject);
        }

        return null;
    }
}
