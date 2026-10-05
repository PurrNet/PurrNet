using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Network objects spawned under a plain transform that only exists on the server must land as
/// siblings under their nearest networked parent on clients, not nested inside each other.
/// </summary>
public class ServerOnlyHolderScenario : Scenario
{
    [SerializeField] private int _itemsPerHolder = 4;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 9900;
    private const int ParentCount = 2;

    private ServerOnlyHolderParent _bareParentPrefab;
    private ServerOnlyHolderParent _modelParentPrefab;
    private ServerOnlyHolderItem _itemPrefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        ServerOnlyHolderParent.ResetAll();
        ServerOnlyHolderItem.ResetAll();

        _bareParentPrefab = CreatePrefab<ServerOnlyHolderParent>("ServerOnlyHolderBareParent");
        _modelParentPrefab = CreatePrefab<ServerOnlyHolderParent>("ServerOnlyHolderModelParent");
        _modelParentPrefab.hasModel = true;
        new GameObject("Model").transform.SetParent(_modelParentPrefab.transform, false);
        _itemPrefab = CreatePrefab<ServerOnlyHolderItem>("ServerOnlyHolderItem");

        manager.prefabProvider.AddRuntimePrefab(_bareParentPrefab.name, _bareParentPrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(_modelParentPrefab.name, _modelParentPrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(_itemPrefab.name, _itemPrefab.gameObject, false);
    }

    private static T CreatePrefab<T>(string prefabName) where T : NetworkIdentity
    {
        var go = new GameObject(prefabName);
        var identity = go.AddComponent<T>();
        go.SetActive(false);
        return identity;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        ServerOnlyHolderParent bare = null;
        ServerOnlyHolderParent withModel = null;

        if (ctx.isServer)
        {
            bare = Instantiate(_bareParentPrefab);
            withModel = Instantiate(_modelParentPrefab);
            SpawnUnderServerOnlyHolder(bare.transform);
            SpawnUnderServerOnlyHolder(withModel.transform);
        }

        int expectedItems = _itemsPerHolder * ParentCount;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerOnlyHolderParent.AliveCount == ParentCount
                      && ServerOnlyHolderItem.AliveCount == expectedItems,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: parents={ServerOnlyHolderParent.AliveCount}/{ParentCount}, " +
                $"items={ServerOnlyHolderItem.AliveCount}/{expectedItems}");
        }

        var failure = ctx.role == NetworkRole.Client ? CheckClientHierarchy() : null;

        await ScenarioBarrier.Wait(ctx, BarrierBase, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bare.Despawn();
            withModel.Despawn();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerOnlyHolderParent.AliveCount == 0 && ServerOnlyHolderItem.AliveCount == 0,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                (failure != null ? failure + "; " : "") +
                $"despawn incomplete: parents={ServerOnlyHolderParent.AliveCount}, items={ServerOnlyHolderItem.AliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        return failure == null
            ? ScenarioResult.Ok($"{expectedItems} items under server-only holders landed as siblings under their networked parents")
            : ScenarioResult.Fail(failure);
    }

    private void SpawnUnderServerOnlyHolder(Transform parent)
    {
        var holder = new GameObject("ServerOnlyHolder").transform;
        holder.SetParent(parent, false);

        for (int i = 0; i < _itemsPerHolder; i++)
            Instantiate(_itemPrefab, holder);
    }

    private string CheckClientHierarchy()
    {
        foreach (var item in ServerOnlyHolderItem.all)
        {
            var parent = item.transform.parent;
            if (!parent || !parent.GetComponent<ServerOnlyHolderParent>())
                return $"item '{item.name}' ({item.id}) is under '{(parent ? parent.name : "<root>")}' instead of its networked parent";
        }

        foreach (var parent in ServerOnlyHolderParent.all)
        {
            var trs = parent.transform;
            int items = 0;
            for (int i = 0; i < trs.childCount; i++)
            {
                if (trs.GetChild(i).GetComponent<ServerOnlyHolderItem>())
                    items++;
            }

            if (items != _itemsPerHolder)
                return $"'{parent.name}' has {items}/{_itemsPerHolder} items as direct children";

            if (parent.hasModel && trs.GetChild(0).name != "Model")
                return $"'{parent.name}' Model child was moved from index 0 by the spawned items";
        }

        return null;
    }
}
