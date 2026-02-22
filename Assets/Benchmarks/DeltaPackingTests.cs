using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class DeltaPackingTests : NetworkIdentity
{
    [SerializeField] private Object _so;

    private Object _lastValue;

    [PurrButton]
    public void UpdateValue()
    {
        using var packer = new BitPacker();
        DeltaPacker<Object>.Write(packer, _lastValue, _so);
        _lastValue = _so;
        UpdateValue(packer);
    }

    [ObserversRpc(bufferLast: true, excludeSender: true)]
    public void UpdateValue(BitPacker packer)
    {
        using (packer)
        {
            Debug.Log($"UpdateValue");
            DeltaPacker<Object>.Read(packer, _lastValue, ref _so);
            _lastValue = _so;
        }
    }
}
