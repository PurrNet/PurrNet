using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncHashSet{T}"/>. The owning client drives every operation; the
/// server and all other observers must converge, and only the owner is the controller.
/// </summary>
public class SyncHashsetOwnerAuthIdentity : NetworkIdentity
{
    public static readonly int[] ExpectedFinal = { 98, 99 };

    [SerializeField] private SyncHashSet<int> _set = new(ownerAuth: true);

    public static SyncHashsetOwnerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ConvergedCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ConvergedCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        PhaseDoneReceived = false;
    }

    public bool MatchesFinal()
    {
        if (_set.Count != ExpectedFinal.Length) return false;
        for (int i = 0; i < ExpectedFinal.Length; i++)
            if (!_set.Contains(ExpectedFinal[i])) return false;
        return true;
    }

    public string Describe()
    {
        var items = new List<int>();
        foreach (var v in _set) items.Add(v);
        items.Sort();
        return $"{{{string.Join(",", items)}}}";
    }

    public void RunOwnerSequence()
    {
        _set.Clear();
        _set.Add(10); _set.Add(20); _set.Add(30);
        _set.Add(40);   // Add        -> 10,20,30,40
        _set.Add(40);   // duplicate  -> (no change)
        _set.Remove(20);// Remove     -> 10,30,40
        _set.Clear();   // Clear      -> (empty)
        _set.Add(99);   // refill     -> 99
        _set.Add(98);   //            -> 98,99
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalConverged(RPCInfo info = default) => ConvergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
