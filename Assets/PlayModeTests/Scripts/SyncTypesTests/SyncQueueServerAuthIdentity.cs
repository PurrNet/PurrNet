using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncQueue{T}"/>. Exercises initial state, Enqueue, Dequeue
/// (FIFO ordering), Clear and refill, asserting ordered convergence on every observer.
/// </summary>
public class SyncQueueServerAuthIdentity : NetworkIdentity
{
    public static readonly int[] InitialValues = { 10, 20, 30 };
    public static readonly int[] ExpectedFinal = { 99, 98 };

    [SerializeField] private SyncQueue<int> _queue = new(ownerAuth: false);

    public static SyncQueueServerAuthIdentity LocalInstance;
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

    public bool MatchesInitial() => SequenceEquals(InitialValues);
    public bool MatchesFinal() => SequenceEquals(ExpectedFinal);

    public string Describe()
    {
        var items = new List<int>();
        foreach (var v in _queue) items.Add(v);
        return $"[{string.Join(",", items)}]";
    }

    public void SeedInitial()
    {
        _queue.Clear();
        foreach (var v in InitialValues)
            _queue.Enqueue(v);
    }

    public void RunServerOps()
    {
        _queue.Enqueue(40); // Enqueue   -> 10,20,30,40
        _queue.Dequeue();   // Dequeue   -> 20,30,40
        _queue.Dequeue();   // Dequeue   -> 30,40
        _queue.Enqueue(50); // Enqueue   -> 30,40,50
        _queue.Clear();     // Clear     -> (empty)
        _queue.Enqueue(99); // refill    -> 99
        _queue.Enqueue(98); //           -> 99,98
    }

    private bool SequenceEquals(int[] expected)
    {
        if (_queue.Count != expected.Length) return false;
        int i = 0;
        foreach (var v in _queue)
        {
            if (v != expected[i]) return false;
            i++;
        }
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
