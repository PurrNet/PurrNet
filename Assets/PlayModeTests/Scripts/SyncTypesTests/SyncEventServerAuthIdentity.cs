using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncEvent{T}"/>. The server invokes the event; every observer's
/// listener must fire with the same payload.
/// </summary>
public class SyncEventServerAuthIdentity : NetworkIdentity
{
    public const int Sentinel = 4242;

    [SerializeField] private SyncEvent<int> _event = new(ownerAuth: false);

    public static SyncEventServerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ReceivedCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public int ReceivedValue { get; private set; } = int.MinValue;
    public bool Received() => ReceivedValue == Sentinel;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ReceivedCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public void Fire() => _event.Invoke(Sentinel);

    private void OnEventFired(int value) => ReceivedValue = value;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        _event.AddListener(OnEventFired);
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalReceived(RPCInfo info = default) => ReceivedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
