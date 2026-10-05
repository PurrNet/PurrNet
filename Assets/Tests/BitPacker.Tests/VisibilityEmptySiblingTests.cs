using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class VisibilityEmptySiblingTests
{
    private static readonly FieldInfo SiblingsField =
        typeof(NetworkIdentity).GetField("_siblingIdentities", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly List<GameObject> _objects = new List<GameObject>();
    private readonly List<(PlayerID player, Transform scope, bool visible)> _changes =
        new List<(PlayerID, Transform, bool)>();
    private VisilityV2 _visibility;
    private static readonly PlayerID Player = new PlayerID(1, false);

    [SetUp]
    public void SetUp()
    {
        var managerObject = CreateObject("VisibilityManager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        _visibility = new VisilityV2(manager);
        _visibility.visibilityChanged += (player, scope, visible) => _changes.Add((player, scope, visible));
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i])
                UnityEngine.Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        _changes.Clear();
    }

    [Test]
    public void SiblingCacheIsExcludedFromSerialization()
    {
        Assert.IsNotNull(SiblingsField);
        Assert.That(SiblingsField.IsNotSerialized, Is.True,
            "A serialized cache comes back from a domain reload as an empty array instead of null.");
    }

    [Test]
    public void StaleEmptySiblingCacheIsRecomputedSoTheScopeBecomesVisible()
    {
        var root = CreateIdentity("Root");
        var stacked = root.gameObject.AddComponent<NetworkIdentity>();
        SiblingsField.SetValue(root, Array.Empty<NetworkIdentity>());

        _visibility.RefreshVisibilityForGameObject(Player, root);

        Assert.That(root.IsObserver(Player), Is.True);
        Assert.That(stacked.IsObserver(Player), Is.True);
        Assert.That(_changes, Is.EqualTo(new[] { (Player, root.transform, true) }));
    }

    private GameObject CreateObject(string name)
    {
        var result = new GameObject(name);
        _objects.Add(result);
        return result;
    }

    private NetworkIdentity CreateIdentity(string name)
    {
        return CreateObject(name).AddComponent<NetworkIdentity>();
    }
}
