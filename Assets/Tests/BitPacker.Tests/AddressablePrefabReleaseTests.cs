#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Loading and releasing single addressable prefabs: the composite must never serve a prefab its
/// provider released, and the usage tracker must only release what nothing needs.
/// </summary>
public class AddressablePrefabReleaseTests
{
    const string GUID_A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string GUID_B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    /// <summary>
    /// Stands in for Addressables: "loading" hands out a fresh GameObject, "releasing" destroys it.
    /// </summary>
    sealed class FakeAddressableProvider : IAddressablePrefabProvider
    {
        readonly List<string> _guids = new();
        readonly Dictionary<string, GameObject> _loaded = new();
        public readonly List<string> releases = new();
        public int loads;

        public event Action<string, bool> loadStateChanged;

        public FakeAddressableProvider(params string[] guids) => _guids.AddRange(guids);

        PrefabData Data(int id) => new PrefabData
        {
            prefabId = id,
            prefab = _loaded.TryGetValue(_guids[id], out var prefab) ? prefab : null
        };

        public IEnumerable<PrefabData> allPrefabs
        {
            get
            {
                for (int i = 0; i < _guids.Count; i++)
                    yield return Data(i);
            }
        }

        public IEnumerable<string> persistentIds => _guids;

        public bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            bool known = prefabId >= 0 && prefabId < _guids.Count;
            prefabData = known ? Data(prefabId) : default;
            return known;
        }

        public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            for (int i = 0; i < _guids.Count; i++)
            {
                if (_loaded.TryGetValue(_guids[i], out var loaded) && loaded == prefab)
                {
                    prefabData = Data(i);
                    return true;
                }
            }

            prefabData = default;
            return false;
        }

        public void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5) { }

        public void Refresh() { }

        public bool NeedsLoad(int prefabId) => TryGetPrefabData(prefabId, out var data) && !data.prefab;

        public Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!TryGetPrefabData(prefabId, out var data))
                return Task.FromResult(default(PrefabData));

            if (!data.prefab)
            {
                loads++;
                var prefab = new GameObject(_guids[prefabId]);
                prefab.AddComponent<NetworkIdentity>();
                _loaded[_guids[prefabId]] = prefab;
                loadStateChanged?.Invoke(_guids[prefabId], true);
            }

            return Task.FromResult(Data(prefabId));
        }

        public Task<PrefabData> LoadPrefabByGuidAsync(string assetGuid) => LoadPrefabAsync(_guids.IndexOf(assetGuid));

        public bool TryGetAddressableGuid(int prefabId, out string assetGuid) => TryGetPersistentId(prefabId, out assetGuid);

        public IEnumerable<string> GetLoadedGuids() => _loaded.Keys;

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            bool known = prefabId >= 0 && prefabId < _guids.Count;
            persistentId = known ? _guids[prefabId] : null;
            return known;
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            persistentId = null;
            return TryGetPrefabData(prefab, out var data) && TryGetPersistentId((int)data.prefabId, out persistentId);
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            return TryGetPrefabData(_guids.IndexOf(persistentId), out prefabData);
        }

        public bool ReleasePrefabByGuid(string assetGuid)
        {
            if (!_loaded.Remove(assetGuid, out var prefab))
                return false;

            Object.DestroyImmediate(prefab);
            releases.Add(assetGuid);
            loadStateChanged?.Invoke(assetGuid, false);
            return true;
        }

        public bool IsLoaded(string guid) => _loaded.ContainsKey(guid);

        public void Dispose()
        {
            foreach (var prefab in _loaded.Values)
                Object.DestroyImmediate(prefab);
            _loaded.Clear();
        }
    }

    readonly List<Object> _created = new();
    FakeAddressableProvider _addressables;
    NetworkPrefabs _regular;
    CompositePrefabProvider _composite;
    NetworkManager _manager;
    float _now;

    [SetUp]
    public void SetUp()
    {
        _now = 0f;
        _regular = ScriptableObject.CreateInstance<NetworkPrefabs>();
        _created.Add(_regular);

        var regularPrefab = new GameObject("Regular");
        regularPrefab.AddComponent<NetworkIdentity>();
        _created.Add(regularPrefab);
        _regular.AddRuntimePrefab("regular", regularPrefab);

        _addressables = new FakeAddressableProvider(GUID_A, GUID_B);

        _composite = new CompositePrefabProvider();
        _composite.AddProvider(_regular);
        _composite.AddProvider(_addressables);
        _composite.Refresh();
    }

    [TearDown]
    public void TearDown()
    {
        _addressables.Dispose();

        for (int i = 0; i < _created.Count; i++)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
        _manager = null;
    }

    int IdOf(string guid)
    {
        Assert.That(_composite.TryGetPrefabDataByPersistentId(guid, out var data), Is.True);
        return (int)data.prefabId;
    }

    AddressableUsageTracker CreateTracker(float releaseDelay)
    {
        var go = new GameObject("Manager");
        go.SetActive(false);
        _created.Add(go);
        _manager = go.AddComponent<NetworkManager>();

        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);

        var field = typeof(NetworkRules).GetField("_addressableRules", BindingFlags.Instance | BindingFlags.NonPublic);
        var addressableRules = (AddressableRules)field.GetValue(rules);
        addressableRules.releaseWhenLastDespawned = true;
        addressableRules.releaseDelay = releaseDelay;
        field.SetValue(rules, addressableRules);

        _manager.SetNetworkRules(rules);
        _manager.SetPrefabProvider(_composite);

        var tracker = _manager.addressableUsage;
        tracker.clock = () => _now;
        return tracker;
    }

    NetworkIdentity Spawn(string guid)
    {
        Assert.That(_composite.TryGetPrefabDataByPersistentId(guid, out var data) && data.prefab, Is.True);

        var instance = Object.Instantiate(data.prefab);
        _created.Add(instance);
        NetworkManager.SetupPrefabInfo(instance, data.prefabId, false);
        return instance.GetComponent<NetworkIdentity>();
    }

    [Test]
    public void AddressableIdsFollowTheRegularOnes()
    {
        Assert.That(IdOf(GUID_A), Is.EqualTo(1));
        Assert.That(IdOf(GUID_B), Is.EqualTo(2));
        Assert.That(_composite.TryGetAddressableGuid(2, out var guid) && guid == GUID_B, Is.True);
        Assert.That(_composite.TryGetAddressableGuid(0, out _), Is.False);
    }

    [Test]
    public void LoadByGuidReturnsTheNetworkId()
    {
        Assert.That(_composite.NeedsLoad(2), Is.True);

        var loaded = _composite.LoadPrefabByGuidAsync(GUID_B).Result;

        Assert.That((int)loaded.prefabId, Is.EqualTo(2));
        Assert.That(loaded.prefab, Is.Not.Null);
        Assert.That(_composite.NeedsLoad(2), Is.False);
        Assert.That(_composite.TryGetPrefabData(loaded.prefab, out var byReference), Is.True);
        Assert.That((int)byReference.prefabId, Is.EqualTo(2));
    }

    [Test]
    public void ReleasedPrefabIsNoLongerServed()
    {
        var first = _composite.LoadPrefabByGuidAsync(GUID_A).Result.prefab;
        Assert.That(_composite.TryGetPrefabData(first, out _), Is.True);

        Assert.That(_composite.ReleasePrefabByGuid(GUID_A), Is.True);

        Assert.That(_composite.NeedsLoad(1), Is.True);
        Assert.That(_composite.TryGetPrefabData(1, out var data), Is.True);
        Assert.That(data.prefab == null, Is.True);
        Assert.That(_composite.ReleasePrefabByGuid(GUID_A), Is.False);

        var second = _composite.LoadPrefabByGuidAsync(GUID_A).Result.prefab;
        Assert.That(second, Is.Not.Null);
        Assert.That(_composite.TryGetPrefabData(second, out var reloaded), Is.True);
        Assert.That((int)reloaded.prefabId, Is.EqualTo(1));
    }

    [Test]
    public void LoadStateEventsReachCompositeSubscribers()
    {
        var events = new List<(string, bool)>();
        Action<string, bool> handler = (guid, loaded) => events.Add((guid, loaded));

        _composite.loadStateChanged += handler;
        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();
        _composite.ReleasePrefabByGuid(GUID_A);
        _composite.loadStateChanged -= handler;
        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();

        Assert.That(events, Is.EqualTo(new[] { (GUID_A, true), (GUID_A, false) }));
    }

    [Test]
    public void PrefabIsReleasedAfterItsLastInstanceIsGone()
    {
        var tracker = CreateTracker(releaseDelay: 5f);
        tracker.Retain(GUID_A);
        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();

        var first = Spawn(GUID_A);
        var second = Spawn(GUID_A);
        tracker.OnIdentityAdded(first, true);
        tracker.OnIdentityAdded(second, true);
        tracker.Release(GUID_A);

        tracker.OnIdentityRemoved(first, true);
        _now = 100f;
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.True, "one instance is still spawned");

        tracker.OnIdentityRemoved(second, true);
        _now = 104f;
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.True, "the release delay hasn't passed");

        _now = 105.5f;
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.False);
        Assert.That(_addressables.releases, Is.EqualTo(new[] { GUID_A }));
    }

    [Test]
    public void SpawningAgainCancelsAPendingRelease()
    {
        var tracker = CreateTracker(releaseDelay: 5f);
        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();

        var first = Spawn(GUID_A);
        tracker.OnIdentityAdded(first, true);
        tracker.OnIdentityRemoved(first, true);

        _now = 3f;
        var second = Spawn(GUID_A);
        tracker.OnIdentityAdded(second, true);

        _now = 60f;
        tracker.Update();

        Assert.That(_addressables.IsLoaded(GUID_A), Is.True);
        Assert.That(tracker.IsInUse(GUID_A), Is.True);
    }

    [Test]
    public void HostCountsAnIdentityOncePerSide()
    {
        var tracker = CreateTracker(releaseDelay: 0f);
        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();

        var identity = Spawn(GUID_A);
        tracker.OnIdentityAdded(identity, true);
        tracker.OnIdentityAdded(identity, false);
        tracker.OnIdentityAdded(identity, false);

        tracker.OnIdentityRemoved(identity, true);
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.True, "the client side still has it");

        tracker.OnIdentityRemoved(identity, false);
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.False);
    }

    [Test]
    public void RetainedPrefabStaysLoadedWhileUnused()
    {
        var tracker = CreateTracker(releaseDelay: 0f);
        tracker.Retain(GUID_A);
        tracker.Retain(GUID_A);
        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();

        _now = 1000f;
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.True);

        tracker.Release(GUID_A);
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.True, "one hold is left");

        tracker.Release(GUID_A);
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_A), Is.False);
    }

    [Test]
    public void PrefabLoadedButNeverUsedIsReleasedAfterAGracePeriod()
    {
        var tracker = CreateTracker(releaseDelay: 0f);
        _composite.LoadPrefabByGuidAsync(GUID_B).Wait();

        _now = 5f;
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_B), Is.True, "a spawn for it may still be on its way");

        _now = 11f;
        tracker.Update();
        Assert.That(_addressables.IsLoaded(GUID_B), Is.False);
    }

    [Test]
    public void NothingIsReleasedWhenTheRuleIsOff()
    {
        var tracker = CreateTracker(releaseDelay: 0f);

        var field = typeof(NetworkRules).GetField("_addressableRules", BindingFlags.Instance | BindingFlags.NonPublic);
        var addressableRules = (AddressableRules)field.GetValue(_manager.networkRules);
        addressableRules.releaseWhenLastDespawned = false;
        field.SetValue(_manager.networkRules, addressableRules);

        _composite.LoadPrefabByGuidAsync(GUID_A).Wait();
        var identity = Spawn(GUID_A);
        tracker.OnIdentityAdded(identity, true);
        tracker.OnIdentityRemoved(identity, true);

        _now = 1000f;
        tracker.Update();

        Assert.That(_addressables.IsLoaded(GUID_A), Is.True);
    }
}
#endif
