using PurrNet;
using PurrNet.Packing;

public class ModuleSerializeModule : NetworkModule
{
    public const int Sentinel = 20817;
    public const string StringSentinel = "module-data";

    public static int DeserializeCount;

    public int readValue;
    public string readString;

    public static void ResetAll()
    {
        DeserializeCount = 0;
    }

    public override void OnSerialize(BitPacker packer)
    {
        Packer<int>.Write(packer, Sentinel);
        Packer<string>.Write(packer, StringSentinel);
    }

    public override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readValue);
        Packer<string>.Read(packer, ref readString);
    }

    public bool ReadValuesMatch => readValue == Sentinel && readString == StringSentinel;
}

public class ModuleSerializeIdentity : NetworkIdentity
{
    public const int Sentinel = 25186;

    public readonly ModuleSerializeModule module = new();

    public static ModuleSerializeIdentity LocalInstance;
    public static int IdentityDeserializeCount;
    public static int ServerOkCount;

    public int readValue;

    public static void ResetAll()
    {
        LocalInstance = null;
        IdentityDeserializeCount = 0;
        ServerOkCount = 0;
        ModuleSerializeModule.ResetAll();
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
        Packer<int>.Write(packer, Sentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        IdentityDeserializeCount++;
        Packer<int>.Read(packer, ref readValue);
    }

    public bool ReadValuesMatch => readValue == Sentinel && module.ReadValuesMatch;

    [ServerRpc(requireOwnership: false)]
    public void SignalDeserializedOk(RPCInfo info = default) => ServerOkCount++;
}
