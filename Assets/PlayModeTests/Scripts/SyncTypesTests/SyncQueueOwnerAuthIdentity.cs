using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncQueue{T}"/>. The owning client drives every operation; the
/// server and all other observers must converge (in FIFO order), and only the owner is the controller.
/// </summary>
public class SyncQueueOwnerAuthIdentity : NetworkIdentity
{
    public static readonly int[] ExpectedFinal = { 99, 98 };

    [SerializeField] private SyncQueue<int> _queue = new(ownerAuth: true);

    public static SyncQueueOwnerAuthIdentity LocalInstance;
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
        if (_queue.Count != ExpectedFinal.Length) return false;
        int i = 0;
        foreach (var v in _queue)
        {
            if (v != ExpectedFinal[i]) return false;
            i++;
        }
        return true;
    }

    public string Describe()
    {
        var items = new List<int>();
        foreach (var v in _queue) items.Add(v);
        return $"[{string.Join(",", items)}]";
    }

    public void RunOwnerSequence()
    {
        _queue.Clear();
        _queue.Enqueue(10); _queue.Enqueue(20); _queue.Enqueue(30);
        _queue.Enqueue(40); // Enqueue   -> 10,20,30,40
        _queue.Dequeue();   // Dequeue   -> 20,30,40
        _queue.Dequeue();   // Dequeue   -> 30,40
        _queue.Enqueue(50); // Enqueue   -> 30,40,50
        _queue.Clear();     // Clear     -> (empty)
        _queue.Enqueue(99); // refill    -> 99
        _queue.Enqueue(98); //           -> 99,98
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
