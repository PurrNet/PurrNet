using System;
using UnityEngine;

namespace PurrNet.HostMigration
{
    [Serializable]
    public struct HostMigrationEndpointEntry
    {
        [SerializeField] private ulong _playerId;
        [SerializeField] private HostMigrationEndpoint _endpoint;

        public ulong playerId => _playerId;

        public HostMigrationEndpoint endpoint => _endpoint;
    }

    [CreateAssetMenu(menuName = "PurrNet/Host Migration/Endpoint Table Provider")]
    public sealed class HostMigrationEndpointTableProvider : HostMigrationEndpointProvider
    {
        [SerializeField] private HostMigrationEndpointEntry[] _entries = Array.Empty<HostMigrationEndpointEntry>();
        [SerializeField] private bool _useFallbackEndpoint;
        [SerializeField] private HostMigrationEndpoint _fallbackEndpoint = new HostMigrationEndpoint("127.0.0.1", 7777);

        public override bool TryGetEndpoint(HostMigrationContext context, PlayerID promotedPlayer,
            out HostMigrationEndpoint endpoint)
        {
            var promotedId = promotedPlayer.id.value;

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].playerId != promotedId)
                    continue;

                endpoint = _entries[i].endpoint;
                return endpoint.isValid;
            }

            endpoint = _fallbackEndpoint;
            return _useFallbackEndpoint && endpoint.isValid;
        }
    }
}
