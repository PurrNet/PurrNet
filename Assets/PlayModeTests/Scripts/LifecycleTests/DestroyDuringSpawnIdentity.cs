using PurrNet;
using UnityEngine;

internal enum DestroyDuringSpawnStage
{
    None,
    Awake,
    EarlySpawn,
    EarlySpawnAsServer,
    Spawned,
    SpawnedAsServer,
    Despawned,
    DespawnedAsServer
}

public class DestroyDuringSpawnIdentity : NetworkIdentity
{
    public static int ServerSawCount;
    public static int DestroyCallCount;

    [SerializeField] private DestroyDuringSpawnStage _stage;

    private bool _destroyCalled;

    internal DestroyDuringSpawnStage Stage => _stage;

    public static void ResetAll()
    {
        ServerSawCount = 0;
        DestroyCallCount = 0;
    }

    internal void Configure(DestroyDuringSpawnStage stage)
    {
        _stage = stage;
    }

    private void Awake()
    {
        TryDestroy(DestroyDuringSpawnStage.Awake);
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
        if (isServer)
            ServerSawCount++;
        TryDestroy(DestroyDuringSpawnStage.EarlySpawn);
    }

    protected override void OnEarlySpawn(bool asServer)
    {
        if (asServer)
            TryDestroy(DestroyDuringSpawnStage.EarlySpawnAsServer);
    }

    protected override void OnSpawned()
    {
        TryDestroy(DestroyDuringSpawnStage.Spawned);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
            TryDestroy(DestroyDuringSpawnStage.SpawnedAsServer);
    }

    protected override void OnDespawned()
    {
        TryDestroy(DestroyDuringSpawnStage.Despawned);
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
            TryDestroy(DestroyDuringSpawnStage.DespawnedAsServer);
    }

    private void TryDestroy(DestroyDuringSpawnStage stage)
    {
        if (_destroyCalled || _stage != stage)
            return;

        _destroyCalled = true;
        DestroyCallCount++;
        Destroy(gameObject);
    }
}
