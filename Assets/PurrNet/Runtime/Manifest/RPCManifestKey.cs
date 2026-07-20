using System;

namespace PurrNet
{
    public readonly struct RPCManifestKey : IEquatable<RPCManifestKey>
    {
        private readonly Type type;
        private readonly int rpcId;

        public RPCManifestKey(Type type, int rpcId)
        {
            this.type = type;
            this.rpcId = rpcId;
        }

        public bool Equals(RPCManifestKey other) => type == other.type && rpcId == other.rpcId;
        public override bool Equals(object obj) => obj is RPCManifestKey k && Equals(k);
        public override int GetHashCode() => ((type?.GetHashCode() ?? 0) * 397) ^ rpcId;
    }
}
