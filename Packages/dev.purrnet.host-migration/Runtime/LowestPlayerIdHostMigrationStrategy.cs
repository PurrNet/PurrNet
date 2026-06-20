using UnityEngine;

namespace PurrNet.HostMigration
{
    [CreateAssetMenu(menuName = "PurrNet/Host Migration/Lowest Player ID Strategy")]
    public sealed class LowestPlayerIdHostMigrationStrategy : HostMigrationStrategy
    {
        public override bool TrySelectPromotedPlayer(HostMigrationContext context, out PlayerID promotedPlayer)
        {
            return TrySelectLowestClientPlayer(context, out promotedPlayer);
        }
    }
}
