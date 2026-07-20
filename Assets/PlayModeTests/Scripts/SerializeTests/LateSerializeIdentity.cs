using PurrNet;
using PurrNet.Packing;

public class LateSerializeIdentity : NetworkIdentity
{
    public const int IntSentinel = 9182;
    public const string StringSentinel = "late-join-serialize";

    public static LateSerializeIdentity LocalInstance;
    public static int DeserializeCount;
    public static int ServerOkCount;

    public static bool VictimIdReceived;
    public static ulong VictimPlayerId;
    public static bool DonePhaseSignal;

    public int readInt;
    public string readString;

    public static void ResetAll()
    {
        LocalInstance = null;
        DeserializeCount = 0;
        ServerOkCount = 0;
        VictimIdReceived = false;
        VictimPlayerId = 0;
        DonePhaseSignal = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnSerialize(BitPacker packer)
    {
        Packer<int>.Write(packer, IntSentinel);
        Packer<string>.Write(packer, StringSentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readInt);
        Packer<string>.Read(packer, ref readString);
    }

    public bool ReadValuesMatch => readInt == IntSentinel && readString == StringSentinel;

    [ObserversRpc(bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }

    [ObserversRpc(bufferLast: true)]
    public void BroadcastDone()
    {
        DonePhaseSignal = true;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalOk(RPCInfo info = default) => ServerOkCount++;
}
