using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Server-authoritative state replication: the server owns and continuously mutates N
// NetworkTransforms; clients only observe. Stresses server->all fan-out.
// -benchBots N adds N CreateBot players on the server.
public class BenchmarkScenario : BenchmarkScenarioBase
{
    [SerializeField] private int _objectCount = 50;

    private readonly List<PlayerID> _bots = new();

    public override void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        base.ApplyOverrides(objectCount, pingsPerSecond);
        if (objectCount is > 0)
            _objectCount = objectCount.Value;
    }

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        var nt = CreatePrefab(manager, nameof(BenchmarkScenario) + "_Obj");
        nt.adaptiveSync = false;
    }

    protected override UniTask Spawn(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            if (CommandLineUtils.TryGetArgument("-benchBots", out var bots) && int.TryParse(bots, out var botCount) &&
                ctx.networkManager.TryGetModule<PlayersManager>(true, out var players))
            {
                for (int i = 0; i < botCount; i++)
                    _bots.Add(players.CreateBot());
            }

            SpawnSuppressed(() =>
            {
                for (int i = 0; i < _objectCount; i++)
                {
                    var inst = Instantiate(_prefab);
                    inst.gameObject.SetActive(true);
                    inst.transform.position = new Vector3(i, 0, 0);
                    _spawned.Add(inst);
                }
            });
        }

        return UniTask.CompletedTask;
    }

    protected override void Tick(ScenarioContext ctx, float elapsed, float dt)
    {
        if (!ctx.isServer)
            return;

        for (int i = 0; i < _spawned.Count; i++)
        {
            var inst = _spawned[i];
            if (!inst)
                continue;
            float phase = i * 0.3f;
            inst.transform.position = new Vector3(
                Mathf.Sin(elapsed + phase) * 5f,
                Mathf.Cos(elapsed * 0.5f + phase) * 5f,
                i);
        }
    }

    protected override void Despawn(ScenarioContext ctx)
    {
        base.Despawn(ctx);

        if (_bots.Count > 0 && ctx.networkManager.TryGetModule<PlayersManager>(true, out var players))
        {
            for (int i = 0; i < _bots.Count; i++)
                players.KickPlayer(_bots[i]);
            _bots.Clear();
        }
    }

    protected override int ObjectCount(ScenarioContext ctx) => ctx.isServer ? _spawned.Count : _objectCount;
}
