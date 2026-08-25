using NUnit.Framework;
using PurrNet.Transports;

public sealed class PurrTransportAuthenticationSerializationTests
{
    [Test]
    public void OrdinaryAuthenticationOmitsProvisionalHostSecret()
    {
        var json = PurrTransport.SerializeClientAuthenticate(
            "room", "client-secret", true, false, "unused-host-secret");

        Assert.That(json, Does.Contain("\"roomName\":\"room\""));
        Assert.That(json, Does.Contain("\"clientSecret\":\"client-secret\""));
        Assert.That(json, Does.Contain("\"nat\":true"));
        Assert.That(json, Does.Not.Contain("provisionalHostSecret"));
    }

    [Test]
    public void PreparedMigrationAuthenticationIncludesProvisionalHostSecret()
    {
        var json = PurrTransport.SerializeClientAuthenticate(
            "room", "client-secret", false, true, "host-secret");

        Assert.That(json, Does.Contain("\"roomName\":\"room\""));
        Assert.That(json, Does.Contain("\"clientSecret\":\"client-secret\""));
        Assert.That(json, Does.Contain("\"nat\":false"));
        Assert.That(json, Does.Contain("\"provisionalHostSecret\":\"host-secret\""));
    }
}
