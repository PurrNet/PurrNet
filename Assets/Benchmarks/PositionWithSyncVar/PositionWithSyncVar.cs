using PurrNet;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

public class PositionWithSyncVar : NetworkIdentity
{
    [SerializeField] private SyncVar<Vector3> _position = new (ownerAuth: true);

    float timer = 3;
    Vector3 moveDir = Vector3.zero;

    [TargetRpc(channel: Channel.Unreliable, runLocally: true)]
    void Test(PlayerID player, PackedUInt integer, BitPacker v, BitPacker a)
    {
        using (v)
        {
            string result = Packer<string>.Read(v);
            string resultb = Packer<string>.Read(a);
            Debug.Log(integer);
            Debug.Log(result);
            Debug.Log(resultb);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            using var packer = BitPackerPool.Get();
            Packer<string>.Write(packer, "YO YO YO 69 70?");
            Test(new PlayerID(1, false), (uint)packer.positionInBits, packer, packer);
        }

        if (isOwner)
        {
            transform.position += moveDir * Time.deltaTime;
            timer += 1 * Time.deltaTime;
            if (timer > 3)
            {
                transform.position = Vector3.zero;
                moveDir = Vector3.left * Random.Range(-2, 2);
                moveDir += Vector3.forward * Random.Range(-2, 2);
                timer = 0;
            }

            _position.value = transform.position;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, _position, 8 * Time.deltaTime);
        }
    }
}
