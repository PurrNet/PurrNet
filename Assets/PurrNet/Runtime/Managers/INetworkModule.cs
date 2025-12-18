using PurrNet.Transports;

namespace PurrNet.Modules
{
    public interface INetworkModule
    {
        void Enable(bool asServer);

        void Disable(bool asServer);
    }

    internal interface IConnectionListener
    {
        void OnConnected(Connection conn, bool asServer);

        void OnDisconnected(Connection conn, bool asServer);
    }

    internal interface IConnectionStateListener
    {
        void OnConnectionState(ConnectionState state, bool asServer);
    }

    internal interface IDataListener
    {
        void OnDataReceived(Connection conn, ByteData data, bool asServer);
    }

    internal interface ICleanup
    {
        /// <summary>
        /// Clean up the module; gets called every tick until it returns true
        /// </summary>
        /// <returns>True if the cleanup is finished, false otherwise</returns>
        bool Cleanup();
    }

    internal interface IPromoteToServerModule
    {
        void PromoteToServerModule();
        void PostPromoteToServerModule();
    }

    internal interface ITransferToNewServer
    {
        void TransferToNewServer();
    }

    internal interface IPostTransferToNewServer
    {
        void PostTransferToNewServer();
    }

    internal interface IPostFixedUpdate
    {
        void PostFixedUpdate();
    }

    internal interface IBatch
    {
        void BatchNetworkMessages();
    }

    internal interface IFlushBatchedRPCs
    {
        void FlushBatchedRPCs();
    }

    internal interface IPostBatch
    {
        void PostBatchNetworkMessages();
    }

    internal interface IDrawGizmos
    {
        void DrawGizmos();
    }

    internal interface IFixedUpdate
    {
        void FixedUpdate();
    }

    internal interface IPreFixedUpdate
    {
        void PreFixedUpdate();
    }

    internal interface IUpdate
    {
        void Update();
    }
}
