using PurrNet.Packing;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    public delegate void BroadcastDelegate<in T>(Connection conn, T data, bool asServer);

    internal readonly struct BroadcastCallback<T> : IBroadcastCallback
    {
        readonly BroadcastDelegate<T> callback;

        public BroadcastCallback(BroadcastDelegate<T> callback)
        {
            this.callback = callback;
        }

        public bool IsSame(object callbackToCmp)
        {
            return callbackToCmp is BroadcastDelegate<T> action && action == callback;
        }

        public void TriggerCallback(Connection conn, BitPacker data, bool asServer)
        {
            var result = default(T);
            Packer<T>.Read(data, ref result);
            callback?.Invoke(conn, result, asServer);
        }

        public void Subscribe(BroadcastModule module)
        {
            module.Subscribe(callback);
        }
    }
}
