using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncBigData"/>. The server sets a byte payload and every observer
/// must receive the identical (LZ4 round-tripped) bytes via the chunked transfer.
/// </summary>
public class SyncBigDataServerAuthIdentity : NetworkIdentity
{
    public const int PayloadLength = 4096;

    // High throughput so the throttled transfer completes quickly under test.
    [SerializeField] private SyncBigData _data = new(ownerAuth: false, maxKBPerSec: 4000);

    public static SyncBigDataServerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ReceivedCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ReceivedCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public static byte[] BuildPayload()
    {
        var b = new byte[PayloadLength];
        for (int i = 0; i < PayloadLength; i++)
            b[i] = (byte)((i * 31 + 7) % 251);
        return b;
    }

    public void Send() => _data.SetData(BuildPayload());

    public bool Received()
    {
        var d = _data.data;
        if (d.Count != PayloadLength) return false;
        for (int i = 0; i < PayloadLength; i++)
            if (d.Array[d.Offset + i] != (byte)((i * 31 + 7) % 251)) return false;
        return true;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalReceived(RPCInfo info = default) => ReceivedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
