using PurrNet;
using PurrNet.Packing;

public class MultiSerializeA : NetworkIdentity
{
    public const int Sentinel = 41377;
    public const string StringSentinel = "identity-A";

    public static MultiSerializeA LocalInstance;
    public static int DeserializeCount;
    public static int ServerOkCount;
    public static bool EarlySpawnBeforeBDeserialize;

    public int readValue;
    public string readString;

    public static void ResetAll()
    {
        LocalInstance = null;
        DeserializeCount = 0;
        ServerOkCount = 0;
        EarlySpawnBeforeBDeserialize = false;
    }

    protected override void OnEarlySpawn()
    {
        if (DeserializeCount > 0 && MultiSerializeB.DeserializeCount == 0)
            EarlySpawnBeforeBDeserialize = true;

        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnSerialize(BitPacker packer)
    {
        Packer<int>.Write(packer, Sentinel);
        Packer<string>.Write(packer, StringSentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readValue);
        Packer<string>.Read(packer, ref readString);
    }

    public bool ReadValuesMatch => readValue == Sentinel && readString == StringSentinel;

    [ServerRpc(requireOwnership: false)]
    public void SignalDeserializedOk(RPCInfo info = default) => ServerOkCount++;
}
