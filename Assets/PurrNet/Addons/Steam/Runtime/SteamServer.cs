#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

using System;
using PurrNet.Transports;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PurrNet.Logging;
using Steamworks;
#endif

namespace PurrNet.Steam
{
    public class SteamServer
    {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        const int MAX_MESSAGES = 256;

        private HSteamListenSocket _listenSocket;

        private bool _isDedicated;
        static byte[] buffer = new byte[1024];

        Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChanged;

        private readonly List<HSteamNetConnection> _connections = new List<HSteamNetConnection>();
        private readonly Dictionary<int, HSteamNetConnection> _connectionById =
 new Dictionary<int, HSteamNetConnection>();
        private readonly Dictionary<HSteamNetConnection, int> _idByConnection =
 new Dictionary<HSteamNetConnection, int>();
        private readonly Dictionary<int, ulong> _steamIdByConnection = new Dictionary<int, ulong>();
        private readonly Dictionary<HSteamNetConnection, SteamSendQueue> _sendQueues =
            new Dictionary<HSteamNetConnection, SteamSendQueue>();
        private readonly List<HSteamNetConnection> _flushConnections = new List<HSteamNetConnection>();
        private readonly List<HSteamNetConnection> _receiveConnections = new List<HSteamNetConnection>();
        private readonly Queue<int> _pendingDisconnects = new Queue<int>();
        private readonly HashSet<HSteamNetConnection> _sendLimitWarnings = new HashSet<HSteamNetConnection>();

        readonly IntPtr[] _messages = new IntPtr[MAX_MESSAGES];
#endif

#pragma warning disable CS0067 // Event is never used
        public event Action<int> onRemoteConnected;
        public event Action<int> onRemoteDisconnected;
        public event Action<int, ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        public bool listening => _listenSocket != HSteamListenSocket.Invalid;
#else
        public bool listening => false;
#endif

        public void Listen(ushort port, bool dedicated = false)
        {
            Listen(port, dedicated, SteamSendSettings.defaults);
        }

        internal void Listen(ushort port, bool dedicated, SteamSendSettings sendSettings)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            _isDedicated = dedicated;
            var options = sendSettings.CreateOptions();

            var localAddress = new SteamNetworkingIPAddr();
            localAddress.Clear();
            localAddress.SetIPv4(0, port);

            if (dedicated)
            {
                _listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketIP(
                    ref localAddress,
                    options.Length,
                    options
                );
            }
            else
            {
                _listenSocket = SteamNetworkingSockets.CreateListenSocketIP(
                    ref localAddress,
                    options.Length,
                    options
                );
            }

            PostListen();
#endif
        }

        public void ListenP2P(bool dedicated = false)
        {
            ListenP2P(dedicated, SteamSendSettings.defaults);
        }

        internal void ListenP2P(bool dedicated, SteamSendSettings sendSettings)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            _isDedicated = dedicated;
            var options = sendSettings.CreateOptions();

            if (dedicated)
            {
                _listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(
                    0,
                    options.Length,
                    options
                );
            }
            else
            {
                _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(
                    0,
                    options.Length,
                    options
                );
            }

            PostListen();
#endif
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        private void PostListen()
        {
            if (_listenSocket == HSteamListenSocket.Invalid)
            {
                PurrLogger.LogError("Failed to create listen socket.");
                return;
            }

            _connectionStatusChanged = _isDedicated ?
                Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(OnRemoteConnectionState) :
                Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnRemoteConnectionState);
        }
#endif

        public void SendMessages()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            // Pumping a failed queue removes its connection from the live list.
            _flushConnections.Clear();
            _flushConnections.AddRange(_connections);
            for (var i = 0; i < _flushConnections.Count; i++)
            {
                var conn = _flushConnections[i];
                if (_idByConnection.ContainsKey(conn))
                    FlushNativeConnection(conn);
            }
#endif
        }

        public void ReceiveMessages()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            while (_pendingDisconnects.Count > 0)
            {
                var disconnectedId = _pendingDisconnects.Dequeue();
                NotifyDisconnected(disconnectedId);
            }

            _receiveConnections.Clear();
            _receiveConnections.AddRange(_connections);
            for (var i = 0; i < _receiveConnections.Count; i++)
            {
                var conn = _receiveConnections[i];

                if (!_idByConnection.TryGetValue(conn, out var connId))
                    continue;

                int receivedCount = _isDedicated ?
                    SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(conn, _messages, MAX_MESSAGES) :
                    SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, _messages, MAX_MESSAGES);

                for (int j = 0; j < receivedCount; j++)
                {
                    var ptr = _messages[j];
                    if (!_connectionById.TryGetValue(connId, out var current) || current != conn)
                    {
                        SteamNetworkingMessage_t.Release(ptr);
                        continue;
                    }

                    try
                    {
                        var data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptr);
                        int packetLength = data.m_cbSize;
                        MakeSureBufferCanFit(packetLength);
                        Marshal.Copy(data.m_pData, buffer, 0, packetLength);
                        SteamNetworkingMessage_t.Release(ptr);
                        ptr = IntPtr.Zero;
                        onDataReceived?.Invoke(connId, new ByteData(buffer, 0, packetLength));
                    }
                    catch
                    {
                        if (ptr != IntPtr.Zero)
                            SteamNetworkingMessage_t.Release(ptr);
                        for (var remaining = j + 1; remaining < receivedCount; remaining++)
                            SteamNetworkingMessage_t.Release(_messages[remaining]);
                        throw;
                    }
                }
            }
#endif
        }

        public void Kick(int id)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(id, out var conn))
                return;

            CloseConnection(conn, null, true);
#endif
        }
        
        public int GetRoundTripTime(int connectionId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(connectionId, out var conn))
                return -1;

            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();

            var result = _isDedicated
                ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes)
                : SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes);

            return result == EResult.k_EResultOK ? status.m_nPing : -1;
#else
            return -1;
#endif
        }

        public ulong GetSteamID(int connectionId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_steamIdByConnection.TryGetValue(connectionId, out var steamID))
                return steamID;
#endif
            return 0;
        }

        public SteamSendStatistics GetSendStatistics(int connectionId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_connectionById.TryGetValue(connectionId, out var conn) &&
                _sendQueues.TryGetValue(conn, out var queue))
                return queue.statistics;
#endif
            return default;
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        private static void MakeSureBufferCanFit(int packetLength)
        {
            if (buffer.Length < packetLength)
                Array.Resize(ref buffer, packetLength);
        }
#endif
        public void FlushConnection(int connId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(connId, out var conn))
                return;

            FlushNativeConnection(conn);
#endif
        }

        public void SendToConnection(int connId, ByteData data, Channel channel)
        {
            TrySendToConnection(connId, data, channel);
        }

        internal bool TrySendToConnection(int connId, ByteData data, Channel channel)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(connId, out var conn))
                return false;

            string failure;
            try
            {
                if (!_sendQueues.TryGetValue(conn, out var queue))
                {
                    queue = new SteamSendQueue((ptr, length, flags) => SendNative(conn, ptr, length, flags),
                        () => UnityEngine.Time.realtimeSinceStartupAsDouble);
                    _sendQueues.Add(conn, queue);
                }
                bool healthy = queue.Send(data, channel, out var accepted);
                if (queue.statistics.lastSendResult == (int)EResult.k_EResultLimitExceeded && _sendLimitWarnings.Add(conn))
                {
                    try
                    {
                        PurrLogger.LogWarning($"Steam send buffer limit reached for server connection {connId} ({conn}). " +
                            "Reliable messages retry in order; unreliable sends may be refused. " +
                            "Check outgoing traffic and configured Steam send limits. This warning is logged once per connection.");
                    }
                    catch { }
                }
                if (healthy)
                    return accepted;
                failure = queue.failureReason;
            }
            catch (Exception e)
            {
                failure = $"Steam send failed: {e.Message}";
            }
            FailSend(conn, failure);
#endif
            return false;
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        private EResult SendNative(HSteamNetConnection conn, IntPtr data, uint length, int flags)
        {
            return _isDedicated
                ? SteamGameServerNetworkingSockets.SendMessageToConnection(conn, data, length, flags, out _)
                : SteamNetworkingSockets.SendMessageToConnection(conn, data, length, flags, out _);
        }

        private void FlushNativeConnection(HSteamNetConnection conn)
        {
            string failure;
            try
            {
                if (_sendQueues.TryGetValue(conn, out var queue) && !queue.Pump())
                {
                    failure = queue.failureReason;
                }
                else
                {
                    if (_isDedicated)
                        SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
                    else SteamNetworkingSockets.FlushMessagesOnConnection(conn);
                    return;
                }
            }
            catch (Exception e)
            {
                failure = $"Steam send failed: {e.Message}";
            }
            FailSend(conn, failure);
        }

        private void FailSend(HSteamNetConnection conn, string reason)
        {
            PurrLogger.LogError($"Disconnecting Steam connection {conn}: {reason}");
            CloseConnection(conn, reason, true);
        }

        private void CloseConnection(HSteamNetConnection conn, string reason = null, bool deferNotification = false)
        {
            try
            {
                if (_isDedicated)
                    SteamGameServerNetworkingSockets.CloseConnection(conn, 0, reason, false);
                else SteamNetworkingSockets.CloseConnection(conn, 0, reason, false);
            }
            catch
            {
                // The native subsystem may already be shutting down. Still release managed state.
            }
            finally
            {
                // Steam does not post a local state callback when we explicitly close a handle.
                RemoveConnection(conn, deferNotification);
            }
        }

        private int _nextConnectionId;

        private void AddConnection(HSteamNetConnection connection, ulong steamId)
        {
            int id = _nextConnectionId++;
            _connections.Add(connection);
            _connectionById.Add(id, connection);
            _idByConnection.Add(connection, id);
            _steamIdByConnection.Add(id, steamId);

            onRemoteConnected?.Invoke(id);
        }

        private void RemoveConnection(HSteamNetConnection connection, bool deferNotification)
        {
            _sendLimitWarnings.Remove(connection);
            if (_sendQueues.Remove(connection, out var queue))
                queue.Clear();
            if (_connections.Remove(connection) && _idByConnection.Remove(connection, out var _id))
            {
                _connectionById.Remove(_id);
                if (deferNotification)
                    _pendingDisconnects.Enqueue(_id);
                else
                    NotifyDisconnected(_id);
            }
        }

        private void NotifyDisconnected(int connectionId)
        {
            try
            {
                // Disconnect handlers may still need the Steam identity for cleanup.
                onRemoteDisconnected?.Invoke(connectionId);
            }
            finally
            {
                _steamIdByConnection.Remove(connectionId);
            }
        }

        private void OnRemoteConnectionState(SteamNetConnectionStatusChangedCallback_t args)
        {
            if (args.m_info.m_hListenSocket != _listenSocket)
                return;

            var state = args.m_info.m_eState;

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                {
                    var res = _isDedicated
                        ? SteamGameServerNetworkingSockets.AcceptConnection(args.m_hConn)
                        : SteamNetworkingSockets.AcceptConnection(args.m_hConn);

                    if (res != EResult.k_EResultOK)
                        PurrLogger.LogError($"Failed to accept connection: {res}");
                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                {
                    AddConnection(args.m_hConn, args.m_info.m_identityRemote.GetSteamID64());
                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                {
                    CloseConnection(args.m_hConn);
                    break;
                }
            }
        }
#endif

        public void Stop()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_connectionStatusChanged != null)
            {
                _connectionStatusChanged.Dispose();
                _connectionStatusChanged = null;
            }

            for (var o = 0; o < _connections.Count; o++)
            {
                try
                {
                    var conn = _connections[o];
                    if (_isDedicated)
                         SteamGameServerNetworkingSockets.CloseConnection(conn, 0, null, false);
                    else SteamNetworkingSockets.CloseConnection(conn, 0, null, false);
                }
                catch
                {
                    // ignored
                }
            }

            _connections.Clear();
            _connectionById.Clear();
            _idByConnection.Clear();
            _steamIdByConnection.Clear();
            foreach (var queue in _sendQueues.Values)
                queue.Clear();
            _sendQueues.Clear();
            _pendingDisconnects.Clear();
            _sendLimitWarnings.Clear();

            if (_listenSocket == HSteamListenSocket.Invalid)
                return;

            try
            {
                if (_isDedicated)
                    SteamGameServerNetworkingSockets.CloseListenSocket(_listenSocket);
                else SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            }
            catch
            {
                // ignored
            }

            _listenSocket = HSteamListenSocket.Invalid;
#endif
        }
    }
}
