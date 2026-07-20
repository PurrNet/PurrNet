using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Client-authoritative player movement: the server spawns one owner-auth NetworkTransform per
// connected player and hands ownership to that player; each client random-walks the player it
// owns. Stresses the N-player "everyone moves, everyone sees everyone" fan-out.
public class PlayerMovementBenchmarkScenario : BenchmarkScenarioBase
{
    private const float SPAWN_READY_TIMEOUT = 30f;

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab(manager, nameof(PlayerMovementBenchmarkScenario) + "_Player", typeof(BenchmarkPlayer));
    }

    protected override async UniTask Spawn(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            SpawnSuppressed(() =>
            {
                var players = ctx.networkManager.players;
                for (int i = 0; i < players.Count; i++)
                {
                    var inst = Instantiate(_prefab);
                    inst.gameObject.SetActive(true);
                    inst.transform.position = new Vector3(
                        UnityEngine.Random.Range(-25f, 25f), 0f, UnityEngine.Random.Range(-25f, 25f));
                    inst.GiveOwnership(players[i]);
                    _spawned.Add(inst);
                }
            });
        }

        // Clients wait until they actually control their player before the measurement window starts,
        // so the spawn/ownership storm doesn't bleed into the steady-state numbers.
        if (ctx.isClient)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(HasControlledPlayer, SPAWN_READY_TIMEOUT, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                Debug.LogError("[PlayerMovementBenchmark] client never received a controllable player");
            }
        }
    }

    protected override void Tick(ScenarioContext ctx, float elapsed, float dt)
    {
        var all = BenchmarkPlayer.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].isController)
                all[i].Step(dt);
        }
    }

    protected override int ObjectCount(ScenarioContext ctx) => ctx.isServer ? _spawned.Count : CountControlled();

    private static bool HasControlledPlayer() => CountControlled() > 0;

    private static int CountControlled()
    {
        int n = 0;
        var all = BenchmarkPlayer.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].isController)
                n++;
        return n;
    }
}
