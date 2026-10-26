using System.Collections.Generic;
using PurrNet;

public class ManualSpawnVisibilityIdentity : NetworkIdentity
{
    public static int ClientDespawned;
    public static int LastPing;
    public static readonly List<ulong> ObserverRemovedCalls = new();

    public static void ResetAll()
    {
        ClientDespawned = 0;
        LastPing = 0;
        ObserverRemovedCalls.Clear();
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!asServer)
            ClientDespawned++;
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        ObserverRemovedCalls.Add(player.id.value);
    }

    [ObserversRpc(runLocally: true)]
    public void Ping(int value)
    {
        LastPing = value;
    }
}
