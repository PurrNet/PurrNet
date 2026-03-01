using System;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class LogServerConnectionState : MonoBehaviour
{
    [SerializeField] private NetworkManager _manager;

    private void Awake()
    {
        _manager.onServerConnectionState += OnServerConnectionState;
        _manager.onClientConnectionState += OnClientConnectionState;
    }

    private void OnClientConnectionState(ConnectionState obj)
    {
        //Debug.Log($"Client connection state changed: {obj}");
    }

    private void OnServerConnectionState(ConnectionState obj)
    {
        //Debug.Log($"Server connection state changed: {obj}");
    }
}
