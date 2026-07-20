using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Utils;

namespace PurrNet
{
    internal static class PreciseArrayPool<T>
    {
        static readonly List<T[]> _queue = new List<T[]>();

        public static T[] Rent(int size)
        {
            for (var i = 0; i < _queue.Count; ++i)
            {
                if (_queue[i].Length == size)
                {
                    var array = _queue[i];
                    Array.Clear(array, 0, size);
                    _queue.RemoveAt(i);
                    return array;
                }
            }

            return new T[size];
        }

        public static void Return(T[] array)
        {
            _queue.Add(array);
        }
    }

    public struct GenericRPCHeader
    {
        public BitPacker stream;
        public Type[] types;
        public object[] values;
        public RPCInfo info;

        [UsedByIL]
        public static GenericRPCHeader CreateGenericHeader(BitPacker stream, RPCInfo info, int genericCount, int paramCount)
        {
            var types = PreciseArrayPool<Type>.Rent(genericCount);
            var values = PreciseArrayPool<object>.Rent(paramCount);

            return new GenericRPCHeader
            {
                stream = stream,
                types = types,
                values = values,
                info = info
            };
        }

        [UsedByIL]
        public void SaveReadHash(uint hash, int index)
        {
            types[index] = Hasher.ResolveType(hash);
        }

        [UsedImplicitly]
        public void SetPlayerId(PlayerID player, int index)
        {
            values[index] = player;
        }

        [UsedImplicitly]
        public void SetInfo(int index)
        {
            values[index] = info;
        }

        [UsedImplicitly]
        public Type GetTypeAt(int index)
        {
            return types[index];
        }

        [UsedByIL]
        public void SaveReadValue<T>(T value, int index)
        {
            values[index] = value;
        }

        [UsedByIL]
        public void SaveReadValue(object value, int index)
        {
            values[index] = value;
        }

       [UsedImplicitly]
        public void Read(int genericIndex, int index)
        {
            var type = types[genericIndex];
            Packer.Read(stream, type, ref values[index]); // delta serializion breaks here
        }

        // Used for parameters that are closed on the method's open generic parameters at compile
        // time (e.g. `GenericPair<T>` where `T` is the RPC's method-generic). The codegen builds
        // the closed Type at runtime from `types[]` and routes the read through the runtime-typed
        // Packer.Read instead of the strongly-typed Packer<T>.
        [UsedByIL]
        public void ReadType(Type type, int index)
        {
            Packer.Read(stream, type, ref values[index]);
        }

        [UsedImplicitly]
        public void Read<T>(int index)
        {
            T value = default;
            Packer<T>.Read(stream, ref value);
            AsyncPackableHelper.PrepareAfterUnpack(ref value);
            values[index] = value;
        }
    }
}
