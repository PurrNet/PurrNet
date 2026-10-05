#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

using System;
using System.Collections;
using PurrNet.Transports;
using PurrConnectionState = PurrNet.Transports.ConnectionState;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using System.Runtime.InteropServices;
using PurrNet.Logging;
using Steamworks;
#endif

namespace PurrNet.Steam
{
    public class SteamClient
    {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        const int MAX_MESSAGES = 256;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _onLocalConnectionState;

        private CSteamID _hostSteamID;
        private HSteamNetConnection _connection;
        private bool _isDedicated;
        private SteamSendQueue _sendQueue;
        private bool _sendLimitWarningLogged;
        private bool _pendingDisconnect;
        static byte[] buffer = new byte[1024];
        readonly IntPtr[] _messages = new IntPtr[MAX_MESSAGES];
#endif

#pragma warning disable CS0067 // Event is never used
        public event Action<ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used
        public event Action<PurrNet.Transports.ConnectionState> onConnectionState;

        private PurrConnectionState _state = PurrConnectionState.Disconnected;

        public PurrNet.Transports.ConnectionState connectionState
        {
            get => _state;
            set
            {
                if (_state == value)
                    return;

                _state = value;
                onConnectionState?.Invoke(_state);
            }
        }

        public int GetRoundTripTime()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_connection == HSteamNetConnection.Invalid || connectionState != PurrConnectionState.Connected)
                return -1;

            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();

            var result = _isDedicated
                ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(_connection, ref status, 0, ref lanes)
                : SteamNetworkingSockets.GetConnectionRealTimeStatus(_connection, ref status, 0, ref lanes);

            return result == EResult.k_EResultOK ? status.m_nPing : -1;
#else
            return -1;
#endif
        }

        public SteamSendStatistics sendStatistics
        {
            get
            {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
                return _sendQueue?.statistics ?? default;
#else
                return default;
#endif
            }
        }

        public IEnumerator Connect(string address, ushort port, bool dedicated = false)
        {
            return Connect(address, port, dedicated, SteamSendSettings.defaults);
        }

        internal IEnumerator Connect(string address, ushort port, bool dedicated, SteamSendSettings sendSettings)
        {
            yield return null;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            _isDedicated = dedicated;

            var addr = new SteamNetworkingIPAddr();
            addr.Clear();
            addr.SetIPv4(address.GetIPv4(), port);

            var options = sendSettings.CreateOptions();
            _connection = _isDedicated ?
                SteamGameServerNetworkingSockets.ConnectByIPAddress(ref addr, options.Length, options) :
                SteamNetworkingSockets.ConnectByIPAddress(ref addr, options.Length, options);

            PostConnect();
#endif
        }

        public IEnumerator ConnectP2P(string steamId, bool dedicated = false)
        {
            return ConnectP2P(steamId, dedicated, SteamSendSettings.defaults);
        }

        internal IEnumerator ConnectP2P(string steamId, bool dedicated, SteamSendSettings sendSettings)
        {
            yield return null;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (ulong.TryParse(steamId, out var id) == false)
            {
                if (steamId is "localhost" or "127.0.0.1")
                {
                    id = SteamUser.GetSteamID().m_SteamID;
                }
                else
                {
                    PurrLogger.LogError("Invalid Steam ID provided as address to connect");
                    yield break;
                }
            }

            _isDedicated = dedicated;
            _hostSteamID = new CSteamID(id);

            var networkIdentity = new SteamNetworkingIdentity();
            networkIdentity.SetSteamID(_hostSteamID);

            var options = sendSettings.CreateOptions();
            _connection = _isDedicated ?
                SteamGameServerNetworkingSockets.ConnectP2P(ref networkIdentity, 0, options.Length, options) :
                SteamNetworkingSockets.ConnectP2P(ref networkIdentity, 0, options.Length, options);

            PostConnect();
#endif
        }

        public void Send(ByteData data, Channel channel)
        {
            TrySend(data, channel);
        }

        internal bool TrySend(ByteData data, Channel channel)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_connection == HSteamNetConnection.Invalid)
                return false;

            string failure;
            try
            {
                _sendQueue ??= new SteamSendQueue(SendNative, () => UnityEngine.Time.realtimeSinceStartupAsDouble);
                bool healthy = _sendQueue.Send(data, channel, out var accepted);
                if (!_sendLimitWarningLogged && _sendQueue.statistics.lastSendResult == (int)EResult.k_EResultLimitExceeded)
                {
                    _sendLimitWarningLogged = true;
                    try
                    {
                        PurrLogger.LogWarning($"Steam send buffer limit reached for client connection {_connection}. " +
                            "Reliable messages retry in order; unreliable sends may be refused. " +
                            "Check outgoing traffic and configured Steam send limits. This warning is logged once per connection.");
                    }
                    catch { }
                }
                if (healthy)
                    return accepted;
                failure = _sendQueue.failureReason;
            }
            catch (Exception e)
            {
                failure = $"Steam send failed: {e.Message}";
            }
            FailSend(failure);
#endif
            return false;
        }

        public void SendMessages()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_connection == HSteamNetConnection.Invalid)
                return;
            string failure;
            try
            {
                if (_sendQueue != null && !_sendQueue.Pump())
                {
                    failure = _sendQueue.failureReason;
                }
                else
                {
                    if (_isDedicated)
                        SteamGameServerNetworkingSockets.FlushMessagesOnConnection(_connection);
                    else SteamNetworkingSockets.FlushMessagesOnConnection(_connection);
                    return;
                }
            }
            catch (Exception e)
            {
                failure = $"Steam send failed: {e.Message}";
            }
            FailSend(failure);
#endif
        }

        public void ReceiveMessages()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_pendingDisconnect)
                CompleteDisconnect();
            if (_connection == HSteamNetConnection.Invalid)
                return;
            var connection = _connection;
            int receivedCount = _isDedicated ?
                SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(_connection, _messages, MAX_MESSAGES) :
                SteamNetworkingSockets.ReceiveMessagesOnConnection(_connection, _messages, MAX_MESSAGES);

            for (int j = 0; j < receivedCount; j++)
            {
                var ptr = _messages[j];
                if (_connection != connection)
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
                    onDataReceived?.Invoke(new ByteData(buffer, 0, packetLength));
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
#endif
        }
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS

        private EResult SendNative(IntPtr data, uint length, int flags)
        {
            return _isDedicated
                ? SteamGameServerNetworkingSockets.SendMessageToConnection(_connection, data, length, flags, out _)
                : SteamNetworkingSockets.SendMessageToConnection(_connection, data, length, flags, out _);
        }

        private void FailSend(string reason)
        {
            PurrLogger.LogError($"Disconnecting Steam client: {reason}");
            Disconnect(reason, true);
        }

        private static void MakeSureBufferCanFit(int packetLength)
        {
            if (buffer.Length < packetLength)
                Array.Resize(ref buffer, packetLength);
        }

        private void PostConnect()
        {
            _pendingDisconnect = false;
            _sendLimitWarningLogged = false;
            _sendQueue = null;
            if (_connection == HSteamNetConnection.Invalid)
            {
                connectionState = PurrConnectionState.Disconnecting;
                connectionState = PurrConnectionState.Disconnected;
                PurrLogger.LogError("Failed to connect to host");
                return;
            }

            connectionState = PurrConnectionState.Connecting;
            _onLocalConnectionState = _isDedicated
                ? Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(OnLocalConnectionState)
                : Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnLocalConnectionState);
        }

        private void OnLocalConnectionState(SteamNetConnectionStatusChangedCallback_t param)
        {
            if (param.m_hConn != _connection)
                return;

            switch (param.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    connectionState = PurrConnectionState.Connecting;
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    connectionState = PurrConnectionState.Connected;
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    Disconnect();
                    break;
            }
        }

        void Disconnect(string reason = null, bool deferNotification = false)
        {
            _sendQueue?.Clear();
            if (_connection != HSteamNetConnection.Invalid)
            {
                var connection = _connection;
                _connection = HSteamNetConnection.Invalid;
                try
                {
                    if (_isDedicated)
                        SteamGameServerNetworkingSockets.CloseConnection(connection, 0, reason, false);
                    else SteamNetworkingSockets.CloseConnection(connection, 0, reason, false);
                }
                catch
                {
                    // ignored
                }

                _pendingDisconnect = true;
            }
            // Defer callbacks until receive/update so send callers can finish iterating players.
            if (_pendingDisconnect && !deferNotification)
                CompleteDisconnect();
        }

        private void CompleteDisconnect()
        {
            _pendingDisconnect = false;
            if (connectionState != PurrConnectionState.Disconnected)
                connectionState = PurrConnectionState.Disconnecting;
            connectionState = PurrConnectionState.Disconnected;
        }
#endif

        public void Stop()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_onLocalConnectionState != null)
            {
                _onLocalConnectionState.Dispose();
                _onLocalConnectionState = null;
            }

            Disconnect();
#endif
        }
    }
}
