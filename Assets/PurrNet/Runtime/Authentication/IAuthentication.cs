using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet.Authentication
{
    public struct AuthenticationRequest : IPackedAuto
    {
        [CanBeNull] public string version;
        [CanBeNull] public string cookie;
    }

    public struct AuthenticationRequestData : IPackedAuto
    {
        /// <summary>
        /// This will be used to retrieve the same PlayerID from past sessions if present.
        /// </summary>
        [CanBeNull] public string cookie;

        /// <summary>
        /// The payload to be validated.
        /// </summary>
        public ByteData payload;
    }

    public struct AuthenticationRequest<T> : IPackedAuto
    {
        /// <summary>
        /// This will be used to retrieve the same PlayerID from past sessions if present.
        /// </summary>
        [CanBeNull] public string cookie;

        /// <summary>
        /// The payload to be validated.
        /// </summary>
        public T payload;

        public AuthenticationRequest(T payload)
        {
            this.payload = payload;
            cookie = null;
        }
    }

    public struct AuthenticationResponse : IPackedAuto
    {
        /// <summary>
        /// Whether the authentication was successful.
        /// If true, the client will be authenticated.
        /// Else, the client will be disconnected.
        /// </summary>
        public bool success;

        /// <summary>
        /// Optional cookie, this overrides the cookie in the AuthenticationRequest.
        /// This will be used to retrieve the same PlayerID from past sessions if present.
        /// </summary>
        [CanBeNull] public string cookie;

        public static implicit operator bool(AuthenticationResponse response) => response.success;

        public static implicit operator AuthenticationResponse(bool success) =>
            new AuthenticationResponse { success = success };
    }

    /// <summary>
    /// Result returned by <see cref="AuthenticationBehaviour{TRequest,TDenial}.ValidateClientPayload"/>.
    /// When <see cref="success"/> is false, <see cref="denialReason"/> is serialized and shipped to
    /// the client before its connection is closed.
    /// </summary>
    public struct AuthenticationResponse<TDenial> : IPackedAuto
    {
        public bool success;
        [CanBeNull] public string cookie;
        public TDenial denialReason;

        public static AuthenticationResponse<TDenial> Accept(string cookie = null) =>
            new AuthenticationResponse<TDenial> { success = true, cookie = cookie };

        public static AuthenticationResponse<TDenial> Deny(TDenial reason) =>
            new AuthenticationResponse<TDenial> { success = false, denialReason = reason };
    }

    /// <summary>
    /// Categorises why the server rejected an inbound connection. Surfaced to the server via
    /// <see cref="AuthModule.onAuthenticationDenied"/> and shipped to the client inside
    /// <see cref="AuthenticationDenialPacket"/> so user-supplied UI can react before the socket closes.
    /// </summary>
    public enum DenialKind : byte
    {
        VersionMismatch = 0,
        Timeout = 1,
        AuthenticatorRejected = 2,
        NoAuthenticator = 3,
    }

    /// <summary>
    /// Server -> client packet describing why a connection is about to be closed. The client decodes
    /// <see cref="reason"/> via the active <see cref="AuthenticationLayer.OnDenialReceived"/> override
    /// (typed by <see cref="AuthenticationBehaviour{TRequest,TDenial}"/>) and acks with
    /// <see cref="AuthenticationDenialAck"/>. The server closes the connection on ack OR after the
    /// authenticator's timeout, whichever fires first.
    /// </summary>
    public struct AuthenticationDenialPacket : IPackedAuto
    {
        public DenialKind kind;
        public ByteData reason;
    }

    /// <summary>
    /// Client -> server ack acknowledging an <see cref="AuthenticationDenialPacket"/> so the server
    /// can immediately close the socket without waiting for the ack timeout.
    /// </summary>
    public struct AuthenticationDenialAck : IPackedAuto
    {
    }

    public abstract class AuthenticationLayer : MonoBehaviour
    {
        [Tooltip("The time in seconds before the authentication times out and the client is disconnected.")]
        [SerializeField]
        private float _timeout = 5f;

        public float timeout
        {
            get => _timeout;
            set => _timeout = value;
        }

        public event Action<Connection, AuthenticationResponse> onAuthenticationComplete;

        /// <summary>
        /// Server-side event raised by behaviours that want to attach a typed denial payload to a
        /// rejection. The bytes are forwarded verbatim by <see cref="AuthModule"/> inside an
        /// <see cref="AuthenticationDenialPacket"/>. <c>null</c> means "no reason payload";
        /// pass actual bytes (even zero-length) to indicate the behaviour produced a payload.
        /// </summary>
        public event Action<Connection, ByteData?> onAuthenticationDeniedWithReason;

        public abstract void Subscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players);

        public abstract void Unsubscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players);

        public abstract void SendClientPayload(BroadcastModule broadcastModule, CookiesModule cookies);

        protected abstract void UnAuthenticateClient(Connection conn);

        /// <summary>
        /// Called on the client by <see cref="AuthModule"/> when a denial packet arrives. The default
        /// implementation does nothing; <see cref="AuthenticationBehaviour{TRequest,TDenial}"/>
        /// overrides this to deserialize the reason bytes into TDenial and surface
        /// <c>onDeniedByServer</c>. <c>reason</c> is <c>null</c> when the server sent no payload
        /// (e.g. built-in deniers like version mismatch / timeout).
        /// </summary>
        public virtual void OnDenialReceived(DenialKind kind, ByteData? reason)
        {
        }

        protected void TrigerAuthenticationComplete(Connection conn, AuthenticationResponse response)
        {
            try
            {
                onAuthenticationComplete?.Invoke(conn, response);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to complete authentation for {conn}: {e.Message}");
            }
        }

        protected void TriggerAuthenticationDeniedWithReason(Connection conn, ByteData? reason)
        {
            try
            {
                onAuthenticationDeniedWithReason?.Invoke(conn, reason);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to dispatch denial reason for {conn}: {e.Message}");
            }
        }
    }

    public abstract class AuthenticationBehaviour<T> : AuthenticationLayer
    {
        private PlayersManager _players;
        private INetworkManager _networkManager;

        public override void Subscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players)
        {
            _players = players;
            _networkManager = manager;

            broadcastModule.Subscribe<AuthenticationRequestData>(OnPayload);
            players.onPrePlayerLeft += UnAuthenticatePlayer;
        }

        public override void Unsubscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players)
        {
            broadcastModule.Unsubscribe<AuthenticationRequestData>(OnPayload);
            players.onPrePlayerLeft -= UnAuthenticatePlayer;
            _networkManager = null;
        }

        private void UnAuthenticatePlayer(PlayerID player, bool asserver)
        {
            if (_players.TryGetConnection(player, out var conn))
                UnAuthenticateClient(conn);
        }

        public override async void SendClientPayload(BroadcastModule broadcastModule, CookiesModule cookies)
        {
            try
            {
                var payload = await GetClientPayload();
                payload.cookie ??= cookies.GetOrSet(AuthModule.CONNECTION_COOKIE_KEY, Guid.NewGuid().ToString());
                using var packer = BitPackerPool.Get();
                Packer<string>.Write(packer, NetworkManager.version);
                Packer<T>.Write(packer, payload.payload);
                var data = new AuthenticationRequestData
                {
                    cookie = payload.cookie,
                    payload = packer.ToByteData()
                };
                broadcastModule.SendToServer(data);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async void OnPayload(Connection conn, AuthenticationRequestData data, bool asServer)
        {
            try
            {
                using var packer = BitPackerPool.Get(data.payload);
                T payload = default;
                string clientVersion = default;
                Packer<string>.Read(packer, ref clientVersion);
                Packer<T>.Read(packer, ref payload);

                if (!NetworkManager.VerifyVersion(clientVersion))
                {
                    var rules = _networkManager?.networkRules;
                    var behaviour = rules
                        ? rules.GetVersionMismatchBehaviour()
                        : VersionMismatchBehaviour.Warning;

                    if (behaviour == VersionMismatchBehaviour.Deny)
                        throw new Exception($"Client version mismatch. Client version: {clientVersion}, Server version: {NetworkManager.version}");

                    PurrLogger.LogWarning($"Client version mismatch. Client version: {clientVersion}, Server version: {NetworkManager.version}");
                }

                var result = await ValidateClientPayload(conn, payload);
                if (result.cookie == null && data.cookie != null)
                    result.cookie = data.cookie;
                TrigerAuthenticationComplete(conn, result);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                TrigerAuthenticationComplete(conn, false);
            }
        }

        /// <summary>
        /// Get the client payload to be validated.
        /// This gets called when a new client connects and is then sent to the server for validation.
        /// </summary>
        /// <returns>The client payload to be validated.</returns>
        protected abstract Task<AuthenticationRequest<T>> GetClientPayload();

        /// <summary>
        /// Once the client payload is received, this method is called to validate the payload.
        /// This only runs on the server.
        /// </summary>
        /// <param name="conn">The connection of the client.</param>
        /// <param name="payload">The client payload to be validated.</param>
        /// <returns>The result of the validation.</returns>
        protected abstract Task<AuthenticationResponse> ValidateClientPayload(Connection conn, T payload);
    }

    /// <summary>
    /// Authentication behaviour with a typed denial payload. On the server, returning
    /// <c>success = false</c> from <see cref="ValidateClientPayload"/> ships the supplied
    /// <typeparamref name="TDenial"/> to the client (via <see cref="AuthenticationDenialPacket"/>)
    /// before the socket closes. On the client, <see cref="onDeniedByServer"/> fires with the
    /// decoded reason so user UI can react.
    /// </summary>
    /// <remarks>
    /// This is a separate type from <see cref="AuthenticationBehaviour{T}"/> to preserve the
    /// existing contract; pick this overload when you need to surface a structured rejection
    /// reason to the client UI.
    /// </remarks>
    public abstract class AuthenticationBehaviour<TRequest, TDenial> : AuthenticationLayer
        where TDenial : struct
    {
        private PlayersManager _players;
        private INetworkManager _networkManager;

        /// <summary>
        /// Client-side event fired when the server has rejected the connection. The kind tells you
        /// which built-in or user denier triggered; reason is <c>null</c> for
        /// built-in deniers (version mismatch, timeout, etc.) and only carries a value when
        /// kind is <see cref="DenialKind.AuthenticatorRejected"/>.
        /// </summary>
        public event Action<DenialKind, TDenial?> onDeniedByServer;

        public override void Subscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players)
        {
            _players = players;
            _networkManager = manager;

            broadcastModule.Subscribe<AuthenticationRequestData>(OnPayload);
            players.onPrePlayerLeft += UnAuthenticatePlayer;
        }

        public override void Unsubscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players)
        {
            broadcastModule.Unsubscribe<AuthenticationRequestData>(OnPayload);
            players.onPrePlayerLeft -= UnAuthenticatePlayer;
            _networkManager = null;
        }

        private void UnAuthenticatePlayer(PlayerID player, bool asserver)
        {
            if (_players.TryGetConnection(player, out var conn))
                UnAuthenticateClient(conn);
        }

        public override async void SendClientPayload(BroadcastModule broadcastModule, CookiesModule cookies)
        {
            try
            {
                var payload = await GetClientPayload();
                payload.cookie ??= cookies.GetOrSet(AuthModule.CONNECTION_COOKIE_KEY, Guid.NewGuid().ToString());
                using var packer = BitPackerPool.Get();
                Packer<string>.Write(packer, NetworkManager.version);
                Packer<TRequest>.Write(packer, payload.payload);
                var data = new AuthenticationRequestData
                {
                    cookie = payload.cookie,
                    payload = packer.ToByteData()
                };
                broadcastModule.SendToServer(data);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async void OnPayload(Connection conn, AuthenticationRequestData data, bool asServer)
        {
            try
            {
                using var packer = BitPackerPool.Get(data.payload);
                TRequest payload = default;
                string clientVersion = default;
                Packer<string>.Read(packer, ref clientVersion);
                Packer<TRequest>.Read(packer, ref payload);

                if (!NetworkManager.VerifyVersion(clientVersion))
                {
                    var rules = _networkManager?.networkRules;
                    var behaviour = rules
                        ? rules.GetVersionMismatchBehaviour()
                        : VersionMismatchBehaviour.Warning;

                    if (behaviour == VersionMismatchBehaviour.Deny)
                        throw new Exception($"Client version mismatch. Client version: {clientVersion}, Server version: {NetworkManager.version}");

                    PurrLogger.LogWarning($"Client version mismatch. Client version: {clientVersion}, Server version: {NetworkManager.version}");
                }

                var result = await ValidateClientPayload(conn, payload);

                if (!result.success)
                {
                    using var dpacker = BitPackerPool.Get();
                    Packer<TDenial>.Write(dpacker, result.denialReason);
                    TriggerAuthenticationDeniedWithReason(conn, dpacker.ToByteData());
                }

                var response = new AuthenticationResponse
                {
                    success = result.success,
                    cookie = result.cookie ?? data.cookie
                };
                TrigerAuthenticationComplete(conn, response);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                TrigerAuthenticationComplete(conn, false);
            }
        }

        public override void OnDenialReceived(DenialKind kind, ByteData? reason)
        {
            TDenial? decoded = null;
            try
            {
                if (kind == DenialKind.AuthenticatorRejected && reason.HasValue)
                {
                    using var packer = BitPackerPool.Get(reason.Value);
                    TDenial value = default;
                    Packer<TDenial>.Read(packer, ref value);
                    decoded = value;
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to decode denial reason for kind {kind}: {e.Message}");
            }

            try
            {
                OnDeniedByServer(kind, decoded);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"OnDeniedByServer override threw for kind {kind}: {e.Message}");
            }

            try
            {
                onDeniedByServer?.Invoke(kind, decoded);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"onDeniedByServer handler threw for kind {kind}: {e.Message}");
            }
        }

        /// <summary>
        /// Convenience override for client-side denial handling. Called before the
        /// <see cref="onDeniedByServer"/> event so subclasses can react in-place without
        /// wiring an event handler. Default body is empty.
        /// </summary>
        /// <param name="kind">Which denier rejected the connection.</param>
        /// <param name="reason"><c>null</c> for built-in deniers (no payload). Carries the decoded
        /// TDenial only when <paramref name="kind"/> is <see cref="DenialKind.AuthenticatorRejected"/>.</param>
        protected virtual void OnDeniedByServer(DenialKind kind, TDenial? reason)
        {
        }

        /// <summary>
        /// Get the client payload to be validated.
        /// This gets called when a new client connects and is then sent to the server for validation.
        /// </summary>
        /// <returns>The client payload to be validated.</returns>
        protected abstract Task<AuthenticationRequest<TRequest>> GetClientPayload();

        /// <summary>
        /// Once the client payload is received, this method is called to validate the payload.
        /// This only runs on the server. Return <c>success = false</c> with a populated
        /// <c>denialReason</c> to ship a typed rejection to the client.
        /// </summary>
        /// <param name="conn">The connection of the client.</param>
        /// <param name="payload">The client payload to be validated.</param>
        /// <returns>The result of the validation.</returns>
        protected abstract Task<AuthenticationResponse<TDenial>> ValidateClientPayload(Connection conn, TRequest payload);
    }
}
