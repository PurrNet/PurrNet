using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class SpawnSerializeIdentity : NetworkIdentity
{
    public const int IntSentinel = 1337;
    public const string StringSentinel = "purr-serialize";
    public const float FloatSentinel = 3.14159f;
    public static readonly Vector3 VectorSentinel = new(1f, 2f, 3f);

    public static SpawnSerializeIdentity LocalInstance;
    public static int SerializeCount;
    public static int DeserializeCount;
    public static int ServerOkCount;

    public int readInt;
    public string readString;
    public float readFloat;
    public Vector3 readVector;

    public static void ResetAll()
    {
        LocalInstance = null;
        SerializeCount = 0;
        DeserializeCount = 0;
        ServerOkCount = 0;
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
        SerializeCount++;
        Packer<int>.Write(packer, IntSentinel);
        Packer<string>.Write(packer, StringSentinel);
        Packer<float>.Write(packer, FloatSentinel);
        Packer<Vector3>.Write(packer, VectorSentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readInt);
        Packer<string>.Read(packer, ref readString);
        Packer<float>.Read(packer, ref readFloat);
        Packer<Vector3>.Read(packer, ref readVector);
    }

    public bool ReadValuesMatch =>
        readInt == IntSentinel &&
        readString == StringSentinel &&
        Mathf.Approximately(readFloat, FloatSentinel) &&
        readVector == VectorSentinel;

    [ServerRpc(requireOwnership: false)]
    public void SignalDeserializedOk(RPCInfo info = default) => ServerOkCount++;
}
