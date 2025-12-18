using System;
using System.Collections.Generic;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet.Modules
{
    internal struct WaitingConnectionAuth
    {
        public Connection conn;
        public float addedTimeStamp;
    }

    public class AuthModule : INetworkModule, IConnectionListener, IFixedUpdate, IPromoteToServerModule
    {
        private readonly NetworkManager _manager;
        private readonly BroadcastModule _broadcastModule;
        private readonly CookiesModule _cookiesModule;
        private PlayersManager _playersManager;

        private AuthenticationLayer _authenticator;
        private readonly List<WaitingConnectionAuth> _waitingConnections = new List<WaitingConnectionAuth>();

        public event Action<Connection, AuthenticationResponse> onConnection;

        public AuthModule(NetworkManager manager, BroadcastModule broadcastModule, CookiesModule cookiesModule)
        {
            _cookiesModule = cookiesModule;
            _manager = manager;
            _broadcastModule = broadcastModule;
        }

        public void SetPlayerModule(PlayersManager players)
        {
            _playersManager = players;
        }

        public void PromoteToServerModule()
        {
            Enable(true);
        }

        public void PostPromoteToServerModule()
        {
        }

        public void Enable(bool asServer)
        {
            _authenticator = _manager.authenticator;

            if (!asServer)
                return;

            _broadcastModule.Subscribe<AuthenticationRequest>(OnNonAuthRequest);

            if (!_authenticator)
                return;

            _authenticator.onAuthenticationComplete += OnAuthenticationComplete;
            _authenticator.Subscribe(_broadcastModule, _playersManager);
        }

        public void Disable(bool asServer)
        {
            if (!asServer)
                return;

            _broadcastModule.Unsubscribe<AuthenticationRequest>(OnNonAuthRequest);

            if (!_authenticator)
                return;

            _authenticator.onAuthenticationComplete -= OnAuthenticationComplete;
            _authenticator.Unsubscribe(_broadcastModule, _playersManager);
        }

        public void OnConnected(Connection conn, bool asServer)
        {
            if (!asServer)
            {
                if (_authenticator)
                {
                    _authenticator.SendClientPayload(_broadcastModule, _cookiesModule);
                }
                else
                {
                    var cookie = _cookiesModule.GetOrSet("client_connection_session", Guid.NewGuid().ToString());
                    _broadcastModule.SendToServer(new AuthenticationRequest
                    {
                        cookie = cookie,
                        version = NetworkManager.version
                    });
                }

                return;
            }

            _waitingConnections.Add(new WaitingConnectionAuth
            {
                conn = conn,
                addedTimeStamp = Time.time
            });
        }

        public void OnDisconnected(Connection conn, bool asServer)
        {
        }

        private void OnNonAuthRequest(Connection conn, AuthenticationRequest data, bool asserver)
        {
            if (!asserver)
                return;

            RemoveFromWaitingList(conn);

            if (_authenticator)
            {
                PurrLogger.LogError("Authenticator is enabled, but non-auth request received");
                _manager.CloseConnection(conn);
                return;
            }

            if (!NetworkManager.VerifyVersion(data.version))
            {
                PurrLogger.LogError($"Client version mismatch. Client version: {data.version}, Server version: {NetworkManager.version}");
                _manager.CloseConnection(conn);
                return;
            }

            onConnection?.Invoke(conn, new AuthenticationResponse
            {
                success = true,
                cookie = data.cookie
            });
        }

        private void OnAuthenticationComplete(Connection conn, AuthenticationResponse response)
        {
            RemoveFromWaitingList(conn);

            if (!response.success)
            {
                PurrLogger.LogError($"Authentication failed for conn `{conn}`");
                _manager.CloseConnection(conn);
                return;
            }

            onConnection?.Invoke(conn, response);
        }

        private void RemoveFromWaitingList(Connection conn)
        {
            for (int i = _waitingConnections.Count - 1; i >= 0; i--)
            {
                var waitingConnection = _waitingConnections[i];
                if (waitingConnection.conn != conn)
                    continue;

                _waitingConnections.RemoveAt(i);
            }
        }

        public void FixedUpdate()
        {
            if (!_authenticator)
                return;

            for (int i = _waitingConnections.Count - 1; i >= 0; i--)
            {
                var waitingConnection = _waitingConnections[i];
                if (Time.time - waitingConnection.addedTimeStamp > _authenticator.timeout)
                {
                    PurrLogger.LogError($"Authentication failed for conn `{waitingConnection.conn}` (timed out)");
                    _waitingConnections.RemoveAt(i);
                    _manager.CloseConnection(waitingConnection.conn);
                }
            }
        }
    }
}
