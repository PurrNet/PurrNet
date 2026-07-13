using System.Collections.Generic;
using PurrNet.Packing;
using UnityEngine;

/// <summary>
/// Concrete adapters for the currently registered DeltaPacker delegates. Invoking the delegate
/// fields directly prevents Unity's serializer IL post-processor from rewriting benchmark call
/// sites, while retaining the exact production dispatch selected by DeltaPacker&lt;T&gt;.Write/Read.
/// </summary>
internal sealed class DeltaBenchmarkCurrentCodecs
{
    private DeltaBenchmarkCurrentCodecs() { }

    internal static bool WriteInt(BitPacker packer, int oldValue, int newValue) =>
        DeltaPacker<int>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadInt(BitPacker packer, int oldValue, ref int value) =>
        DeltaPacker<int>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteLong(BitPacker packer, long oldValue, long newValue) =>
        DeltaPacker<long>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadLong(BitPacker packer, long oldValue, ref long value) =>
        DeltaPacker<long>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteFloat(BitPacker packer, float oldValue, float newValue) =>
        DeltaPacker<float>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadFloat(BitPacker packer, float oldValue, ref float value) =>
        DeltaPacker<float>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteDouble(BitPacker packer, double oldValue, double newValue) =>
        DeltaPacker<double>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadDouble(BitPacker packer, double oldValue, ref double value) =>
        DeltaPacker<double>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteVector3(BitPacker packer, Vector3 oldValue, Vector3 newValue) =>
        DeltaPacker<Vector3>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadVector3(BitPacker packer, Vector3 oldValue, ref Vector3 value) =>
        DeltaPacker<Vector3>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteQuaternion(BitPacker packer, Quaternion oldValue, Quaternion newValue) =>
        DeltaPacker<Quaternion>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadQuaternion(BitPacker packer, Quaternion oldValue, ref Quaternion value) =>
        DeltaPacker<Quaternion>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteList(BitPacker packer, List<int> oldValue, List<int> newValue) =>
        DeltaPacker<List<int>>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadList(BitPacker packer, List<int> oldValue, ref List<int> value) =>
        DeltaPacker<List<int>>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteDictionary(BitPacker packer, Dictionary<int, int> oldValue,
        Dictionary<int, int> newValue) =>
        DeltaPacker<Dictionary<int, int>>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadDictionary(BitPacker packer, Dictionary<int, int> oldValue,
        ref Dictionary<int, int> value) =>
        DeltaPacker<Dictionary<int, int>>.ReadFunc(packer, oldValue, ref value);

    internal static bool WriteBytes(BitPacker packer, byte[] oldValue, byte[] newValue) =>
        DeltaPacker<byte[]>.WriteFunc(packer, oldValue, newValue);

    internal static void ReadBytes(BitPacker packer, byte[] oldValue, ref byte[] value) =>
        DeltaPacker<byte[]>.ReadFunc(packer, oldValue, ref value);
}
