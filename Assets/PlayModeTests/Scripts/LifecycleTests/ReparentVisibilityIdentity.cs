using System.Collections.Generic;
using PurrNet;

public class ReparentVisibilityIdentity : NetworkIdentity
{
    public const int Slots = 2;
    public const int Container = 0;
    public const int Item = 1;

    // Container or Item, assigned on the prefab.
    public int slot;

    // Set by the server before it spawns the container, which then starts out hidden from this player.
    public static PlayerID? HideContainerFrom;

    public static readonly ReparentVisibilityIdentity[] LocalInstances = new ReparentVisibilityIdentity[Slots];
    public static readonly int[] ClientSpawned = new int[Slots];
    public static readonly int[] ClientDespawned = new int[Slots];

    public static readonly List<ulong>[] ObserverAddedCalls = { new(), new() };
    public static readonly List<ulong>[] ObserverRemovedCalls = { new(), new() };

    public static void ResetAll()
    {
        HideContainerFrom = null;

        for (var i = 0; i < Slots; i++)
        {
            LocalInstances[i] = null;
            ClientSpawned[i] = 0;
            ClientDespawned[i] = 0;
            ObserverAddedCalls[i].Clear();
            ObserverRemovedCalls[i].Clear();
        }
    }

    protected override void OnEarlySpawn(bool asServer)
    {
        gameObject.SetActive(true);

        // The lists can already be filled here, before anyone has been sent the object.
        if (asServer && slot == Container && HideContainerFrom.HasValue)
            BlacklistPlayer(HideContainerFrom.Value);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstances[slot] = this;

        if (!asServer)
            ClientSpawned[slot]++;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!asServer)
            ClientDespawned[slot]++;
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        ObserverAddedCalls[slot].Add(player.id.value);
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        ObserverRemovedCalls[slot].Add(player.id.value);
    }
}
