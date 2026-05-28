using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncList{T}"/>. The owning client drives every operation; the
/// server and all other observers must converge, and only the owner is the controller.
/// </summary>
public class SyncListOwnerAuthIdentity : NetworkIdentity
{
    public static readonly int[] ExpectedFinal = { 99, 98 };

    [SerializeField] private SyncList<int> _list = new(ownerAuth: true);

    public static SyncListOwnerAuthIdentity LocalInstance;
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

    public bool MatchesFinal() => SequenceEquals(_list.list, ExpectedFinal);
    public string Describe() => $"[{string.Join(",", _list.list)}]";

    /// <summary>Owner-only: seed then mutate, ending at <see cref="ExpectedFinal"/>.</summary>
    public void RunOwnerSequence()
    {
        _list.Clear();
        _list.Add(10);
        _list.Add(20);
        _list.Add(30);        // -> 10,20,30
        _list.Add(40);        // Added           -> 10,20,30,40
        _list.Insert(0, 5);   // Insert          -> 5,10,20,30,40
        _list[2] = 21;        // Set             -> 5,10,21,30,40
        _list.Remove(30);     // Removed (value) -> 5,10,21,40
        _list.RemoveAt(0);    // Removed (index) -> 10,21,40
        _list.Clear();        // Cleared         -> (empty)
        _list.Add(99);        // refill          -> 99
        _list.Add(98);        //                 -> 99,98
    }

    private static bool SequenceEquals(IReadOnlyList<int> a, int[] b)
    {
        if (a.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
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
