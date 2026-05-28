using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncVar{T}"/>. Exercises initial state plus a sequence of
/// post-spawn value changes (deltas), asserting every observer converges to the final value.
/// </summary>
public class SyncVarServerAuthIdentity : NetworkIdentity
{
    public const int InitialValue = 7;
    public const int FinalValue = 4242;

    [SerializeField] private SyncVar<int> _value = new(0, sendIntervalInSeconds: 0f, ownerAuth: false);

    public static SyncVarServerAuthIdentity LocalInstance;
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

    public bool MatchesInitial() => _value.value == InitialValue;
    public bool MatchesFinal() => _value.value == FinalValue;
    public string Describe() => _value.value.ToString();

    public void SeedInitial() => _value.value = InitialValue;

    public void RunServerOps()
    {
        _value.value = 100;        // intermediate delta
        _value.value = 200;        // intermediate delta
        _value.value = FinalValue; // final
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
