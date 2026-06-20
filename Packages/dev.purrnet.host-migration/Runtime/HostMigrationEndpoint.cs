using System;
using UnityEngine;

namespace PurrNet.HostMigration
{
    [Serializable]
    public struct HostMigrationEndpoint
    {
        [SerializeField] private string _address;
        [SerializeField] private ushort _port;

        public HostMigrationEndpoint(string address, ushort port)
        {
            _address = address;
            _port = port;
        }

        public string address => _address;

        public ushort port => _port;

        public bool isValid => !string.IsNullOrWhiteSpace(_address) && _port != 0;

        public override string ToString()
        {
            return isValid ? $"{_address}:{_port}" : "<unset>";
        }
    }
}
