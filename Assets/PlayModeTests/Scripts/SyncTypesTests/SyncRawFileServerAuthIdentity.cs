using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncRawFile"/>. The server sets raw bytes; every observer decodes
/// them into the typed <c>content</c> (byte[]) after the transfer completes.
/// </summary>
public class SyncRawFileServerAuthIdentity : NetworkIdentity
{
    public const int PayloadLength = 4096;

    [SerializeField] private SyncRawFile _file = new(ownerAuth: false, maxKBPerSec: 4000);

    public static SyncRawFileServerAuthIdentity LocalInstance;
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
            b[i] = (byte)((i * 17 + 3) % 251);
        return b;
    }

    public void Send() => _file.SetData(BuildPayload());

    public bool Received()
    {
        var c = _file.content;
        if (c == null || c.Length != PayloadLength) return false;
        for (int i = 0; i < PayloadLength; i++)
            if (c[i] != (byte)((i * 17 + 3) % 251)) return false;
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
