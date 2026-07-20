using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncVar{T}"/>. The owning client drives a sequence of value
/// changes; the server and all other observers converge, and only the owner is the controller.
/// </summary>
public class SyncVarOwnerAuthIdentity : NetworkIdentity
{
    public const int FinalValue = 4242;

    [SerializeField] private SyncVar<int> _value = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);

    public static SyncVarOwnerAuthIdentity LocalInstance;
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

    public bool MatchesFinal() => _value.value == FinalValue;
    public string Describe() => _value.value.ToString();

    public void RunOwnerSequence()
    {
        _value.value = 100;        // intermediate delta
        _value.value = 200;        // intermediate delta
        _value.value = FinalValue; // final
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
