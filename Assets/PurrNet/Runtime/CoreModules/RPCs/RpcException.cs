using System;

namespace PurrNet
{
    public abstract class RpcException : Exception
    {
        protected RpcException(string message) : base(message) { }
        protected RpcException(string message, Exception inner) : base(message, inner) { }
    }

    public class RpcRejectedException : RpcException
    {
        public RpcError error { get; }

        public RpcRejectedException(RpcError error)
            : base($"RPC was rejected by the server: {error}")
        {
            this.error = error;
        }

        public RpcRejectedException(RpcError error, string message)
            : base(message)
        {
            this.error = error;
        }
    }

    /// <summary>
    /// Thrown on a pending async RPC when the player it was waiting on disconnects,
    /// so the awaiter fails fast instead of waiting out the full timeout.
    /// </summary>
    public class RpcTargetDisconnectedException : RpcException
    {
        public PlayerID target { get; }

        public RpcTargetDisconnectedException(PlayerID target)
            : base($"RPC target player '{target}' disconnected before responding.")
        {
            this.target = target;
        }

        public RpcTargetDisconnectedException(PlayerID target, string message)
            : base(message)
        {
            this.target = target;
        }
    }

    /// <summary>
    /// Wraps an exception thrown by an RPC handler so the manifest entry shows up in the message
    /// while the original stack trace is preserved as InnerException for clickable navigation.
    /// </summary>
    public class RpcDispatchException : RpcException
    {
        public Type declaringType { get; }
        public int rpcId { get; }
        public RPCManifestEntry? entry { get; }

        public RpcDispatchException(RPCManifestEntry entry, Exception inner)
            : base($"RPC `{entry}` threw", inner)
        {
            declaringType = entry.declaringType;
            rpcId = entry.rpcId;
            this.entry = entry;
        }

        public RpcDispatchException(Type declaringType, int rpcId, Exception inner)
            : base($"RPC <id={rpcId}> on '{declaringType?.FullName ?? "<unknown>"}' threw", inner)
        {
            this.declaringType = declaringType;
            this.rpcId = rpcId;
        }
    }
}
