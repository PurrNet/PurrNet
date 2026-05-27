using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncArray{T}"/>. Exercises initial state, set-by-index, resize
/// (grow + shrink) and asserts every observer converges to the same contents.
/// </summary>
public class SyncArrayServerAuthIdentity : NetworkIdentity
{
    public static readonly int[] InitialValues = { 10, 20, 30 };
    public static readonly int[] ExpectedFinal = { 99, 21 };

    [SerializeField] private SyncArray<int> _array = new(length: 3, ownerAuth: false);

    public static SyncArrayServerAuthIdentity LocalInstance;
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
        var parts = new string[_array.Count];
        for (int i = 0; i < _array.Count; i++) parts[i] = _array[i].ToString();
        return $"[{string.Join(",", parts)}]";
    }

    public void SeedInitial()
    {
        if (_array.Length != InitialValues.Length)
            _array.Length = InitialValues.Length;
        for (int i = 0; i < InitialValues.Length; i++)
            _array[i] = InitialValues[i];
    }

    public void RunServerOps()
    {
        _array[1] = 21;     // Set              -> 10,21,30
        _array.Length = 5;  // Resize (grow)    -> 10,21,30,0,0
        _array[3] = 40;
        _array[4] = 50;     //                  -> 10,21,30,40,50
        _array.Length = 2;  // Resize (shrink)  -> 10,21
        _array[0] = 99;     // Set              -> 99,21
    }

    private bool SequenceEquals(int[] b)
    {
        if (_array.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
            if (_array[i] != b[i]) return false;
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
