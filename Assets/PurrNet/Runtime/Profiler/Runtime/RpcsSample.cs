using System;
using PurrNet.Packing;

namespace PurrNet.Profiler
{
    public readonly struct RpcsSample : IDisposable, IEquatable<RpcsSample>
    {
        public readonly Type type;
        public readonly string method;
        public readonly RPCType rpcType;
        public readonly BitPacker data;
        public readonly UnityEngine.Object context;

        public RpcsSample(Type type, RPCType rpcType, string method, BitData data, UnityEngine.Object context)
        {
            this.type = type;
            this.method = method;
            this.rpcType = rpcType;
            this.context = context;
            this.data = BitPackerPool.Get();
            this.data.WriteBitDataWithoutConsumingIt(data);
        }

        public void Dispose()
        {
            data?.Dispose();
        }

        public bool Equals(RpcsSample other)
        {
            return type == other.type && method == other.method && Equals(data, other.data) && Equals(context, other.context);
        }

        public override bool Equals(object obj)
        {
            return obj is RpcsSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(type, method, data, context);
        }
    }
}
