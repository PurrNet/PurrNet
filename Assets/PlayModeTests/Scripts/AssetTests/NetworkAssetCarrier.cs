using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using Object = UnityEngine.Object;

public class NetworkAssetTestSO : ScriptableObject
{
    public int value;
}

public class NetworkAssetCarrier : NetworkIdentity
{
    public static NetworkAssetCarrier LocalInstance;
    public static int ReceivedCount;
    public static int ServerOkCount;
    public static int DeserializeCount;

    public static NetworkAssetTestSO SerializeAsset;

    public NetworkAssetTestSO recvSo;
    public AudioClip recvClip;
    public Texture2D recvTex;
    public Object recvMaybeNull;
    public bool nullArrivedNull;

    public NetworkAssetTestSO recvSerializedSo;

    public static void ResetAll()
    {
        LocalInstance = null;
        ReceivedCount = 0;
        ServerOkCount = 0;
        DeserializeCount = 0;
        SerializeAsset = null;
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
        Packer<NetworkAssetTestSO>.Write(packer, SerializeAsset);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<NetworkAssetTestSO>.Read(packer, ref recvSerializedSo);
    }

    [ObserversRpc(bufferLast: true)]
    public void SendAssets(NetworkAssetTestSO so, AudioClip clip, Texture2D tex, Object maybeNull)
    {
        recvSo = so;
        recvClip = clip;
        recvTex = tex;
        recvMaybeNull = maybeNull;
        nullArrivedNull = maybeNull == null;
        ReceivedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReceivedOk(RPCInfo info = default) => ServerOkCount++;
}
