using System;
using System.Collections.Generic;
using PurrNet.Packing;
using UnityEngine;

/// <summary>
/// Concrete Packer-equivalent baselines for the A/B benchmark. Keeping these methods concrete
/// avoids the PurrNet IL post-processor treating an open generic benchmark adapter as a serializer.
/// </summary>
internal sealed class DeltaBenchmarkFullCodecs
{
    private DeltaBenchmarkFullCodecs() { }

    internal static bool WriteInt(BitPacker packer, int oldValue, int newValue)
    {
        packer.WriteBits(unchecked((uint)newValue), 32);
        return true;
    }

    internal static void ReadInt(BitPacker packer, int oldValue, ref int value)
    {
        value = unchecked((int)(uint)packer.ReadBits(32));
    }

    internal static bool WriteLong(BitPacker packer, long oldValue, long newValue)
    {
        packer.WriteBits(unchecked((ulong)newValue), 64);
        return true;
    }

    internal static void ReadLong(BitPacker packer, long oldValue, ref long value)
    {
        value = unchecked((long)packer.ReadBits(64));
    }

    internal static bool WriteFloat(BitPacker packer, float oldValue, float newValue)
    {
        WriteFloatValue(packer, newValue);
        return true;
    }

    internal static void ReadFloat(BitPacker packer, float oldValue, ref float value)
    {
        value = ReadFloatValue(packer);
    }

    internal static bool WriteDouble(BitPacker packer, double oldValue, double newValue)
    {
        packer.WriteBits(unchecked((ulong)BitConverter.DoubleToInt64Bits(newValue)), 64);
        return true;
    }

    internal static void ReadDouble(BitPacker packer, double oldValue, ref double value)
    {
        value = BitConverter.Int64BitsToDouble(unchecked((long)packer.ReadBits(64)));
    }

    internal static bool WriteVector3(BitPacker packer, Vector3 oldValue, Vector3 newValue)
    {
        WriteFloatValue(packer, newValue.x);
        WriteFloatValue(packer, newValue.y);
        WriteFloatValue(packer, newValue.z);
        return true;
    }

    internal static void ReadVector3(BitPacker packer, Vector3 oldValue, ref Vector3 value)
    {
        value.x = ReadFloatValue(packer);
        value.y = ReadFloatValue(packer);
        value.z = ReadFloatValue(packer);
    }

    internal static bool WriteQuaternion(BitPacker packer, Quaternion oldValue, Quaternion newValue)
    {
        WriteFloatValue(packer, newValue.x);
        WriteFloatValue(packer, newValue.y);
        WriteFloatValue(packer, newValue.z);
        WriteFloatValue(packer, newValue.w);
        return true;
    }

    internal static void ReadQuaternion(BitPacker packer, Quaternion oldValue, ref Quaternion value)
    {
        value.x = ReadFloatValue(packer);
        value.y = ReadFloatValue(packer);
        value.z = ReadFloatValue(packer);
        value.w = ReadFloatValue(packer);
    }

    internal static bool WriteList(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        // Public Packer<List<int>> declared-type discriminator.
        packer.WriteBit(true);
        if (newValue == null)
        {
            packer.WriteBit(false);
            return true;
        }

        packer.WriteBit(true);
        packer.WriteBits((uint)newValue.Count, 31);
        for (int i = 0; i < newValue.Count; i++)
            packer.WriteBits(unchecked((uint)newValue[i]), 32);
        return true;
    }

    internal static void ReadList(BitPacker packer, List<int> oldValue, ref List<int> value)
    {
        if (!packer.ReadBit())
            throw new InvalidOperationException("The full List<int> benchmark received a runtime-type payload.");
        if (!packer.ReadBit())
        {
            value = null;
            return;
        }

        int count = checked((int)packer.ReadBits(31));
        var result = new List<int>(count);
        for (int i = 0; i < count; i++)
            result.Add(unchecked((int)(uint)packer.ReadBits(32)));
        value = result;
    }

    internal static bool WriteDictionary(BitPacker packer, Dictionary<int, int> oldValue,
        Dictionary<int, int> newValue)
    {
        // Public Packer<Dictionary<int,int>> declared-type discriminator.
        packer.WriteBit(true);
        if (newValue == null)
        {
            packer.WriteBit(false);
            return true;
        }

        packer.WriteBit(true);
        packer.WriteBits((uint)newValue.Count, 31);
        foreach (var pair in newValue)
        {
            packer.WriteBits(unchecked((uint)pair.Key), 32);
            packer.WriteBits(unchecked((uint)pair.Value), 32);
        }
        return true;
    }

    internal static void ReadDictionary(BitPacker packer, Dictionary<int, int> oldValue,
        ref Dictionary<int, int> value)
    {
        if (!packer.ReadBit())
            throw new InvalidOperationException("The full Dictionary benchmark received a runtime-type payload.");
        if (!packer.ReadBit())
        {
            value = null;
            return;
        }

        int count = checked((int)packer.ReadBits(31));
        var result = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++)
        {
            int key = unchecked((int)(uint)packer.ReadBits(32));
            int item = unchecked((int)(uint)packer.ReadBits(32));
            result.Add(key, item);
        }
        value = result;
    }

    internal static bool WriteBytes(BitPacker packer, byte[] oldValue, byte[] newValue)
    {
        if (newValue == null)
        {
            packer.WriteBit(false);
            return true;
        }

        packer.WriteBit(true);
        packer.WriteBits((uint)newValue.Length, 31);
        packer.WriteBytes(newValue);
        return true;
    }

    internal static void ReadBytes(BitPacker packer, byte[] oldValue, ref byte[] value)
    {
        if (!packer.ReadBit())
        {
            value = null;
            return;
        }

        int count = checked((int)packer.ReadBits(31));
        var result = new byte[count];
        packer.ReadBytes(result);
        value = result;
    }

    private static void WriteFloatValue(BitPacker packer, float value)
    {
        packer.WriteBits(unchecked((uint)BitConverter.SingleToInt32Bits(value)), 32);
    }

    private static float ReadFloatValue(BitPacker packer)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)(uint)packer.ReadBits(32)));
    }
}
