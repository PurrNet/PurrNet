using System.Collections.Generic;
using PurrNet;

public class ObserverEventsIdentity : NetworkIdentity
{
    public struct ObserverRecord
    {
        public ulong playerId;
        public bool hasIsSpawner;
        public bool isSpawner;
    }

    public static ObserverEventsIdentity LocalInstance;
    public static int ServerDoneCount;

    public static readonly List<ObserverRecord> PreAddedNoArg = new();
    public static readonly List<ObserverRecord> PreAddedWithSpawner = new();
    public static readonly List<ObserverRecord> AddedNoArg = new();
    public static readonly List<ObserverRecord> AddedWithSpawner = new();
    public static readonly List<ObserverRecord> Removed = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerDoneCount = 0;
        PreAddedNoArg.Clear();
        PreAddedWithSpawner.Clear();
        AddedNoArg.Clear();
        AddedWithSpawner.Clear();
        Removed.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnPreObserverAdded(PlayerID player)
    {
        PreAddedNoArg.Add(new ObserverRecord { playerId = player.id.value });
    }

    protected override void OnPreObserverAdded(PlayerID player, bool isSpawner)
    {
        PreAddedWithSpawner.Add(new ObserverRecord
        {
            playerId = player.id.value,
            hasIsSpawner = true,
            isSpawner = isSpawner
        });
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        AddedNoArg.Add(new ObserverRecord { playerId = player.id.value });
    }

    protected override void OnObserverAdded(PlayerID player, bool isSpawner)
    {
        AddedWithSpawner.Add(new ObserverRecord
        {
            playerId = player.id.value,
            hasIsSpawner = true,
            isSpawner = isSpawner
        });
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        Removed.Add(new ObserverRecord { playerId = player.id.value });
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        ServerDoneCount++;
    }
}
