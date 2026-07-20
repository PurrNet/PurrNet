using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncHashSet{T}"/>. Exercises initial state, Add (incl. a
/// duplicate no-op), Remove, Clear and refill, asserting set convergence on every observer.
/// </summary>
public class SyncHashsetServerAuthIdentity : NetworkIdentity
{
    public static readonly int[] InitialValues = { 10, 20, 30 };
    public static readonly int[] ExpectedFinal = { 98, 99 };

    [SerializeField] private SyncHashSet<int> _set = new(ownerAuth: false);

    public static SyncHashsetServerAuthIdentity LocalInstance;
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

    public bool MatchesInitial() => SetEquals(InitialValues);
    public bool MatchesFinal() => SetEquals(ExpectedFinal);

    public string Describe()
    {
        var items = new List<int>();
        foreach (var v in _set) items.Add(v);
        items.Sort();
        return $"{{{string.Join(",", items)}}}";
    }

    public void SeedInitial()
    {
        _set.Clear();
        foreach (var v in InitialValues)
            _set.Add(v);
    }

    public void RunServerOps()
    {
        _set.Add(40);   // Add        -> 10,20,30,40
        _set.Add(40);   // duplicate  -> (no change)
        _set.Remove(20);// Remove     -> 10,30,40
        _set.Clear();   // Clear      -> (empty)
        _set.Add(99);   // refill     -> 99
        _set.Add(98);   //            -> 98,99
    }

    private bool SetEquals(int[] expected)
    {
        if (_set.Count != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
            if (!_set.Contains(expected[i])) return false;
        return true;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
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
