using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncArray{T}"/>. The owning client drives every operation; the
/// server and all other observers must converge, and only the owner is the controller.
/// </summary>
public class SyncArrayOwnerAuthIdentity : NetworkIdentity
{
    public static readonly int[] ExpectedFinal = { 99, 21 };

    [SerializeField] private SyncArray<int> _array = new(length: 0, ownerAuth: true);

    public static SyncArrayOwnerAuthIdentity LocalInstance;
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

    public bool MatchesFinal() => SequenceEquals(ExpectedFinal);

    public string Describe()
    {
        var parts = new string[_array.Count];
        for (int i = 0; i < _array.Count; i++) parts[i] = _array[i].ToString();
        return $"[{string.Join(",", parts)}]";
    }

    public void RunOwnerSequence()
    {
        _array.Length = 3;
        _array[0] = 10; _array[1] = 20; _array[2] = 30; // -> 10,20,30
        _array[1] = 21;     // Set             -> 10,21,30
        _array.Length = 5;  // Resize (grow)   -> 10,21,30,0,0
        _array[3] = 40; _array[4] = 50;        // -> 10,21,30,40,50
        _array.Length = 2;  // Resize (shrink) -> 10,21
        _array[0] = 99;     // Set             -> 99,21
    }

    private bool SequenceEquals(int[] b)
    {
        if (_array.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
            if (_array[i] != b[i]) return false;
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
