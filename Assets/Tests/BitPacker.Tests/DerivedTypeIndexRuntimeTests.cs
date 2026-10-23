using System;
using System.Linq;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Utils;

public sealed class DerivedTypeIndexRuntimeTests
{
    private sealed class Player : PlayerIdentity<Player> { }
    private static bool IsPlayer(Type t) => !t.IsAbstract && t.BaseType != null &&
        t.BaseType.IsGenericType && t.BaseType.GetGenericTypeDefinition() == typeof(PlayerIdentity<>);

    [Test]
    public void CompiledIndexesMatchLegacyDiscoveryOrder()
    {
        foreach (var assembly in new[] { typeof(NetworkManager).Assembly, typeof(Player).Assembly })
        {
            var indexed = DerivedTypeDiscovery.ReadIndex(assembly, DerivedTypeDiscovery.PlayerIdentityIndexTypeName);
            Assert.That(indexed, Is.Not.Null, assembly.FullName);
            CollectionAssert.AreEqual(assembly.GetTypes().Where(IsPlayer).ToArray(), indexed.Where(IsPlayer).ToArray());
        }
        Assert.That(DerivedTypeDiscovery.ReadIndex(typeof(Player).Assembly,
            DerivedTypeDiscovery.PlayerIdentityIndexTypeName).Contains(typeof(Player)), Is.True);
    }

    private static class UnmarkedIndex { public static Type[] GetTypes() => Type.EmptyTypes; }
    [GeneratedByIL]
    private static class BrokenIndex { public static Type[] GetTypes() => throw new InvalidOperationException(); }

    [Test]
    public void MissingUnmarkedAndBrokenIndexesRetainReflectionFallback()
    {
        var assembly = typeof(Player).Assembly;
        Assert.That(DerivedTypeDiscovery.ReadIndex(assembly, "Missing.Index"), Is.Null);
        Assert.That(DerivedTypeDiscovery.ReadIndex(assembly, typeof(UnmarkedIndex).FullName), Is.Null);
        Assert.That(DerivedTypeDiscovery.ReadIndex(assembly, typeof(BrokenIndex).FullName), Is.Null);
    }
}
