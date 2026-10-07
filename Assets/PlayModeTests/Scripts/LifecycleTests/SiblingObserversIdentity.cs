using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SiblingObserversIdentity : NetworkIdentity
{
    public const int Slots = 2;

    // Index of this component on the GameObject, assigned on the prefab.
    public int slot;

    [SerializeField] private SyncVar<int> _counter = new(0, sendIntervalInSeconds: 0f, ownerAuth: false);

    public static readonly SiblingObserversIdentity[] LocalInstances = new SiblingObserversIdentity[Slots];
    public static int ServerReadyCount;

    public static readonly List<SiblingObserversIdentity>[] ClientLive = { new(), new() };
    public static readonly int[] ClientSpawned = new int[Slots];
    public static readonly int[] ClientDespawned = new int[Slots];
    public static readonly int[] LastPing = new int[Slots];

    public static readonly List<ulong>[] ObserverAddedCalls = { new(), new() };
    public static readonly List<ulong>[] ObserverRemovedCalls = { new(), new() };

    public static void ResetAll()
    {
        ServerReadyCount = 0;

        for (var i = 0; i < Slots; i++)
        {
            LocalInstances[i] = null;
            ClientLive[i].Clear();
            ClientSpawned[i] = 0;
            ClientDespawned[i] = 0;
            LastPing[i] = 0;
            ObserverAddedCalls[i].Clear();
            ObserverRemovedCalls[i].Clear();
        }
    }

    public static int CountLive(int slot)
    {
        int count = 0;
        var live = ClientLive[slot];
        for (var i = 0; i < live.Count; i++)
        {
            if (live[i] && live[i].isSpawned)
                count++;
        }
        return count;
    }

    public int counter
    {
        get => _counter.value;
        set => _counter.value = value;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstances[slot] = this;

        if (asServer)
            return;

        ClientSpawned[slot]++;
        ClientLive[slot].Add(this);
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
            return;

        ClientDespawned[slot]++;
        ClientLive[slot].Remove(this);
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        ObserverAddedCalls[slot].Add(player.id.value);
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        ObserverRemovedCalls[slot].Add(player.id.value);
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ObserversRpc(runLocally: true)]
    public void Ping(int value)
    {
        LastPing[slot] = value;
    }
}
