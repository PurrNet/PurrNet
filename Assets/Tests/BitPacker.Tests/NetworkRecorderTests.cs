using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PurrNet;
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
    public bool isReady => manager && manager.tickModule != null;
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

        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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

    static int EntityId(string json)
    {
        int start = json.IndexOf("\"entity\":", System.StringComparison.Ordinal) + 9;
        int end = start;
        while (end < json.Length && char.IsDigit(json[end]))
            end++;
        return int.Parse(json.Substring(start, end - start));
    }

    static string Dump(List<(RecordKind kind, string json)> meta)
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
    static List<(RecordKind kind, string json)> ReadMeta(string path)
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
