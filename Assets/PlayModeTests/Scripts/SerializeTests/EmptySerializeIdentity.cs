using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class EmptySerializeIdentity : NetworkIdentity
{
    public static EmptySerializeIdentity LocalInstance;
    public static int SerializeCount;
    public static int DeserializeCount;
    public static int ServerOkCount;

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
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalOk(RPCInfo info = default) => ServerOkCount++;
}
