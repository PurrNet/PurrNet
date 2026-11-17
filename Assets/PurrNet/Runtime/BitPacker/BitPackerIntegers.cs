using System;
using PurrNet.Modules;
using UnityEngine;

namespace PurrNet.Packing
{
    public partial class BitPacker
    {
        public static byte GetBitCount(long minValue, long maxValue)
        {
            // Calculate the range and bits needed
            var range = (ulong)(maxValue - minValue);
            return (byte)Math.Ceiling(Math.Log(range + 1, 2));
        }

        public static byte GetBitCount(ulong minValue, ulong maxValue)
        {
            // Calculate the range and bits needed
            var range = maxValue - minValue;
            return (byte)Math.Ceiling(Math.Log(range + 1, 2));
        }

        [UsedByIL]
        public void WriteInteger(long data, long minValue, long maxValue, byte bitCount)
        {
            Debug.Assert(data >= minValue, $"Data is less than min value; {data} < {minValue}");
            Debug.Assert(data <= maxValue, $"Data is greater than max value; {data} > {maxValue}");
            WriteBits((ulong)(data - minValue), bitCount);
        }

        [UsedByIL]
        public void WriteInteger(long data, long minValue, long maxValue)
        {
            WriteInteger(data, minValue, maxValue, GetBitCount(minValue, maxValue));
        }

        [UsedByIL]
        public void WriteInteger(long data, byte bitCount)
        {
            WriteBits((ulong)data, bitCount);
        }

        [UsedByIL]
        public void ReadInteger(ref long data, long minValue, long maxValue, byte bitCount)
        {
            data = (long)ReadBits(bitCount) + minValue;
        }

        [UsedByIL]
        public void ReadInteger(ref long data, byte bitCount)
        {
            data = (long)ReadBits(bitCount);
        }

        [UsedByIL]
        public void WriteInteger(ulong data, ulong minValue, ulong maxValue, byte bitCount)
        {
            Debug.Assert(data >= minValue, $"Data is less than min value; {data} < {minValue}");
            Debug.Assert(data <= maxValue, $"Data is greater than max value; {data} > {maxValue}");
            WriteBits(data - minValue, bitCount);
        }

        /*[UsedByIL]
        public void WriteInteger(ulong data, ulong minValue, ulong maxValue)
        {
            WriteInteger(data, minValue, maxValue, GetBitCount(minValue, maxValue));
        }*/

        [UsedByIL]
        public void WriteInteger(ulong data, byte bitCount)
        {
            WriteBits(data, bitCount);
        }

        [UsedByIL]
        public void ReadInteger(ref ulong data, ulong minValue, ulong maxValue, byte bitCount)
        {
            data = ReadBits(bitCount) + minValue;
        }

        [UsedByIL]
        public void ReadInteger(ref ulong data, byte bitCount)
        {
            data = ReadBits(bitCount);
        }
    }
}