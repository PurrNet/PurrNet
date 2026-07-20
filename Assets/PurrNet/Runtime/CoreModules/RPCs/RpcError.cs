namespace PurrNet
{
    public enum RpcError : byte
    {
        None = 0,
        TargetServerNotAllowed = 1,
        RequireOwnership = 2,
        NotObserver = 3,
        ServerRequired = 4,
        Unknown = 255,
    }
}
