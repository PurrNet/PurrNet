using UnityEngine;

namespace PurrNet.HostMigration
{
    [CreateAssetMenu(menuName = "PurrNet/Host Migration/Static Endpoint Provider")]
    public sealed class StaticHostMigrationEndpointProvider : HostMigrationEndpointProvider
    {
        [SerializeField] private HostMigrationEndpoint _endpoint = new HostMigrationEndpoint("127.0.0.1", 7777);

        public override bool TryGetEndpoint(HostMigrationContext context, PlayerID promotedPlayer,
            out HostMigrationEndpoint endpoint)
        {
            endpoint = _endpoint;
            return endpoint.isValid;
        }
    }
}
