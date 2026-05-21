using PurrNet;
using PurrNet.Packing;

public class NestedSerializeChild : NetworkIdentity
{
    public const int Sentinel = 22222;
    public const float FloatSentinel = 9.5f;

    public static NestedSerializeChild LocalInstance;
    public static int DeserializeCount;

    public int readValue;
    public float readFloat;

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
        Packer<float>.Write(packer, FloatSentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readValue);
        Packer<float>.Read(packer, ref readFloat);
    }

    public bool ReadValuesMatch => readValue == Sentinel && UnityEngine.Mathf.Approximately(readFloat, FloatSentinel);
}
