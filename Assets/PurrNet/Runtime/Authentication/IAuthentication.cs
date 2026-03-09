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

        public abstract void Subscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players);

        public abstract void Unsubscribe(INetworkManager manager, BroadcastModule broadcastModule, PlayersManager players);

        public abstract void SendClientPayload(BroadcastModule broadcastModule, CookiesModule cookies);

        protected abstract void UnAuthenticateClient(Connection conn);

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
                payload.cookie ??= cookies.GetOrSet("client_connection_session", Guid.NewGuid().ToString());
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
}
