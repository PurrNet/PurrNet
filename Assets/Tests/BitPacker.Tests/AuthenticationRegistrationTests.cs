using NUnit.Framework;
using PurrNet;
using PurrNet.Authentication;
using PurrNet.Packing;
using PurrNet.Pooling;

// Deliberately a game-owned payload inside a runtime-owned generic envelope.
public struct RegistrationAuthPayload
{
    public ulong steamId;
    public string ticket;
}

[RegisterNetworkType(typeof(AuthenticationRequest<RegistrationAuthPayload>))]
[RegisterNetworkType(typeof(DisposableList<ulong>))]
public class AuthenticationRegistrationTests
{
    [OneTimeSetUp]
    public void Register() => NetworkManager.CallAllRegisters();

    [Test]
    public void GameAssemblyRegistersClosedAuthenticationRequest()
    {
        Assert.That(Packer<AuthenticationRequest<RegistrationAuthPayload>>.HasPacker(), Is.True);
        var request = new AuthenticationRequest<RegistrationAuthPayload>(new RegistrationAuthPayload
        {
            steamId = 76561198000000000UL,
            ticket = "example-ticket"
        }) { cookie = "reconnect-cookie" };

        using var packer = BitPackerPool.Get();
        Packer<AuthenticationRequest<RegistrationAuthPayload>>.Write(packer, request);
        packer.ResetPositionAndMode(true);
        var copy = Packer<AuthenticationRequest<RegistrationAuthPayload>>.Read(packer);

        Assert.That(copy.cookie, Is.EqualTo(request.cookie));
        Assert.That(copy.payload.steamId, Is.EqualTo(request.payload.steamId));
        Assert.That(copy.payload.ticket, Is.EqualTo(request.payload.ticket));
        var duplicated = PurrCopy<AuthenticationRequest<RegistrationAuthPayload>>.Copy(request);
        Assert.That(duplicated.payload.ticket, Is.EqualTo(request.payload.ticket));
    }

    [Test]
    public void AttributeRegisteredValueTypeContainerHasValidCodec()
    {
        Assert.That(Packer<DisposableList<ulong>>.HasPacker(), Is.True);
        using var source = DisposableList<ulong>.Create(1);
        source.Add(123UL);
        using var packer = BitPackerPool.Get();
        Packer<DisposableList<ulong>>.Write(packer, source);
        packer.ResetPositionAndMode(true);
        using var copy = Packer<DisposableList<ulong>>.Read(packer);
        CollectionAssert.AreEqual(source, copy);
    }
}
