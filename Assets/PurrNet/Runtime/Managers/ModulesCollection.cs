using System.Collections.Generic;
using PurrNet.Modules;
using PurrNet.Transports;

namespace PurrNet
{
    public readonly struct ModulesCollection
    {
        private readonly List<INetworkModule> _modules;
        private readonly List<IConnectionListener> _connectionListeners;
        private readonly List<IConnectionStateListener> _connectionStateListeners;
        private readonly List<IDataListener> _dataListeners;
        private readonly List<IFixedUpdate> _fixedUpdatesListeners;
        private readonly List<IPreFixedUpdate> _preFixedUpdatesListeners;
        private readonly List<IPostFixedUpdate> _posteFixedUpdatesListeners;
        private readonly List<IBatch> _batchListeners;
        private readonly List<IPostBatch> _postBatchListeners;
        private readonly List<IDrawGizmos> _drawGizmosListeners;
        private readonly List<IUpdate> _updateListeners;
        private readonly List<ICleanup> _cleanupListeners;
        private readonly List<IFlushBatchedRPCs> _preBroadcastSentListeners;
        private readonly List<IPromoteToServerModule> _IPromoteToServerModule;
        private readonly List<ITransferToNewServer> _ITransferToNewServer;
        private readonly List<IPostTransferToNewServer> _IPostTransferToNewServer;

        private readonly IRegisterModules _manager;
        private readonly bool _asServer;

        public ModulesCollection(IRegisterModules manager, bool asServer)
        {
            _modules = new List<INetworkModule>();
            _connectionListeners = new List<IConnectionListener>();
            _connectionStateListeners = new List<IConnectionStateListener>();
            _preFixedUpdatesListeners = new List<IPreFixedUpdate>();
            _posteFixedUpdatesListeners = new List<IPostFixedUpdate>();
            _dataListeners = new List<IDataListener>();
            _updateListeners = new List<IUpdate>();
            _fixedUpdatesListeners = new List<IFixedUpdate>();
            _cleanupListeners = new List<ICleanup>();
            _drawGizmosListeners = new List<IDrawGizmos>();
            _batchListeners = new List<IBatch>();
            _postBatchListeners = new List<IPostBatch>();
            _preBroadcastSentListeners = new List<IFlushBatchedRPCs>();
            _IPromoteToServerModule = new List<IPromoteToServerModule>();
            _ITransferToNewServer = new List<ITransferToNewServer>();
            _IPostTransferToNewServer = new List<IPostTransferToNewServer>();
            _manager = manager;
            _asServer = asServer;
        }

        public bool TryGetModule<T>(out T module) where T : INetworkModule
        {
            if (_modules == null)
            {
                module = default;
                return false;
            }

            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] is T mod)
                {
                    module = mod;
                    return true;
                }
            }

            module = default;
            return false;
        }

        public void RegisterModules()
        {
            bool isClientTransfering = !_asServer && _manager.isTranferingToNewServer;

            if (!isClientTransfering)
                UnregisterModules();

            _manager.RegisterModules(this, _asServer);

            if (_manager.isPromotingToServer)
                return;

            if (_manager.isTranferingToNewServer)
                return;

            for (int i = 0; i < _modules.Count; i++)
            {
                _modules[i].Enable(_asServer);

                if (_modules[i] is IConnectionListener connectionListener)
                    _connectionListeners.Add(connectionListener);

                if (_modules[i] is IConnectionStateListener connectionStateListener)
                    _connectionStateListeners.Add(connectionStateListener);

                if (_modules[i] is IDataListener dataListener)
                    _dataListeners.Add(dataListener);

                if (_modules[i] is IFixedUpdate fixedUpdate)
                    _fixedUpdatesListeners.Add(fixedUpdate);

                if (_modules[i] is IUpdate update)
                    _updateListeners.Add(update);

                if (_modules[i] is ICleanup cleanup)
                    _cleanupListeners.Add(cleanup);

                if (_modules[i] is IPreFixedUpdate preFixedUpdate)
                    _preFixedUpdatesListeners.Add(preFixedUpdate);

                if (_modules[i] is IPostFixedUpdate postFixedUpdate)
                    _posteFixedUpdatesListeners.Add(postFixedUpdate);

                if (_modules[i] is IDrawGizmos drawGizmos)
                    _drawGizmosListeners.Add(drawGizmos);

                if (_modules[i] is IBatch batch)
                    _batchListeners.Add(batch);

                if (_modules[i] is IPostBatch postBatch)
                    _postBatchListeners.Add(postBatch);

                if (_modules[i] is IFlushBatchedRPCs preBroadcastSent)
                    _preBroadcastSentListeners.Add(preBroadcastSent);

                if (_modules[i] is IPromoteToServerModule promoteToServerModule)
                    _IPromoteToServerModule.Add(promoteToServerModule);

                if (_modules[i] is ITransferToNewServer TransferToNewServer)
                    _ITransferToNewServer.Add(TransferToNewServer);

                if (_modules[i] is IPostTransferToNewServer PostTransferToNewServer)
                    _IPostTransferToNewServer.Add(PostTransferToNewServer);
            }
        }

        public void OnNewConnection(Connection conn, bool asServer)
        {
            for (int i = 0; i < _connectionListeners.Count; i++)
                _connectionListeners[i].OnConnected(conn, asServer);
        }

        public void OnConnectionState(ConnectionState state, bool asServer)
        {
            for (int i = 0; i < _connectionStateListeners.Count; i++)
                _connectionStateListeners[i].OnConnectionState(state, asServer);
        }

        public void OnLostConnection(Connection conn, bool asServer)
        {
            for (int i = 0; i < _connectionListeners.Count; i++)
                _connectionListeners[i].OnDisconnected(conn, asServer);
        }

        public void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            for (int i = 0; i < _dataListeners.Count; i++)
                _dataListeners[i].OnDataReceived(conn, data, asServer);
        }

        public void TriggerOnUpdate()
        {
            for (int i = 0; i < _updateListeners.Count; i++)
                _updateListeners[i].Update();
        }

        public void TriggerOnFixedUpdate()
        {
            for (int i = 0; i < _fixedUpdatesListeners.Count; i++)
                _fixedUpdatesListeners[i].FixedUpdate();
        }

        public void TriggerOnPreFixedUpdate()
        {
            for (int i = 0; i < _preFixedUpdatesListeners.Count; i++)
                _preFixedUpdatesListeners[i].PreFixedUpdate();
        }

        public void TriggerOnPostFixedUpdate()
        {
            for (int i = 0; i < _posteFixedUpdatesListeners.Count; i++)
                _posteFixedUpdatesListeners[i].PostFixedUpdate();
        }

        public void TriggerOnBatch()
        {
            for (int i = 0; i < _batchListeners.Count; i++)
                _batchListeners[i].BatchNetworkMessages();
        }

        public void TriggerOnPostBatch()
        {
            for (int i = 0; i < _postBatchListeners.Count; i++)
                _postBatchListeners[i].PostBatchNetworkMessages();
        }

        public void TriggerOnDrawGizmos()
        {
            for (int i = 0; i < _drawGizmosListeners.Count; i++)
                _drawGizmosListeners[i].DrawGizmos();
        }

        public void FlushBatchRPCs()
        {
            for (int i = 0; i < _preBroadcastSentListeners.Count; i++)
                _preBroadcastSentListeners[i].FlushBatchedRPCs();
        }

        public void PromoteToServer()
        {
            for (int i = 0; i < _IPromoteToServerModule.Count; i++)
                _IPromoteToServerModule[i].PromoteToServerModule();
        }

        public void PostPromoteToServer()
        {
            for (int i = 0; i < _IPromoteToServerModule.Count; i++)
                _IPromoteToServerModule[i].PostPromoteToServerModule();
        }

        public void TransferToNewServer()
        {
            for (int i = 0; i < _ITransferToNewServer.Count; i++)
                _ITransferToNewServer[i].TransferToNewServer();
        }

        public void PostTransferToNewServer()
        {
            for (int i = 0; i < _IPostTransferToNewServer.Count; i++)
                _IPostTransferToNewServer[i].PostTransferToNewServer();
        }

        public bool Cleanup()
        {
            bool allTrue = true;

            for (int i = 0; i < _cleanupListeners.Count; i++)
            {
                if (!_cleanupListeners[i].Cleanup())
                {
                    allTrue = false;
                }
            }

            return allTrue;
        }

        public void UnregisterModules()
        {
            for (int i = 0; i < _modules.Count; i++)
                _modules[i].Disable(_asServer);

            Clear();
        }

        private void Clear()
        {
            _modules.Clear();
            _connectionListeners.Clear();
            _connectionStateListeners.Clear();
            _dataListeners.Clear();
            _updateListeners.Clear();
            _fixedUpdatesListeners.Clear();
            _cleanupListeners.Clear();
            _preFixedUpdatesListeners.Clear();
            _posteFixedUpdatesListeners.Clear();
            _drawGizmosListeners.Clear();
            _batchListeners.Clear();
            _postBatchListeners.Clear();
            _preBroadcastSentListeners.Clear();
            _IPromoteToServerModule.Clear();
            _ITransferToNewServer.Clear();
            _IPostTransferToNewServer.Clear();
        }

        public void AddModule(INetworkModule module)
        {
            _modules.Add(module);
        }

        public void MigrateFrom(ModulesCollection other)
        {
            _modules.AddRange(other._modules);
            _connectionListeners.AddRange(other._connectionListeners);
            _connectionStateListeners.AddRange(other._connectionStateListeners);
            _dataListeners.AddRange(other._dataListeners);
            _updateListeners.AddRange(other._updateListeners);
            _fixedUpdatesListeners.AddRange(other._fixedUpdatesListeners);
            _cleanupListeners.AddRange(other._cleanupListeners);
            _preFixedUpdatesListeners.AddRange(other._preFixedUpdatesListeners);
            _posteFixedUpdatesListeners.AddRange(other._posteFixedUpdatesListeners);
            _drawGizmosListeners.AddRange(other._drawGizmosListeners);
            _batchListeners.AddRange(other._batchListeners);
            _postBatchListeners.AddRange(other._postBatchListeners);
            _preBroadcastSentListeners.AddRange(other._preBroadcastSentListeners);
            _IPromoteToServerModule.AddRange(other._IPromoteToServerModule);
            _ITransferToNewServer.AddRange(other._ITransferToNewServer);
            _IPostTransferToNewServer.AddRange(other._IPostTransferToNewServer);
            other.Clear();

            PromoteToServer();
        }
    }
}
