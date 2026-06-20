using UnityEngine;

namespace PurrNet.HostMigration
{
    public abstract class HostMigrationEndpointProvider : ScriptableObject
    {
        public abstract bool TryGetEndpoint(HostMigrationContext context, PlayerID promotedPlayer,
            out HostMigrationEndpoint endpoint);
    }
}
