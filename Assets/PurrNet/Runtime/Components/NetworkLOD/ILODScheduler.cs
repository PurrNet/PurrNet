namespace PurrNet
{
    /// <summary>
    /// Decides whether an identity should send to a given player on a given tick.
    /// The default implementation uses the profile's fixed send intervals with per-identity staggering.
    /// </summary>
    public interface ILODScheduler
    {
        bool ShouldSendThisTick(NetworkLOD lod, PlayerID player, byte tier, uint tick);
    }

    public sealed class LODIntervalScheduler : ILODScheduler
    {
        public static readonly LODIntervalScheduler instance = new LODIntervalScheduler();

        public bool ShouldSendThisTick(NetworkLOD lod, PlayerID player, byte tier, uint tick)
        {
            var profile = lod.profile;
            int interval = profile ? profile.GetSendIntervalTicks(tier) : 1;
            if (interval <= 1)
                return true;
            return (tick + lod.staggerOffset) % interval == 0;
        }
    }
}
