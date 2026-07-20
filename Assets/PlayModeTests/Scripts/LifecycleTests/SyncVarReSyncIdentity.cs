using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncVar{T}"/> that the owning client mutates every frame, to
/// mimic a continuously-changing synced stat (the scenario that surfaced the SetDirty re-arm
/// freeze). A non-zero send interval keeps the SyncVar almost always dirty between flushes, so an
/// ownership blip reliably lands while a change is pending.
/// </summary>
public class SyncVarReSyncIdentity : NetworkIdentity
{
    [SerializeField]
    private SyncVar<float> _value = new(0f, sendIntervalInSeconds: 0.1f, ownerAuth: true);

    public static SyncVarReSyncIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public float currentValue => _value.value;

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    private void Update()
    {
        // Only the owning client drives the value. The setter is gated on the controller check;
        // owner-auth means the owner is the controller, so this is the only peer that writes.
        if (isSpawned && isOwner)
            _value.value += 1f;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
