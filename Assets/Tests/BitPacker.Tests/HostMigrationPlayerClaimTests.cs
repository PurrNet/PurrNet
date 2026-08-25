using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;

public sealed class HostMigrationPlayerClaimTests
{
    [Test]
    public void OrdinaryLoginResponseHasNoMigrationFenceFields()
    {
        var response = new ServerLoginResponse(
            new PlayerID(7, false),
            new NetworkID(23, new PlayerID(7, false)),
            "application-cookie");

        Assert.That(response.playerId, Is.EqualTo(new PlayerID(7, false)));
        Assert.That(response.cookie, Is.EqualTo("application-cookie"));
        Assert.That(typeof(ServerLoginResponse).GetProperty("hostMigrationSessionId"), Is.Null);
        Assert.That(typeof(ServerLoginResponse).GetProperty("hostMigrationEpoch"), Is.Null);
    }

    [Test]
    public void SessionAdvertisementCarriesOnlyTheMigrationFence()
    {
        var advertisement = new HostMigrationSessionAdvertisement
        {
            sessionId = "room-incarnation",
            epoch = 4
        };

        Assert.That(advertisement.sessionId, Is.EqualTo("room-incarnation"));
        Assert.That(advertisement.epoch, Is.EqualTo(4));
    }

    [Test]
    public void ClaimCarriesOnlyScopedMigrationIdentity()
    {
        var player = new PlayerID(7, false);
        var claim = new HostMigrationPlayerClaim
        {
            sessionId = "room-incarnation",
            epoch = 4,
            playerId = player
        };

        Assert.That(claim.sessionId, Is.EqualTo("room-incarnation"));
        Assert.That(claim.epoch, Is.EqualTo(4));
        Assert.That(claim.playerId, Is.EqualTo(player));
    }

    [Test]
    public void PlayerJoinEventRetainsOptionalCookieWireSlot()
    {
        const string cookie = "opt-in-application-cookie";
        var joined = new PlayerJoinedEvent(
            new PlayerID(7, false),
            new PurrNet.Transports.Connection(11),
            new NetworkID(23, new PlayerID(7, false)), cookie);

        Assert.That(joined.cookie, Is.EqualTo(cookie));
    }
}
