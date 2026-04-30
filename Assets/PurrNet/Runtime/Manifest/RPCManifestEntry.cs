using System;

namespace PurrNet
{
    public readonly struct RPCManifestEntry
    {
        public readonly Type declaringType;
        public readonly int rpcId;
        public readonly RPCSignature signature;

        public RPCManifestEntry(Type declaringType, int rpcId, RPCSignature signature)
        {
            this.declaringType = declaringType;
            this.rpcId = rpcId;
            this.signature = signature;
        }

        public override string ToString()
        {
            var typeName = declaringType?.FullName ?? "<unknown>";
            var name = string.IsNullOrEmpty(signature.rpcName) ? $"<rpcId={rpcId}>" : signature.rpcName;
            return signature.deltaPacked
                ? $"{typeName}.{name} (id={rpcId}, {signature.type}, {signature.channel}, delta)"
                : $"{typeName}.{name} (id={rpcId}, {signature.type}, {signature.channel})";
        }
    }
}
