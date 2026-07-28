using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;
#if NAKAMA
using Nakama;
using System;
using System.Collections.Concurrent;
#endif

namespace PurrNet.Nakama
{
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("PurrNet/Transport/Nakama Transport")]
    public class NakamaTransport : GenericTransport, ITransport
    {
        [Header("Match Settings")]
        [Tooltip("Match ID. As a client, the match to join. As a server, a non-empty value adopts an existing match (e.g. one created by a lobby system) instead of creating a new one.")]
        [SerializeField] private string _matchId;

        [Tooltip("Base op code used for PurrNet data frames. Two consecutive op codes are reserved: this one for server→client traffic and (this + 1) for client→server traffic. Pick a base value such that neither collides with other systems sharing this match (e.g. PurrLobby uses 1-8).")]
        [SerializeField] private long _opCode = 100;

        [Tooltip("Optional. The user id of the presence to treat as the host when joining as a client. When empty, the first non-self presence is assumed to be the host. Set this for lobby/host-migration flows where the host is not necessarily the first presence in the match.")]
        [SerializeField] private string _hostUserId;

#if NAKAMA
        // Distinct op code for client→host traffic so host+client mode (single socket, self-echoed
        // state) can tell which side a message was sent from.
        private long serverBoundOpCode => _opCode + 1;
#endif

        public string matchId
        {
            get => _matchId;
            set => _matchId = value;
        }

        public long opCode
        {
            get => _opCode;
            set => _opCode = value;
        }

        /// <summary>
        /// Optional user id of the presence to bind to as the host when joining as a client. When
        /// null/empty, the first non-self presence is assumed to be the host. Set this before
        /// connecting in flows where the host can change (e.g. lobby host migration).
        /// </summary>
        public string hostUserId
        {
            get => _hostUserId;
            set => _hostUserId = value;
        }

#if NAKAMA
        /// <summary>
        /// Authenticated Nakama socket used for relay traffic. The user is responsible for connecting
        /// and authenticating it before calling Listen/Connect, and for closing it on shutdown.
        /// </summary>
        public ISocket socket { get; set; }

        /// <summary>
        /// Match handle returned by Nakama after Create/Join. Null when not in a match.
        /// </summary>
        public IMatch match { get; private set; }

        private IUserPresence _self;
        private IUserPresence _hostPresence;

        private readonly Dictionary<string, int> _idBySessionId = new();
        private readonly Dictionary<int, IUserPresence> _presenceById = new();
        private int _nextConnectionId = 1;

        private readonly ConcurrentQueue<Action> _eventQueue = new();
#endif

        public override bool isSupported
        {
#if NAKAMA
            get => true;
#else
            get => false;
#endif
        }

        public override ITransport transport => this;

        public bool SupportsChannel(Channel channel) => true;

        public int GetMTU(Connection target, Channel channel, bool asServer) => 8192;

        private readonly List<Connection> _connections = new();
        public IReadOnlyList<Connection> connections => _connections;

        private ConnectionState _listenerState = ConnectionState.Disconnected;
        public ConnectionState listenerState
        {
            get => _listenerState;
            private set
            {
                if (_listenerState == value) return;
                _listenerState = value;
                onConnectionState?.Invoke(_listenerState, true);
            }
        }

        private ConnectionState _clientState = ConnectionState.Disconnected;
        public ConnectionState clientState
        {
            get => _clientState;
            private set
            {
                if (_clientState == value) return;
                _clientState = value;
                onConnectionState?.Invoke(_clientState, false);
            }
        }

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        protected override void StartClientInternal() => Connect(_matchId, 0);
        protected override void StartServerInternal() => Listen(0);

        public void Listen(ushort port)
        {
#if NAKAMA
            if (listenerState != ConnectionState.Disconnected)
                return;

            if (socket is not { IsConnected: true })
            {
                PurrLogger.LogError("`socket` must be assigned and connected before Listen.");
                return;
            }

            listenerState = ConnectionState.Connecting;
            HookSocket();

            _ = string.IsNullOrEmpty(_matchId) ?
                CreateMatchAsync() :
                AdoptMatchAsync(_matchId);
#else
            PurrLogger.LogError("Nakama package is not installed.");
#endif
        }

#if NAKAMA
        private async System.Threading.Tasks.Task CreateMatchAsync()
        {
            try
            {
                var created = await socket.CreateMatchAsync();
                _eventQueue.Enqueue(() => OnMatchAcquired(created));
            }
            catch (Exception e)
            {
                _eventQueue.Enqueue(() =>
                {
                    PurrLogger.LogError($"CreateMatchAsync failed: {e.Message}");
                    UnhookSocket();
                    listenerState = ConnectionState.Disconnected;
                });
            }
        }

        private async System.Threading.Tasks.Task AdoptMatchAsync(string id)
        {
            try
            {
                // JoinMatchAsync is idempotent for already-joined matches (e.g. one this socket
                // created via the lobby system), so the same call covers fresh and re-adoption.
                var joined = await socket.JoinMatchAsync(id);
                _eventQueue.Enqueue(() => OnMatchAcquired(joined));
            }
            catch (Exception e)
            {
                _eventQueue.Enqueue(() =>
                {
                    PurrLogger.LogError($"Failed to adopt existing match `{id}`: {e.Message}");
                    UnhookSocket();
                    listenerState = ConnectionState.Disconnected;
                });
            }
        }

        private void OnMatchAcquired(IMatch acquired)
        {
            match = acquired;
            _self = acquired.Self;
            _matchId = acquired.Id;
            _connections.Clear();
            _idBySessionId.Clear();
            _presenceById.Clear();
            _nextConnectionId = 1;

            var prejoined = new List<Connection>();
            if (acquired.Presences != null)
            {
                foreach (var p in acquired.Presences)
                {
                    if (_self != null && p.SessionId == _self.SessionId) continue;
                    if (_idBySessionId.ContainsKey(p.SessionId)) continue;

                    int cid = _nextConnectionId++;
                    _idBySessionId[p.SessionId] = cid;
                    _presenceById[cid] = p;
                    var conn = new Connection(cid);
                    _connections.Add(conn);
                    prejoined.Add(conn);
                }
            }

            listenerState = ConnectionState.Connected;

            for (int i = 0; i < prejoined.Count; i++)
                onConnected?.Invoke(prejoined[i], true);
        }
#endif

        public void StopListening()
        {
#if NAKAMA
            if (listenerState == ConnectionState.Disconnected)
                return;

            listenerState = ConnectionState.Disconnecting;
            _connections.Clear();
            _idBySessionId.Clear();
            _presenceById.Clear();

            // Only leave the Nakama match when the client isn't using it. In host+client mode
            // the client owns the same match and leaving here would knock it offline too.
            if (clientState == ConnectionState.Disconnected)
            {
                var idToLeave = match?.Id;
                match = null;
                _self = null;
                UnhookSocket();

                if (!string.IsNullOrEmpty(idToLeave) && socket != null && socket.IsConnected)
                    _ = LeaveMatchSafe(idToLeave);
            }

            listenerState = ConnectionState.Disconnected;
#endif
        }

        public void Connect(string ip, ushort port)
        {
#if NAKAMA
            if (clientState != ConnectionState.Disconnected)
                return;

            if (socket is not { IsConnected: true })
            {
                PurrLogger.LogError("`socket` must be assigned and connected before Connect.");
                return;
            }

            if (string.IsNullOrEmpty(ip))
            {
                PurrLogger.LogError("matchId (passed via Connect's ip parameter) must not be empty.");
                return;
            }

            _matchId = ip;
            clientState = ConnectionState.Connecting;
            HookSocket();

            _ = JoinMatchAsync(ip);
#else
            PurrLogger.LogError("Nakama package is not installed.");
#endif
        }

#if NAKAMA
        private async System.Threading.Tasks.Task JoinMatchAsync(string id)
        {
            try
            {
                var joined = await socket.JoinMatchAsync(id);
                _eventQueue.Enqueue(() => OnMatchJoined(joined));
            }
            catch (Exception e)
            {
                _eventQueue.Enqueue(() =>
                {
                    PurrLogger.LogError($"JoinMatchAsync failed: {e.Message}");
                    if (listenerState == ConnectionState.Disconnected)
                        UnhookSocket();
                    clientState = ConnectionState.Disconnected;
                });
            }
        }

        private void OnMatchJoined(IMatch joined)
        {
            if (listenerState == ConnectionState.Connected && match != null && joined.Id == match.Id && _self != null)
            {
                _hostPresence = _self;

                if (!_idBySessionId.TryGetValue(_self.SessionId, out var selfId))
                {
                    selfId = _nextConnectionId++;
                    _idBySessionId[_self.SessionId] = selfId;
                    _presenceById[selfId] = _self;
                    _connections.Add(new Connection(selfId));
                }

                clientState = ConnectionState.Connected;
                onConnected?.Invoke(new Connection(0), false);
                onConnected?.Invoke(new Connection(selfId), true);
                return;
            }

            match = joined;
            _self = joined.Self;

            _hostPresence = ResolveHostPresence(joined);

            if (_hostPresence == null)
            {
                PurrLogger.LogError("Joined match has no host presence; refusing connection.");
                clientState = ConnectionState.Disconnected;
                _ = LeaveMatchSafe(joined.Id);
                return;
            }

            clientState = ConnectionState.Connected;
            onConnected?.Invoke(new Connection(0), false);
        }

        private IUserPresence ResolveHostPresence(IMatch joined)
        {
            if (joined.Presences == null)
                return null;

            // When a host user id is provided (e.g. by a lobby or host-migration system), bind to
            // that exact presence so the client connects to the elected host rather than guessing.
            if (!string.IsNullOrEmpty(_hostUserId))
            {
                foreach (var p in joined.Presences)
                {
                    if (p.UserId != _hostUserId)
                        continue;
                    if (_self != null && p.SessionId == _self.SessionId)
                        continue;
                    return p;
                }
            }

            // Fallback: assume the first non-self presence is the host. Holds when the host created
            // the match before any client joined and no explicit host id was provided.
            foreach (var p in joined.Presences)
            {
                if (_self == null || p.SessionId != _self.SessionId)
                    return p;
            }

            return null;
        }

        private async System.Threading.Tasks.Task LeaveMatchSafe(string id)
        {
            try
            {
                await socket.LeaveMatchAsync(id);
            }
            catch (Exception e)
            {
                PurrLogger.LogWarning($"LeaveMatchAsync failed: {e.Message}");
            }
        }
#endif

        public void Disconnect()
        {
#if NAKAMA
            if (clientState == ConnectionState.Disconnected)
                return;

            clientState = ConnectionState.Disconnecting;
            _hostPresence = null;

            // Host+client: Nakama doesn't generate a Leave event for self, so manually drop the
            // self-presence connection on the listener side.
            if (listenerState == ConnectionState.Connected
                && _self != null
                && _idBySessionId.Remove(_self.SessionId, out var selfId))
            {
                _presenceById.Remove(selfId);
                var selfConn = new Connection(selfId);
                _connections.Remove(selfConn);
                onDisconnected?.Invoke(selfConn, DisconnectReason.ClientRequest, true);
            }

            // Only leave the Nakama match when the listener isn't using it. In host+client mode
            // the listener owns the match and leaving it here would also tear down the server.
            if (listenerState == ConnectionState.Disconnected)
            {
                // Only leave if we actually joined; an adoption that errored leaves match==null
                // and there's nothing on the server to tell goodbye to.
                var idToLeave = match?.Id;
                match = null;
                _self = null;
                UnhookSocket();

                if (!string.IsNullOrEmpty(idToLeave) && socket != null && socket.IsConnected)
                    _ = LeaveMatchSafe(idToLeave);
            }

            clientState = ConnectionState.Disconnected;
#endif
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
            => onDataReceived?.Invoke(conn, data, asServer);

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
            => onDataSent?.Invoke(conn, data, asServer);

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
#if NAKAMA
            if (listenerState != ConnectionState.Connected || match == null || socket == null)
                return;
            if (!target.isValid)
                return;
            if (!_presenceById.TryGetValue(target.connectionId, out var presence))
                return;

            var payload = ToArray(data);
            _ = SendStateSafe(match.Id, _opCode, payload, new[] { presence });
            RaiseDataSent(target, data, true);
#endif
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
#if NAKAMA
            if (clientState != ConnectionState.Connected || match == null || socket == null || _hostPresence == null)
                return;

            var payload = ToArray(data);
            _ = SendStateSafe(match.Id, serverBoundOpCode, payload, new[] { _hostPresence });
            RaiseDataSent(default, data, false);
#endif
        }

#if NAKAMA
        private async System.Threading.Tasks.Task SendStateSafe(string mid, long op, byte[] payload, IEnumerable<IUserPresence> targets)
        {
            try
            {
                await socket.SendMatchStateAsync(mid, op, payload, targets);
            }
            catch (Exception e)
            {
                PurrLogger.LogWarning($"SendMatchStateAsync failed: {e.Message}");
            }
        }

        private static byte[] ToArray(ByteData data)
        {
            if (data.length == 0)
                return Array.Empty<byte>();
            var copy = new byte[data.length];
            Buffer.BlockCopy(data.data, data.offset, copy, 0, data.length);
            return copy;
        }
#endif

        public void CloseConnection(Connection conn)
        {
#if NAKAMA
            // Nakama relay has no host-side kick. Drop the mapping locally and notify;
            // the remote will continue to see the match until it leaves on its own.
            if (!_presenceById.Remove(conn.connectionId, out var presence))
                return;

            _idBySessionId.Remove(presence.SessionId);
            _connections.Remove(conn);
            onDisconnected?.Invoke(conn, DisconnectReason.ServerRequest, true);
#endif
        }

        public void ReceiveMessages(float delta)
        {
#if NAKAMA
            while (_eventQueue.TryDequeue(out var evt))
            {
                try { evt(); }
                catch (Exception e) { PurrLogger.LogError($"Event handler threw: {e}"); }
            }
#endif
        }

        public void UnityUpdate(float delta)
        {
            ReceiveMessages(delta);
        }

        public void SendMessages(float delta)
        {
            // Sends are dispatched immediately via the Nakama socket; nothing to flush per tick.
        }

#if NAKAMA
        private bool _hooked;

        private void HookSocket()
        {
            if (_hooked || socket == null) return;
            socket.ReceivedMatchState += OnReceivedMatchState;
            socket.ReceivedMatchPresence += OnReceivedMatchPresence;
            _hooked = true;
        }

        private void UnhookSocket()
        {
            if (!_hooked || socket == null) return;
            socket.ReceivedMatchState -= OnReceivedMatchState;
            socket.ReceivedMatchPresence -= OnReceivedMatchPresence;
            _hooked = false;
        }

        private void OnReceivedMatchState(IMatchState state)
        {
            // Nakama callbacks may arrive on a worker thread; marshal onto the tick.
            _eventQueue.Enqueue(() => HandleMatchState(state));
        }

        private void OnReceivedMatchPresence(IMatchPresenceEvent evt)
        {
            _eventQueue.Enqueue(() => HandleMatchPresence(evt));
        }

        private void HandleMatchState(IMatchState state)
        {
            if (match == null || state.MatchId != match.Id)
                return;

            var bytes = state.State ?? Array.Empty<byte>();
            var data = new ByteData(bytes, 0, bytes.Length);

            if (state.OpCode == serverBoundOpCode
                && listenerState == ConnectionState.Connected
                && state.UserPresence != null
                && _idBySessionId.TryGetValue(state.UserPresence.SessionId, out var id))
            {
                onDataReceived?.Invoke(new Connection(id), data, true);
                return;
            }

            if (state.OpCode == _opCode
                && clientState == ConnectionState.Connected
                && _hostPresence != null
                && state.UserPresence != null
                && state.UserPresence.SessionId == _hostPresence.SessionId)
            {
                onDataReceived?.Invoke(new Connection(0), data, false);
            }
        }

        private void HandleMatchPresence(IMatchPresenceEvent evt)
        {
            if (match == null || evt.MatchId != match.Id)
                return;

            if (listenerState == ConnectionState.Connected)
            {
                if (evt.Joins != null)
                {
                    foreach (var p in evt.Joins)
                    {
                        if (_self != null && p.SessionId == _self.SessionId) continue;
                        if (_idBySessionId.ContainsKey(p.SessionId)) continue;

                        int id = _nextConnectionId++;
                        _idBySessionId[p.SessionId] = id;
                        _presenceById[id] = p;
                        var conn = new Connection(id);
                        _connections.Add(conn);
                        onConnected?.Invoke(conn, true);
                    }
                }

                if (evt.Leaves != null)
                {
                    foreach (var p in evt.Leaves)
                    {
                        if (!_idBySessionId.Remove(p.SessionId, out var id))
                            continue;
                        _presenceById.Remove(id);
                        var conn = new Connection(id);
                        _connections.Remove(conn);
                        onDisconnected?.Invoke(conn, DisconnectReason.ClientRequest, true);
                    }
                }
            }

            if (clientState == ConnectionState.Connected && _hostPresence != null && evt.Leaves != null)
            {
                foreach (var p in evt.Leaves)
                {
                    if (p.SessionId != _hostPresence.SessionId) continue;
                    onDisconnected?.Invoke(new Connection(0), DisconnectReason.ServerRequest, false);
                    _hostPresence = null;
                    clientState = ConnectionState.Disconnected;
                    if (listenerState == ConnectionState.Disconnected)
                    {
                        match = null;
                        _self = null;
                        UnhookSocket();
                    }
                    break;
                }
            }
        }

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            UnhookSocket();
            while (_eventQueue.TryDequeue(out _)) { }
            var idToLeave = match?.Id;
            match = null;
            _self = null;
            _hostPresence = null;
            _connections.Clear();
            _idBySessionId.Clear();
            _presenceById.Clear();
            listenerState = ConnectionState.Disconnected;
            clientState = ConnectionState.Disconnected;
            if (!string.IsNullOrEmpty(idToLeave) && socket != null && socket.IsConnected)
                _ = LeaveMatchSafe(idToLeave);
        }
#endif
    }
}
