using System.Threading;
using PurrNet;

public enum NetworkRole
{
    Server,
    Client,
    Host
}

public struct ScenarioContext
{
    public NetworkRole role;
    public int expectedConnections;
    public NetworkManager networkManager;
    public CancellationToken cancellationToken;

    public float benchSeconds;
    public bool measured;

    public bool isServer => role is NetworkRole.Server or NetworkRole.Host;
    public bool isClient => role is NetworkRole.Client or NetworkRole.Host;
}
