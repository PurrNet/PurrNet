using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncList{T}"/>. Exercises initial-state delivery and every list
/// operation, asserting every observer converges to the same final contents.
/// </summary>
public class SyncListServerAuthIdentity : NetworkIdentity
{
    public static readonly int[] InitialValues = { 10, 20, 30 };
    public static readonly int[] ExpectedFinal = { 99, 98 };

    [SerializeField] private SyncList<int> _list = new(ownerAuth: false);

    public static SyncListServerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int InitialMatchCount;
    public static int ConvergedCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        InitialMatchCount = 0;
        ConvergedCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public bool MatchesInitial() => SequenceEquals(_list.list, InitialValues);
    public bool MatchesFinal() => SequenceEquals(_list.list, ExpectedFinal);
    public string Describe() => $"[{string.Join(",", _list.list)}]";

    /// <summary>Server-only: seed the initial contents (delivered to observers as initial state).</summary>
    public void SeedInitial()
    {
        _list.Clear();
        foreach (var v in InitialValues)
            _list.Add(v);
    }

    /// <summary>Server-only: deterministic sequence touching every SyncList operation.</summary>
    public void RunServerOps()
    {
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

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;

        // Seed on the server side so the contents are delivered to observers as initial state.
        if (asServer)
            SeedInitial();
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalInitialMatched(RPCInfo info = default) => InitialMatchCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalConverged(RPCInfo info = default) => ConvergedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
