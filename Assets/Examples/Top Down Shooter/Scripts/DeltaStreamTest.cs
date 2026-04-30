using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class DeltaStreamTest : NetworkBehaviour
{
    private ReliableDeltaStream<int> _stream;
    
    private void Awake()
    {
        _stream = new ReliableDeltaStream<int>(new TestRPCManifestKey());
    }

    private void Update()
    {
        if (!isServer)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log($"Steam0: {_stream[0]} | Stream1: {_stream[1]}");
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            _stream[0]++;
            _stream[1] += 2;
        }
    }
    
    public struct TestRPCManifestKey : IStableHashable
    {
        public uint GetStableHash() => 0x1337u;
    }
}
