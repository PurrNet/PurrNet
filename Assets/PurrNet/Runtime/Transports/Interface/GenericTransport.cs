using UnityEngine;

namespace PurrNet.Transports
{
    [DefaultExecutionOrder(-100)]
    public abstract class GenericTransport : MonoBehaviour
    {
        /// <summary>
        /// Returns true if the transport is supported on the current platform.
        /// For example, WebGL does not support UDP or SteamTransport.
        /// This will return false if the transport is not supported.
        /// </summary>
        public abstract bool isSupported { get; }

        /// <summary>
        /// Access the underlying transport interface.
        /// This is used for low-level operations and should not be used directly.
        /// Unless you know what you are doing.
        /// </summary>
        public abstract ITransport transport { get; }

        bool TryGetNetworkManager(INetworkManager manager, out INetworkManager networkManager)
        {
            if (manager != null)
            {
                networkManager = manager;
                return true;
            }

            if (TryGetComponent<INetworkManager>(out networkManager))
                return true;

            var parentNm = GetComponentInParent<INetworkManager>();

            if (parentNm != null)
            {
                networkManager = parentNm;
                return true;
            }

            var childNm = GetComponentInChildren<INetworkManager>();

            if (childNm != null)
            {
                networkManager = childNm;
                return true;
            }

            if (NetworkManager.main)
            {
                networkManager = NetworkManager.main;
                return true;
            }

            networkManager = null;
            return false;
        }

        /// <summary>
        /// Starts the server.
        /// Optionally, you can pass a NetworkManager to register server modules.
        /// If you do not pass a NetworkManager, it will try to find one in the hierarchy.
        /// </summary>
        public void StartServer()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StartServer();
        }

        internal void StartServer(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
            {
                if (networkManager.serverState != ConnectionState.Disconnected)
                {
                    Debug.LogError($"[{GetType().Name}] Cannot start server since it is already running ({networkManager.serverState}).");
                    return;
                }
                networkManager.InternalRegisterServerModules();
            }

            StartServerInternal();
        }

        /// <summary>
        /// Stops the server.
        /// This will disconnect all clients.
        /// </summary>
        public void StopServer()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StopServer();
        }

        internal void StopServer(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
                networkManager.InternalUnregisterServerModules();

            StopServerInternal();
        }

        /// <summary>
        /// Starts the client.
        /// Optionally, you can pass a NetworkManager to register client modules.
        /// If you do not pass a NetworkManager, it will try to find one in the hierarchy.
        /// </summary>
        public void StartClient()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StartClient();
        }

        internal void StartClient(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
            {
                if (networkManager.clientState != ConnectionState.Disconnected)
                {
                    Debug.LogError($"[{GetType().Name}] Cannot start client since it is already running ({networkManager.clientState}).");
                    return;
                }
                networkManager.InternalRegisterClientModules();
            }

            StartClientInternal();
        }

        /// <summary>
        /// Stops the client.
        /// This will disconnect from the server.
        /// Optionally, you can pass a NetworkManager to register client modules.
        /// If you do not pass a NetworkManager, it will try to find one in the hierarchy.
        /// </summary>
        public void StopClient()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StopClient();
        }

        internal void StopClient(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
                networkManager.InternalUnregisterClientModules();

            StopClientInternal();
        }

        internal void StartClientInternalOnly()
        {
            StartClientInternal();
        }

        internal void StopClientInternalOnly()
        {
            StopClientInternal();
        }

        internal void StartServerInternalOnly()
        {
            StartServerInternal();
        }

        protected abstract void StartClientInternal();

        protected abstract void StartServerInternal();

        protected void StopClientInternal() => transport.Disconnect();

        protected void StopServerInternal() => transport.StopListening();
    }
}
