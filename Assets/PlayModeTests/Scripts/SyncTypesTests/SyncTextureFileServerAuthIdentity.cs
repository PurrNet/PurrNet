using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncTextureFile"/>. The server encodes a runtime-built texture to
/// PNG and sets it; every observer decodes the bytes back into a Texture2D of matching dimensions.
/// </summary>
public class SyncTextureFileServerAuthIdentity : NetworkIdentity
{
    public const int TexW = 16;
    public const int TexH = 16;

    [SerializeField] private SyncTextureFile _tex = new(ownerAuth: false, maxKBPerSec: 4000);

    public static SyncTextureFileServerAuthIdentity LocalInstance;
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

    public void Send()
    {
        var t = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
        var px = new Color32[TexW * TexH];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32((byte)(i % 256), (byte)((i * 3) % 256), (byte)((i * 7) % 256), 255);
        t.SetPixels32(px);
        t.Apply();
        var png = t.EncodeToPNG();
        Destroy(t);
        _tex.SetData(png);
    }

    public bool Received()
    {
        var c = _tex.content;
        return c != null && c.width == TexW && c.height == TexH;
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
