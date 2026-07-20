using PurrNet;
using PurrNet.Packing;

public class MultiSerializeB : NetworkIdentity
{
    public const int Sentinel = 45746;
    public const bool BoolSentinel = true;

    public static MultiSerializeB LocalInstance;
    public static int DeserializeCount;

    public int readValue;
    public bool readBool;

    public static void ResetAll()
    {
        LocalInstance = null;
        DeserializeCount = 0;
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnSerialize(BitPacker packer)
    {
        Packer<int>.Write(packer, Sentinel);
        Packer<bool>.Write(packer, BoolSentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readValue);
        Packer<bool>.Read(packer, ref readBool);
    }

    public bool ReadValuesMatch => readValue == Sentinel && readBool == BoolSentinel;
}
