namespace PurrNet
{
    /// <summary>
    /// Decides whether a target should send to a given player on a given tick.
    /// The default implementation uses the profile's fixed send intervals with per-target staggering.
    /// </summary>
    public interface ILODScheduler
    {
        bool ShouldSendThisTick(ILODTarget target, NetworkLODProfile profile, PlayerID player, byte tier, uint tick);
    }

    public sealed class LODIntervalScheduler : ILODScheduler
    {
        public static readonly LODIntervalScheduler instance = new LODIntervalScheduler();

        public bool ShouldSendThisTick(ILODTarget target, NetworkLODProfile profile, PlayerID player, byte tier, uint tick)
        {
            int interval = profile ? profile.GetSendIntervalTicks(tier) : 1;
            if (interval <= 1)
                return true;
            return (tick + target.staggerSeed) % interval == 0;
        }
    }
}
