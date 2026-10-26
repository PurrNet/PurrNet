using System.Collections.Generic;
using PurrNet;

public class HiddenParentWhitelistIdentity : NetworkIdentity
{
    public const int Slots = 2;
    public const int Root = 0;
    public const int Child = 1;

    // Root or Child, assigned on the prefab.
    public int slot;

    public static readonly HiddenParentWhitelistIdentity[] LocalInstances = new HiddenParentWhitelistIdentity[Slots];
    public static int ServerReadyCount;

    public static readonly List<HiddenParentWhitelistIdentity>[] ClientLive = { new(), new() };
    public static readonly int[] ClientSpawned = new int[Slots];
    public static readonly int[] ClientDespawned = new int[Slots];
    public static int OrphanChildSpawns;

    public static readonly List<ulong>[] ObserverAddedCalls = { new(), new() };
    public static readonly List<ulong>[] ObserverRemovedCalls = { new(), new() };

    public static void ResetAll()
    {
        ServerReadyCount = 0;
        OrphanChildSpawns = 0;

        for (var i = 0; i < Slots; i++)
        {
            LocalInstances[i] = null;
            ClientLive[i].Clear();
            ClientSpawned[i] = 0;
            ClientDespawned[i] = 0;
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

    public static HiddenParentWhitelistIdentity FirstLive(int slot)
    {
        var live = ClientLive[slot];
        for (var i = 0; i < live.Count; i++)
        {
            if (live[i] && live[i].isSpawned)
                return live[i];
        }
        return null;
    }

    protected override void OnEarlySpawn()
    {
        if (slot == Root)
            gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstances[slot] = this;

        if (asServer)
            return;

        ClientSpawned[slot]++;
        ClientLive[slot].Add(this);

        if (slot != Child)
            return;

        var parent = transform.parent;
        if (!parent || !parent.TryGetComponent<HiddenParentWhitelistIdentity>(out _))
            OrphanChildSpawns++;
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
}
