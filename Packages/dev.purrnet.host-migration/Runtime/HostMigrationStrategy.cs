using UnityEngine;

namespace PurrNet.HostMigration
{
    public abstract class HostMigrationStrategy : ScriptableObject
    {
        public abstract bool TrySelectPromotedPlayer(HostMigrationContext context, out PlayerID promotedPlayer);

        public static bool TrySelectLowestClientPlayer(HostMigrationContext context, out PlayerID promotedPlayer)
        {
            promotedPlayer = default;

            if (context == null)
                return false;

            for (int i = 0; i < context.players.Count; i++)
            {
                var player = context.players[i];

                if (player.isServer)
                    continue;

                if (promotedPlayer.isServer || player.id.value < promotedPlayer.id.value)
                    promotedPlayer = player;
            }

            return !promotedPlayer.isServer;
        }
    }
}
