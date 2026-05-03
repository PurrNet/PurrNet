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
}
