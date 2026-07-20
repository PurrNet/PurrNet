using PurrNet;
using UnityEngine;

/// <summary>
/// <see cref="SyncInput{T}"/> syncs the owning client's value to the server only. Verifies the owner
/// can push a value the server receives, that non-owner clients do not receive it, and that only the
/// owner is the controller.
/// </summary>
public class SyncInputIdentity : NetworkIdentity
{
    public const int Sentinel = 4242;

    [SerializeField] private SyncInput<int> _input = new(defaultValue: 0);

    public static SyncInputIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool PhaseDoneReceived;

    public int Value => _input.value;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        PhaseDoneReceived = false;
    }

    public void PushInput(int v) => _input.value = v;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

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
