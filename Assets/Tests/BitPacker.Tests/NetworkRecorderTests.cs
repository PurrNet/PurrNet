using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Recording;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class RecordedThing : NetworkBehaviour
{
    public SyncVar<int> health = new SyncVar<int>(100);
    public SyncVar<string> label = new SyncVar<string>("start");
    public SyncList<int> items = new SyncList<int>();
    [DontRecord] public SyncVar<int> secret = new SyncVar<int>(7);
}

public struct RecordedShot
{
    public int weapon;
    public Vector3 direction;
}

public struct RecordedPing : PurrNet.Packing.IPackedAuto
{
    public int value;
    public string text;
}

[DontRecord]
public struct RecordedSecretPing : PurrNet.Packing.IPackedAuto
{
    public int value;
}

public class RecordedActor : NetworkBehaviour
{
    public SyncDictionary<int, int> scores = new SyncDictionary<int, int>();
    public SyncHashSet<int> tags = new SyncHashSet<int>();
    public SyncQueue<int> orders = new SyncQueue<int>();
    public SyncArray<int> slots = new SyncArray<int>(3);
    public int shots, announced, hidden;

    [ServerRpc(requireOwnership: false)]
    public void Shoot(RecordedShot shot, string note) => shots++;

    [ObserversRpc]
    public void Announce(int winner, List<int> scores) => announced++;

    [ObserversRpc, DontRecord]
    public void Hidden(int value) => hidden++;
}

[DontRecord]
public class RecordedPlumbing : NetworkBehaviour
{
    public SyncVar<int> counter = new SyncVar<int>(1);
}

/// <summary>An extension with its own clock (like PurrDiction's prediction ticks), inert unless a test turns it on.</summary>
public class TestClockExtension : RecorderExtension, IRecordingContext
{
    public static bool enabled;
    public const ulong Offset = 100000;

    sealed class NoSource : RecordingSource
    {
        public override string name => "testclock";
        public override bool IsEntityObject(GameObject go) => false;
        public override Transform NearestEntity(Transform t, bool includeInactive) => null;
        public override bool HasNestedEntities(RecordedEntity entity) => false;
    }

    readonly NoSource _source = new NoSource();
    PurrNet.Modules.TickManager _ticks;

    public override RecordingSource source => _source;
    public override IRecordingContext clock => enabled ? this : null;

    NetworkManager manager => recorder.networkManager;
    public new bool isReady => manager && manager.tickModule != null;
    public ulong localTick => manager.tickModule.localTick + Offset;
    public double viewTick => localTick;
    public bool isServer => manager.isServer;
    public bool isClient => manager.isClient;
    public PlayerID? localPlayer => manager.isClient ? manager.localPlayer : (PlayerID?)null;
    public int tickRate => manager.tickModule.tickRate;

    public override void OnRecordingStarted()
    {
        if (!enabled)
            return;
        _ticks = manager.tickModule;
        _ticks.onPreTick += Tick;
    }

    public override void OnRecordingStopping()
    {
        if (_ticks != null)
            _ticks.onPreTick -= Tick;
        _ticks = null;
    }

    void Tick() => recorder.RecordTick(localTick);
}

public class RecordingMarker : MonoBehaviour { }

public class NetworkRecorderTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>A second source claiming marked objects, like PurrDiction's recorder on a networked object.</summary>
    sealed class MarkerSource : RecordingSource
    {
        public RecordingSession session;
        public readonly List<GameObject> claimed = new List<GameObject>();

        public override string name => "marker";
        public override bool IsEntityObject(GameObject go) => go.TryGetComponent<RecordingMarker>(out _);

        public override Transform NearestEntity(Transform t, bool includeInactive)
        {
            var marker = t.GetComponentInParent<RecordingMarker>(includeInactive);
            return marker ? marker.transform : null;
        }

        public override bool HasNestedEntities(RecordedEntity entity) => false;

        public override void RecordTick(ulong tick)
        {
            foreach (var go in claimed)
            {
                if (!go)
                    continue;
                if (!session.TryGetEntity(go, out var entity))
                {
                    entity = session.BeginSpawn(this, go, "marker");
                    session.CreateTransformStream(entity);
                    session.WriteMeta(RecordKind.Entity, session.json.Clear().BeginObject().Prop("entity", entity.id)
                        .Prop("tick", tick).Prop("name", session.Name(go)).Prop("transform", entity.transform.id).EndObject());
                    session.EndSpawn(entity, tick);
                }
                else if (!entity.Has(this))
                {
                    session.Attach(entity, this, "marker");
                    session.EndAttachRecord(session.BeginAttachRecord(entity, this, tick));
                }
                session.RecordTransform(entity, this, tick, out _);
            }
            session.DespawnUnseen(this, tick);
        }
    }

    readonly List<Object> _created = new List<Object>();
    NetworkManager _manager;
    NetworkRecorder _recorder;
    NetworkPrefabs _provider;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        TestClockExtension.enabled = false;
        if (_recorder && _recorder.isRecording)
            _recorder.StopRecording();
        if (_manager)
        {
            _manager.StopClient();
            _manager.StopServer();
            for (int i = 0; i < 10; i++)
                yield return null;
        }

#if UNITY_6000_4_OR_NEWER
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include))
#else
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#endif
            if (identity)
                Object.DestroyImmediate(identity.gameObject);
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    GameObject Prefab(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.AddComponent<RecordedThing>();
        go.AddComponent<BoxCollider>().size = new Vector3(1, 2, 3);
        var child = new GameObject(name + " child");
        child.transform.SetParent(go.transform, false);
        child.transform.localPosition = new Vector3(0, 1, 0);
        child.AddComponent<RecordedThing>();
        _created.Add(go);
        return go;
    }

    static IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 10f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    static IEnumerator Ticks(NetworkManager manager, int count)
    {
        var tickModule = manager.tickModule;
        uint until = tickModule.localTick + (uint)count;
        yield return WaitUntil(() => tickModule.localTick >= until);
    }

    GameObject _prefab, _plumbing;

    IEnumerator StartHost(bool recordOnServer)
    {
        var go = new GameObject("RecorderNetworkManager");
        go.SetActive(false);
        _created.Add(go);
        var transport = go.AddComponent<LocalTransport>();
        _manager = go.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;
        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        _manager.SetNetworkRules(rules);
        _provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        _provider.autoGenerate = false;
        _created.Add(_provider);
        _manager.SetPrefabProvider(_provider);
        _prefab = Prefab("Recorded");
        _provider.AddRuntimePrefab("recorded-thing", _prefab);
        _plumbing = new GameObject("Plumbing");
        _plumbing.SetActive(false);
        _plumbing.AddComponent<RecordedPlumbing>();
        _plumbing.AddComponent<SphereCollider>();
        _created.Add(_plumbing);
        _provider.AddRuntimePrefab("recorded-plumbing", _plumbing);
        _manager.transport = transport;

        NetworkRecorder.RegisterExtension<TestClockExtension>();
        _recorder = go.AddComponent<NetworkRecorder>();
        var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../test-results/recorder-tests"));
        Set(_recorder, "_recordOnServer", recordOnServer);
        Set(_recorder, "_recordInEditor", true);
        Set(_recorder, "_recordCamera", false);
        Set(_recorder, "_upload", false);
        Set(_recorder, "_directory", directory);
        Set(_recorder, "_networkManager", _manager);
        go.SetActive(true);

        _manager.StartHost();
        yield return WaitUntil(() => _manager.isServer && _manager.isClient && _manager.players.Count == 1);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RecordsIdentitiesSyncTypesAndSharedEntities()
    {
        yield return StartHost(false);
        var go = _manager.gameObject;
        var prefab = _prefab;

        // The static world is read when the recording starts, so the excluded object is there before.
        var plumbing = UnityProxy.Instantiate(_plumbing, new Vector3(-5, 0, 0), Quaternion.identity, go.scene);
        plumbing.SetActive(true);
        _manager.Spawn(plumbing);
        yield return WaitUntil(() => plumbing.GetComponent<RecordedPlumbing>().isSpawned);

        _recorder.StartRecording("network-recorder.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        // A sample written before the first recorded tick (a camera does this) must stay inside the first chunk.
        ulong early = _manager.tickModule.localTick;
        _recorder.WriteSample("early", 1f);
        var marker = new MarkerSource { session = _recorder.session };
        _recorder.session.AddSource(marker);

        var instance = UnityProxy.Instantiate(prefab, new Vector3(5, 0, 0), Quaternion.identity, go.scene);
        instance.SetActive(true);
        instance.AddComponent<RecordingMarker>();
        _manager.Spawn(instance);
        marker.claimed.Add(instance);
        var thing = instance.GetComponent<RecordedThing>();
        yield return WaitUntil(() => thing.isSpawned);
        yield return Ticks(_manager, 3);
        Assert.That((ulong)typeof(RecordingSession).GetField("_chunkFirstTick", Fields)!.GetValue(_recorder.session),
            Is.LessThanOrEqualTo(early), "the first chunk starts no later than the early sample");

        thing.health.value = 42;
        thing.label.value = "changed";
        thing.items.Add(7);
        thing.items.Add(8);
        yield return Ticks(_manager, 3);

        marker.claimed.Clear();
        yield return Ticks(_manager, 2);

        Object.Destroy(instance);
        yield return Ticks(_manager, 3);

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);

        var meta = ReadMeta(path);
        var entities = meta.FindAll(m => m.kind == RecordKind.Entity);
        var roots = entities.FindAll(m => m.json.Contains("\"name\":\"Recorded(Clone)\"") || m.json.Contains("\"name\":\"Recorded\""));
        Assert.That(roots.Count, Is.EqualTo(1), "one ENTITY for the spawned object, not one per source:\n" + Dump(meta));
        int rootId = EntityId(roots[0].json);
        var children = entities.FindAll(m => m.json.Contains("child"));
        Assert.That(children.Count, Is.EqualTo(1), "the nested identity is its own entity:\n" + Dump(meta));
        StringAssert.Contains($"\"parent\":{rootId}", children[0].json, "the nested entity points at its parent");

        // Extensions record before network identities, so the other source made the entity and PurrNet attached.
        Assert.That(meta.Exists(m => m.kind == RecordKind.Attach && m.json.Contains($"\"entity\":{rootId},") &&
                                     m.json.Contains("\"source\":\"purrnet\"") && m.json.Contains("\"field\":\"health\"")),
            "the network identity attached to the other source's entity:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.kind == RecordKind.Detach && m.json.Contains($"\"entity\":{rootId},") && m.json.Contains("\"source\":\"marker\"")),
            "the other source detached when it let go:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.kind == RecordKind.Despawn && m.json.StartsWith($"{{\"entity\":{rootId},")),
            "the entity despawned once the network identity was gone");

        var syncs = meta.FindAll(m => m.kind == RecordKind.Stream && m.json.Contains("\"kind\":\"sync\"") &&
                                      m.json.Contains($"\"entity\":{rootId},"));
        Assert.That(syncs.Count, Is.EqualTo(3), "health, label and items are sync streams:\n" + Dump(meta));
        foreach (var field in new[] { "health", "label", "items" })
            Assert.That(syncs.Exists(m => m.json.Contains($"\"field\":\"{field}\"")), field);
        Assert.That(meta.Exists(m => m.kind == RecordKind.Colliders && m.json.Contains($"\"entity\":{rootId},") &&
                                     m.json.Contains("\"shape\":\"box\"")), "the entity's collider is recorded");
        Assert.That(meta.Exists(m => m.kind == RecordKind.Player && m.json.Contains("\"event\":\"joined\"")), "the host player");

        Assert.That(meta.Exists(m => m.json.Contains("secret")), Is.False, "a [DontRecord] field has no stream:\n" + Dump(meta));
        Assert.That(entities.Exists(m => m.json.Contains("Plumbing")), Is.False, "a [DontRecord] identity is no entity:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.json.Contains("RecordedPlumbing") || m.json.Contains("counter")), Is.False,
            "nothing of a [DontRecord] identity is recorded:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.kind == RecordKind.Colliders && m.json.StartsWith("{\"entity\":null") &&
                                     m.json.Contains("\"name\":\"Plumbing")), "its collider stays part of the static world:\n" + Dump(meta));
    }

    [UnityTest]
    public IEnumerator AnExtensionsClockRunsTheRecordingAndAClockChangeStartsANewFile()
    {
        TestClockExtension.enabled = true;
        var finished = new List<string>();
        yield return StartHost(true);
        _recorder.onRecordingFinished += finished.Add;
        yield return WaitUntil(() => _recorder.isRecording);
        Assert.That(_recorder.isRecording, Is.True, "recording started on its own");
        string first = _recorder.currentPath;

        var instance = UnityProxy.Instantiate(_prefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
        _manager.Spawn(instance);
        yield return WaitUntil(() => instance.GetComponent<RecordedThing>().isSpawned);
        yield return Ticks(_manager, 4);
        // File names carry the second they started in.
        yield return new WaitForSecondsRealtime(1.2f);

        TestClockExtension.enabled = false;
        yield return WaitUntil(() => finished.Count == 1 && _recorder.isRecording);
        Assert.That(finished, Is.EqualTo(new[] { first }), "the clock change ended the first file");
        string second = _recorder.currentPath;
        Assert.That(second, Is.Not.EqualTo(first), "and a second one started");
        yield return Ticks(_manager, 4);
        _recorder.StopRecording();
        yield return WaitUntil(() => finished.Count == 2);

        ulong FirstTick(string path) => ulong.Parse(System.Text.RegularExpressions.Regex.Match(
            ReadMeta(path).Find(m => m.kind == RecordKind.Header).json, "\"firstTick\":(\\d+)").Groups[1].Value);
        var onClock = ReadMeta(first);
        Assert.That(FirstTick(first), Is.GreaterThanOrEqualTo(TestClockExtension.Offset), "the first file runs on the extension's ticks");
        var entity = onClock.Find(m => m.kind == RecordKind.Entity && m.json.Contains("\"name\":\"Recorded"));
        Assert.That(entity.json, Is.Not.Null, "network identities are recorded on the extension's ticks:\n" + Dump(onClock));
        Assert.That(ulong.Parse(System.Text.RegularExpressions.Regex.Match(entity.json, "\"tick\":(\\d+)").Groups[1].Value),
            Is.GreaterThanOrEqualTo(TestClockExtension.Offset));
        Assert.That(onClock.Exists(m => m.kind == RecordKind.Stream && m.json.Contains("\"field\":\"health\"")), "with their sync types");
        Assert.That(onClock.Exists(m => m.kind == RecordKind.Footer), "the first file was closed cleanly");

        var onNetwork = ReadMeta(second);
        Assert.That(FirstTick(second), Is.LessThan(TestClockExtension.Offset), "the second file runs on network ticks");
        Assert.That(onNetwork.Exists(m => m.kind == RecordKind.Entity && m.json.Contains("\"name\":\"Recorded")),
            "and has the object that was already spawned:\n" + Dump(onNetwork));
    }

    [UnityTest]
    public IEnumerator RecordsCollectionsOwnersObserversRpcsAndBroadcasts()
    {
        yield return StartHost(false);
        var actorPrefab = new GameObject("Actor");
        actorPrefab.SetActive(false);
        actorPrefab.AddComponent<RecordedActor>();
        var actorMesh = new Mesh { name = "ActorBody", vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
        _created.Add(actorMesh);
        actorPrefab.AddComponent<MeshFilter>().sharedMesh = actorMesh;
        actorPrefab.AddComponent<MeshRenderer>();
        _created.Add(actorPrefab);
        _provider.AddRuntimePrefab("recorded-actor", actorPrefab);

        int pings = 0;
        PlayerBroadcastDelegate<RecordedPing> onPing = (player, data, asServer) => pings++;
        PlayerBroadcastDelegate<RecordedSecretPing> onSecret = (player, data, asServer) => pings++;
        _manager.Subscribe(onPing, true);
        _manager.Subscribe(onSecret, true);
        _manager.Subscribe(onPing, false);

        _recorder.StartRecording("network-recorder-calls.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");

        var instance = UnityProxy.Instantiate(actorPrefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
        instance.SetActive(true);
        _manager.Spawn(instance);
        var actor = instance.GetComponent<RecordedActor>();
        yield return WaitUntil(() => actor.isSpawned);
        yield return Ticks(_manager, 3);

        actor.scores.Add(1, 10);
        actor.scores[1] = 25;
        actor.tags.Add(5);
        actor.orders.Enqueue(3);
        actor.slots[1] = 9;
        yield return Ticks(_manager, 2);

        var local = _manager.localPlayer;
        Assert.That(actor.owner, Is.EqualTo((PlayerID?)local), "the spawner owns it");
        actor.RemoveOwnership();
        actor.Shoot(new RecordedShot { weapon = 4, direction = new Vector3(0, 1, 0) }, "first");
        actor.Announce(77, new List<int> { 1, 2, 3 });
        actor.Hidden(13);
        _manager.SendToServer(new RecordedPing { value = 6, text = "up" });
        _manager.SendToServer(new RecordedSecretPing { value = 99 });
        _manager.Send(local, new RecordedPing { value = 8, text = "down" });
        yield return WaitUntil(() => actor.shots == 1 && actor.announced >= 1 && actor.hidden >= 1 && pings >= 3);
        Assert.That(actor.shots, Is.EqualTo(1), "the ServerRpc ran");
        Assert.That(pings, Is.GreaterThanOrEqualTo(3), "the broadcasts arrived");
        yield return Ticks(_manager, 3);

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        _manager.Unsubscribe(onPing, true);
        _manager.Unsubscribe(onSecret, true);
        _manager.Unsubscribe(onPing, false);

        var meta = ReadMeta(path);
        var streams = meta.FindAll(m => m.kind == RecordKind.Stream);
        foreach (var field in new[] { "scores", "tags", "orders", "slots" })
            Assert.That(streams.Exists(m => m.json.Contains("\"kind\":\"sync\"") && m.json.Contains($"\"field\":\"{field}\"")),
                field + " is a sync stream:\n" + Dump(meta));

        Assert.That(streams.FindAll(m => m.json.Contains("\"rpc\":\"RecordedActor.Shoot\"")).Count, Is.EqualTo(1),
            "one stream for the ServerRpc:\n" + Dump(meta));
        Assert.That(streams.Exists(m => m.json.Contains("\"rpc\":\"RecordedActor.Shoot\"") && m.json.Contains("\"rpcType\":\"ServerRPC\"")));
        Assert.That(streams.FindAll(m => m.json.Contains("\"rpc\":\"RecordedActor.Announce\"")).Count, Is.EqualTo(1),
            "one stream for the ObserversRpc:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.json.Contains("Hidden")), Is.False, "a [DontRecord] RPC is not recorded:\n" + Dump(meta));
        var types = meta.FindAll(m => m.kind == RecordKind.Type);
        Assert.That(types.Exists(m => m.json.Contains("\"name\":\"RecordedActor.Shoot\"") && m.json.Contains("[\"shot\",\"note\"]")),
            "the RPC's arguments are named");
        Assert.That(types.Exists(m => m.json.Contains("\"name\":\"RecordedActor.Announce\"") && m.json.Contains("[\"winner\",\"scores\"]")));

        Assert.That(streams.FindAll(m => m.json.Contains("\"broadcast\":\"RecordedPing\"")).Count, Is.EqualTo(1),
            "one stream for the broadcast type:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.json.Contains("RecordedSecretPing")), Is.False, "a [DontRecord] broadcast is not recorded");
        Assert.That(streams.Exists(m => m.json.Contains("\"kind\":\"broadcast\"") && m.json.Contains("PurrNet.")), Is.False,
            "PurrNet's own packets are not recorded as broadcasts:\n" + Dump(meta));
        Assert.That(streams.Exists(m => m.json.Contains("\"kind\":\"rpc\"") && m.json.Contains("\"rpc\":\"PurrNet.")), Is.False,
            "PurrNet's own RPCs are not recorded:\n" + Dump(meta));

        var entity = meta.Find(m => m.kind == RecordKind.Entity && m.json.Contains("\"name\":\"Actor"));
        Assert.That(entity.json, Is.Not.Null, "the actor is an entity:\n" + Dump(meta));
        int id = EntityId(entity.json);
        Assert.That(meta.Exists(m => m.kind == RecordKind.Owner && m.json.Contains($"\"entity\":{id},") &&
                                     m.json.Contains("\"owner\":null")), "the ownership change is recorded:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.kind == RecordKind.Observers && m.json.Contains($"\"entity\":{id},") &&
                                     m.json.Contains($"\"observers\":[{local.id.value}]")), "the observers are recorded:\n" + Dump(meta));
        Assert.That(meta.FindAll(m => m.kind == RecordKind.Renderers && m.json.Contains($"\"entity\":{id},")).Count, Is.EqualTo(1),
            "what the entity is drawn with is written once, not on every check:\n" + Dump(meta));
        Assert.That(meta.Exists(m => m.kind == RecordKind.Renderers && m.json.Contains("\"kind\":\"mesh\"") &&
                                     m.json.Contains("\"mesh\":\"ActorBody\"")), "with the mesh by name:\n" + Dump(meta));
        Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no recorder errors");
    }

    Mesh Grid(string name, int side, bool wide)
    {
        var vertices = new Vector3[side * side];
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                vertices[z * side + x] = new Vector3(x * 0.5f, Mathf.Sin(x * 0.3f + z * 0.7f), z * 0.5f);
        var first = new List<int>();
        var second = new List<int>();
        for (int z = 0; z < side - 1; z++)
            for (int x = 0; x < side - 1; x++)
            {
                int a = z * side + x;
                var into = z < side / 2 ? first : second;
                into.AddRange(new[] { a, a + side, a + 1, a + 1, a + side, a + side + 1 });
            }
        var mesh = new Mesh { name = name, indexFormat = wide ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = vertices;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(first, 0);
        mesh.SetTriangles(second, 1);
        mesh.RecalculateBounds();
        _created.Add(mesh);
        return mesh;
    }

    IEnumerator RecordMeshColliders(bool fromGpu, string file)
    {
        yield return StartHost(false);
        var meshes = new[] { Grid("SmallGrid", 20, false), Grid("LargeGrid", 300, true) };
        var prefabs = new List<GameObject>();
        foreach (var mesh in meshes)
        {
            mesh.UploadMeshData(false);
            var prefab = new GameObject(mesh.name + "Piece");
            prefab.SetActive(false);
            prefab.AddComponent<RecordedPlumbingFree>();
            prefab.AddComponent<MeshCollider>().sharedMesh = mesh;
            _created.Add(prefab);
            _provider.AddRuntimePrefab(prefab.name, prefab);
            prefabs.Add(prefab);
        }

        RecordingSession.forceGpuMeshRead = fromGpu;
        try
        {
            _recorder.StartRecording(file);
            Assert.That(_recorder.isRecording, Is.True, "recording started");
            foreach (var prefab in prefabs)
            {
                var instance = UnityProxy.Instantiate(prefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
                instance.SetActive(true);
                _manager.Spawn(instance);
            }
            yield return Ticks(_manager, 3);
            yield return WaitUntil(() => _recorder.session.pendingMeshes == 0);
            yield return Ticks(_manager, 25);
        }
        finally
        {
            RecordingSession.forceGpuMeshRead = false;
        }

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);

        var meta = ReadMeta(path);
        var recorded = ReadMeshes(path);
        foreach (var mesh in meshes)
        {
            Assert.That(recorded.ContainsKey(mesh.name), mesh.name + " was recorded: " + string.Join(", ", recorded.Keys) + "\n" + Dump(meta));
            var (id, vertices, indices) = recorded[mesh.name];
            Assert.That(vertices, Is.EqualTo(mesh.vertices), mesh.name + " vertices");
            Assert.That(indices, Is.EqualTo(mesh.triangles), mesh.name + " indices, sub-meshes in order");
            Assert.That(meta.Exists(m => m.kind == RecordKind.Colliders && m.json.Contains("\"shape\":\"mesh\"") && m.json.Contains($"\"mesh\":{id},")),
                mesh.name + " is what its collider refers to:\n" + Dump(meta));
        }
        Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no recorder errors");
    }

    [UnityTest]
    public IEnumerator ColliderMeshesAreRecordedExactly()
    {
        yield return RecordMeshColliders(false, "network-recorder-meshes.purrrec");
    }

    [UnityTest]
    public IEnumerator ColliderMeshesReadBackFromTheGpuAreRecordedExactly()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback)
            Assert.Ignore("needs a graphics device: run without -nographics");
        yield return RecordMeshColliders(true, "network-recorder-meshes-gpu.purrrec");
    }

    /// <summary>The MESH records of a recording by mesh name: id, vertices and triangle indices.</summary>
    static Dictionary<string, (int id, Vector3[] vertices, int[] indices)> ReadMeshes(string path)
    {
        var data = File.ReadAllBytes(path);
        var result = new Dictionary<string, (int, Vector3[], int[])>();
        ReadMeshes(data, 10, data.Length, result);
        return result;
    }

    static ulong VarUInt(byte[] data, ref int pos)
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
            shift += 7;
        }
    }

    static void ReadMeshes(byte[] data, int pos, int end, Dictionary<string, (int, Vector3[], int[])> into)
    {
        while (pos < end)
        {
            byte kind = data[pos++];
            int length = (int)VarUInt(data, ref pos);
            var payload = new byte[length];
            System.Array.Copy(data, pos, payload, 0, length);
            pos += length;
            if ((kind & 0x80) != 0)
            {
                using var input = new MemoryStream(payload);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                payload = output.ToArray();
            }
            var k = (RecordKind)(kind & 0x7F);
            if (k == RecordKind.Batch)
                ReadMeshes(payload, 0, payload.Length, into);
            else if (k == RecordKind.Mesh)
            {
                int at = 0;
                int id = (int)VarUInt(payload, ref at);
                int nameLength = (int)VarUInt(payload, ref at);
                string name = Encoding.UTF8.GetString(payload, at, nameLength);
                at += nameLength;
                var vertices = new Vector3[(int)VarUInt(payload, ref at)];
                for (int i = 0; i < vertices.Length; i++, at += 12)
                    vertices[i] = new Vector3(System.BitConverter.ToSingle(payload, at), System.BitConverter.ToSingle(payload, at + 4), System.BitConverter.ToSingle(payload, at + 8));
                var indices = new int[(int)VarUInt(payload, ref at)];
                for (int i = 0; i < indices.Length; i++, at += 4)
                    indices[i] = System.BitConverter.ToInt32(payload, at);
                Assert.That(at, Is.EqualTo(payload.Length), "the MESH record has nothing after its indices");
                into[name] = (id, vertices, indices);
            }
        }
    }

#if UNITY_EDITOR
    [UnityTest]
    public IEnumerator RecordsAnimationParticlesAndSound()
    {
        const string controllerPath = "Assets/__RecorderEffectsTest.controller";
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        foreach (var name in new[] { "IdleClip", "RunClip" })
        {
            var motion = new AnimationClip { name = name };
            motion.SetCurve("", typeof(Transform), "localScale.x", AnimationCurve.Linear(0, 1, 1, 1));
            UnityEditor.AssetDatabase.AddObjectToAsset(motion, controller);
            controller.AddMotion(motion);
        }
        var complaints = new List<string>();
        Application.LogCallback onLog = (text, stack, type) =>
        {
            if (type != LogType.Log)
                complaints.Add(text);
        };
        Application.logMessageReceived += onLog;
        try
        {
            yield return StartHost(false);
            var prefab = new GameObject("EffectActor");
            prefab.SetActive(false);
            prefab.AddComponent<RecordedPlumbingFree>();
            GameObject Child(string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(prefab.transform, false);
                return child;
            }
            Child("Body").AddComponent<Animator>().runtimeAnimatorController = controller;
            Child("Empty").AddComponent<Animator>();
            var main = Child("Muzzle").AddComponent<ParticleSystem>().main;
            main.playOnAwake = false;
            main.loop = false;
            var shot = AudioClip.Create("Shot", 44100, 1, 44100, false);
            _created.Add(shot);
            var gun = Child("Gun").AddComponent<AudioSource>();
            gun.playOnAwake = false;
            gun.clip = shot;
            _created.Add(prefab);
            _provider.AddRuntimePrefab(prefab.name, prefab);

            _recorder.StartRecording("network-recorder-effects.purrrec");
            Assert.That(_recorder.isRecording, Is.True, "recording started");
            var instance = UnityProxy.Instantiate(prefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
            instance.SetActive(true);
            _manager.Spawn(instance);
            var animator = instance.transform.Find("Body").GetComponent<Animator>();
            var particles = instance.GetComponentInChildren<ParticleSystem>();
            var source = instance.GetComponentInChildren<AudioSource>();
            yield return Ticks(_manager, 5);

            animator.CrossFadeInFixedTime("RunClip", 0.2f);
            particles.Play();
            source.Play();
            yield return Ticks(_manager, 12);
            bool audible = source.isPlaying;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            source.Stop();
            animator.Play("IdleClip", 0, 0f);
            yield return Ticks(_manager, 5);
            animator.gameObject.SetActive(false);
            yield return Ticks(_manager, 5);
            animator.gameObject.SetActive(true);
            yield return Ticks(_manager, 5);
            animator.enabled = false;
            yield return Ticks(_manager, 3);
            animator.enabled = true;
            yield return Ticks(_manager, 3);
            animator.runtimeAnimatorController = null;
            yield return Ticks(_manager, 3);
            animator.runtimeAnimatorController = controller;
            yield return Ticks(_manager, 3);

            string path = _recorder.currentPath;
            _recorder.StopRecording();
            yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);

            var meta = ReadMeta(path);
            var effects = meta.FindAll(m => m.kind == RecordKind.Effects);
            Assert.That(effects.Count, Is.EqualTo(1), "one EFFECTS record, for the one object that has any:\n" + Dump(meta));
            string json = effects[0].json;
            Assert.That(json, Does.Contain("\"name\":\"Body\",\"controller\":\"__RecorderEffectsTest\",\"layers\":1"), json);
            Assert.That(json, Does.Contain("\"name\":\"Empty\",\"controller\":null,\"layers\":0"), json);
            Assert.That(json, Does.Contain("\"name\":\"Muzzle\",\"loop\":false"), json);
            Assert.That(json, Does.Contain("\"name\":\"Gun\",\"clip\":\"Shot\""), json);
            var streams = meta.FindAll(m => m.kind == RecordKind.Stream);
            Assert.That(streams.FindAll(m => m.json.Contains("\"effect\":\"animation\"")).Count, Is.EqualTo(1), Dump(meta));
            Assert.That(streams.FindAll(m => m.json.Contains("\"effect\":\"particles\"")).Count, Is.EqualTo(1), Dump(meta));
            if (audible)
                Assert.That(streams.FindAll(m => m.json.Contains("\"effect\":\"audio\"")).Count, Is.EqualTo(1), Dump(meta));
            else
                Debug.Log("no audio device: the audio stream was not checked");
            Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no recorder errors");
            Assert.That(complaints, Is.Empty, "asking about effects never makes the engine complain");
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            UnityEditor.AssetDatabase.DeleteAsset(controllerPath);
        }
    }

    [UnityTest]
    public IEnumerator RecordsEffectsTheGameStartsItself()
    {
        const string controllerPath = "Assets/__RecorderEffectsTest.controller";
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var idle = new AnimationClip { name = "IdleClip" };
        idle.SetCurve("", typeof(Transform), "localScale.x", AnimationCurve.Linear(0, 1, 1, 1));
        UnityEditor.AssetDatabase.AddObjectToAsset(idle, controller);
        controller.AddMotion(idle);
        controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
        var complaints = new List<string>();
        Application.LogCallback onLog = (text, stack, type) =>
        {
            if (type != LogType.Log)
                complaints.Add(text);
        };
        Application.logMessageReceived += onLog;
        try
        {
            yield return StartHost(false);
            AudioClip Clip(string name)
            {
                var clip = AudioClip.Create(name, 22050, 1, 44100, false);
                _created.Add(clip);
                return clip;
            }
            var shot = Clip("Shot");
            var ricochet = Clip("Ricochet");
            var boom = Clip("Boom");

            var prefab = new GameObject("WovenActor");
            prefab.SetActive(false);
            prefab.AddComponent<RecordedPlumbingFree>();
            prefab.AddComponent<Animator>().runtimeAnimatorController = controller;
            var muzzleMain = prefab.AddComponent<ParticleSystem>().main;
            muzzleMain.playOnAwake = false;
            var gun = prefab.AddComponent<AudioSource>();
            gun.playOnAwake = false;
            gun.clip = shot;
            _created.Add(prefab);
            _provider.AddRuntimePrefab(prefab.name, prefab);

            var explosion = new GameObject("Explosion");
            explosion.SetActive(false);
            explosion.AddComponent<ParticleSystem>();
            var blast = new GameObject("Blast");
            blast.transform.SetParent(explosion.transform, false);
            blast.AddComponent<AudioSource>().clip = boom;
            _created.Add(explosion);

            var music = new GameObject("Music");
            _created.Add(music);
            var musicSource = music.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.clip = Clip("Theme");

            _recorder.StartRecording("network-recorder-woven.purrrec");
            Assert.That(_recorder.isRecording, Is.True, "recording started");
            var instance = UnityProxy.Instantiate(prefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
            instance.SetActive(true);
            _manager.Spawn(instance);
            var animator = instance.GetComponent<Animator>();
            var particles = instance.GetComponent<ParticleSystem>();
            var source = instance.GetComponent<AudioSource>();
            yield return Ticks(_manager, 4);

            source.PlayOneShot(ricochet, 0.5f);
            AudioSource.PlayClipAtPoint(boom, new Vector3(1, 2, 3));
            particles.Emit(5);
            animator.SetTrigger("Fire");
            musicSource.Play();
            var fx = Object.Instantiate(explosion, new Vector3(4, 5, 6), Quaternion.identity);
            fx.SetActive(true);
            _created.Add(fx);
            yield return Ticks(_manager, 6);
            Object.DestroyImmediate(fx);
            musicSource.Stop();
            yield return Ticks(_manager, 4);

            string path = _recorder.currentPath;
            _recorder.StopRecording();
            yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);

            var meta = ReadMeta(path);
            var effects = meta.FindAll(m => m.kind == RecordKind.Effects);
            Assert.That(effects.Exists(m => m.json.Contains("\"entity\":") && m.json.Contains("\"name\":\"WovenActor(Clone)\",\"clip\":\"Shot\"")),
                "the spawned object's effects:\n" + Dump(meta));
            var local = effects.Find(m => m.json.Contains("\"name\":\"Explosion(Clone)\""));
            Assert.That(local.json, Does.Contain("\"object\":").And.Contain("\"position\":[4,5,6]").And.Contain("\"name\":\"Blast\",\"clip\":\"Boom\""),
                "the instantiated effect is followed as an object of its own:\n" + Dump(meta));
            string localId = System.Text.RegularExpressions.Regex.Match(local.json, "\"object\":(\\d+)").Groups[1].Value;
            Assert.That(effects.Exists(m => m.json.Contains($"\"object\":{localId},") && m.json.Contains("\"gone\":true")),
                "and its end is recorded:\n" + Dump(meta));
            Assert.That(effects.Exists(m => m.json.Contains("\"object\":") && m.json.Contains("\"name\":\"Music\",\"clip\":\"Theme\"")),
                "a source in the scene is followed from the first call on it:\n" + Dump(meta));
            Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no recorder errors");
            Assert.That(complaints, Is.Empty, "nothing made the engine complain");
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            UnityEditor.AssetDatabase.DeleteAsset(controllerPath);
        }
    }
#endif

    /// <summary>
    /// The same motion, a function of the tick alone, recorded with the transforms read one by one on the main
    /// thread and with them read by a job: the files are compared outside (the poses must be the same rows).
    /// </summary>
    IEnumerator RecordMotion(bool job, string file)
    {
        yield return StartHost(false);
        var prefab = new GameObject("Mover");
        prefab.SetActive(false);
        prefab.AddComponent<RecordedPlumbingFree>();
        _created.Add(prefab);
        _provider.AddRuntimePrefab(prefab.name, prefab);

        var arm = new GameObject("Arm");
        _created.Add(arm);
        arm.transform.localScale = new Vector3(1, 2, 0.5f);

        int from = RecordingSession.transformJobFrom;
        RecordingSession.transformJobFrom = job ? 4 : int.MaxValue;
        RecordingSession.checkTransformJob = job;
        var tickModule = _manager.tickModule;
        var movers = new List<Transform>();
        uint first = 0;
        System.Action move = () =>
        {
            float t = (tickModule.localTick - first) * 0.05f;
            arm.transform.rotation = Quaternion.Euler(0, t * 90, t * 30);
            for (int i = 0; i < movers.Count; i++)
            {
                var m = movers[i];
                if (!m)
                    continue;
                if (i % 4 == 0)
                    m.position = new Vector3(i, Mathf.Sin(t + i), t);
                if (i % 4 == 1)
                    m.rotation = Quaternion.Euler(t * 40, i * 10, 0);
                if (i % 4 == 2)
                    m.localScale = Vector3.one * (1 + Mathf.Min(t, 0.5f));
            }
        };
        try
        {
            _recorder.StartRecording(file);
            Assert.That(_recorder.isRecording, Is.True, "recording started");
            for (int i = 0; i < 16; i++)
            {
                var instance = UnityProxy.Instantiate(prefab, new Vector3(i, 0, 0), Quaternion.identity, _manager.gameObject.scene);
                if (i >= 12)
                    instance.transform.SetParent(arm.transform, false);
                instance.SetActive(true);
                _manager.Spawn(instance);
                movers.Add(instance.transform);
            }
            first = tickModule.localTick;
            tickModule.onPreTick += move;
            yield return Ticks(_manager, 20);
            Object.Destroy(movers[5].gameObject);
            yield return Ticks(_manager, 20);
        }
        finally
        {
            tickModule.onPreTick -= move;
            RecordingSession.transformJobFrom = from;
            RecordingSession.checkTransformJob = false;
        }

        string path = _recorder.currentPath;
        long byJob = _recorder.session.transformsReadByJob;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        var meta = ReadMeta(path);
        Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""),
            "what the job read is what the main thread reads at the same moment");
        if (job)
            Assert.That(byJob, Is.GreaterThan(300), "the poses came from the job");
        else
            Assert.That(byJob, Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator PosesAreReadOnTheMainThreadInSmallScenes()
    {
        yield return RecordMotion(false, "network-recorder-poses-direct.purrrec");
    }

    [UnityTest]
    public IEnumerator PosesReadByAJobAreTheOnesTheMainThreadReads()
    {
        yield return RecordMotion(true, "network-recorder-poses-job.purrrec");
    }

    [UnityTest]
    public IEnumerator WhatTheGameDoesToAnObjectIsRecordedWhenItDoesIt()
    {
        yield return StartHost(false);
        var prefab = new GameObject("Changing");
        prefab.SetActive(false);
        prefab.AddComponent<RecordedPlumbingFree>();
        var bodyPart = new GameObject("Body");
        bodyPart.transform.SetParent(prefab.transform, false);
        bodyPart.AddComponent<MeshFilter>().sharedMesh = Grid("ChangingBody", 4, false);
        bodyPart.AddComponent<MeshRenderer>();
        var hitboxPart = new GameObject("Hitbox");
        hitboxPart.transform.SetParent(prefab.transform, false);
        hitboxPart.AddComponent<BoxCollider>();
        _created.Add(prefab);
        _provider.AddRuntimePrefab(prefab.name, prefab);

        _recorder.StartRecording("network-recorder-changes.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        var instance = UnityProxy.Instantiate(prefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
        instance.SetActive(true);
        _manager.Spawn(instance);
        var body = instance.transform.Find("Body").gameObject;
        var hitbox = instance.transform.Find("Hitbox").GetComponent<BoxCollider>();
        var tickModule = _manager.tickModule;
        yield return Ticks(_manager, 3);
        Assert.That(_recorder.session.TryGetEntity(instance, out var entity), Is.True, "the object is in the recording");
        Assert.That(entity.lastPose.visible, Is.EqualTo(1), "it shows");

        yield return Ticks(_manager, 80);
        Assert.That(entity.checkRounds, Is.GreaterThan(1), "an object nothing happens to is checked less often");

        body.SetActive(false);
        yield return Ticks(_manager, 2);
        Assert.That(entity.lastPose.visible, Is.EqualTo(0), "hidden when the game deactivated its body");
        Assert.That(entity.checkRounds, Is.LessThanOrEqualTo(2), "and it is checked often again");
        body.SetActive(true);
        yield return Ticks(_manager, 2);
        Assert.That(entity.lastPose.visible, Is.EqualTo(1), "shown when the game activated it");
        var renderer = body.GetComponent<MeshRenderer>();
        renderer.enabled = false;
        yield return Ticks(_manager, 2);
        Assert.That(entity.lastPose.visible, Is.EqualTo(0), "hidden when the game disabled its renderer");
        renderer.enabled = true;
        yield return Ticks(_manager, 2);
        Assert.That(entity.lastPose.visible, Is.EqualTo(1));

        uint disabledAt = tickModule.localTick;
        hitbox.enabled = false;
        yield return Ticks(_manager, 3);

        yield return Ticks(_manager, 30);
        uint hiddenAt = tickModule.localTick;
        typeof(GameObject).GetMethod(nameof(GameObject.SetActive))!.Invoke(body, new object[] { false });
        yield return WaitUntil(() => entity.lastPose.visible == 0, 5f);
        Assert.That(entity.lastPose.visible, Is.EqualTo(0), "found without being told");
        Assert.That(tickModule.localTick - hiddenAt, Is.LessThanOrEqualTo(13), "within one round of looks");

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        var meta = ReadMeta(path);
        var colliders = meta.FindAll(m => m.kind == RecordKind.Colliders && m.json.StartsWith("{\"entity\":" + entity.id + ","));
        Assert.That(colliders.Count, Is.EqualTo(2), "its colliders when it appeared, and without the disabled one:\n" + Dump(meta));
        Assert.That(colliders[1].json, Does.Contain("\"colliders\":[]"), colliders[1].json);
        uint recordedAt = uint.Parse(System.Text.RegularExpressions.Regex.Match(colliders[1].json, "\"tick\":(\\d+)").Groups[1].Value);
        Assert.That(recordedAt - disabledAt, Is.LessThanOrEqualTo(2), "the collider the game disabled is gone on the tick it did");
        Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no recorder errors");
    }

    static int EntityId(string json)
    {
        int start = json.IndexOf("\"entity\":", System.StringComparison.Ordinal) + 9;
        int end = start;
        while (end < json.Length && char.IsDigit(json[end]))
            end++;
        return int.Parse(json.Substring(start, end - start));
    }

    internal static string Dump(List<(RecordKind kind, string json)> meta)
    {
        var text = new StringBuilder();
        foreach (var m in meta)
            if (m.kind != RecordKind.Type)
                text.Append(m.kind).Append(' ').Append(m.json.Length > 300 ? m.json.Substring(0, 300) : m.json).Append('\n');
        return text.ToString();
    }

    static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Fields)!.SetValue(target, value);

    /// <summary>The JSON metadata records of a recording, in file order (batches expanded).</summary>
    internal static List<(RecordKind kind, string json)> ReadMeta(string path)
    {
        var data = File.ReadAllBytes(path);
        var result = new List<(RecordKind, string)>();
        Read(data, 10, data.Length, result);
        return result;
    }

    static void Read(byte[] data, int pos, int end, List<(RecordKind, string)> into)
    {
        while (pos < end)
        {
            byte kind = data[pos++];
            ulong length = 0;
            int shift = 0;
            while (true)
            {
                byte b = data[pos++];
                length |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    break;
                shift += 7;
            }
            var payload = new byte[(int)length];
            System.Array.Copy(data, pos, payload, 0, (int)length);
            pos += (int)length;
            if ((kind & 0x80) != 0)
            {
                using var input = new MemoryStream(payload);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                payload = output.ToArray();
            }
            var k = (RecordKind)(kind & 0x7F);
            if (k == RecordKind.Batch)
                Read(payload, 0, payload.Length, into);
            else if (k != RecordKind.Chunk && k != RecordKind.Mesh)
                into.Add((k, Encoding.UTF8.GetString(payload)));
        }
    }
}
