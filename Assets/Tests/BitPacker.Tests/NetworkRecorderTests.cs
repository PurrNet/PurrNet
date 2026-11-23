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

public struct RecordedFiller
{
    public string text;
}

public sealed class FailingRecordingSource : RecordingSource
{
    public const string Message = "the recorder broke on purpose";
    public int asked;

    public override string name => "failing";
    public override bool IsEntityObject(GameObject go) => false;
    public override Transform NearestEntity(Transform t, bool includeInactive) => null;
    public override bool HasNestedEntities(RecordedEntity entity) => false;

    public override void RecordTick(ulong tick)
    {
        asked++;
        throw new System.InvalidOperationException(Message);
    }
}

public sealed class UnwritableFile : FileStream
{
    public UnwritableFile(string path) : base(path, FileMode.Create, FileAccess.Write) { }

    public override void Flush() => throw new IOException("no space left on the device");
}

/// <summary>A recordings service on this machine that takes what the uploader sends: ticket, PUT, complete.</summary>
public sealed class FakeRecordingsSite : System.IDisposable
{
    readonly System.Net.HttpListener _listener = new System.Net.HttpListener();
    readonly Dictionary<string, byte[]> _puts = new Dictionary<string, byte[]>();
    readonly List<string> _tickets = new List<string>();
    readonly List<byte[]> _completed = new List<byte[]>();
    readonly Dictionary<string, double> _putSeconds = new Dictionary<string, double>();
    readonly List<double> _seconds = new List<double>();
    int _next;

    public readonly string endpoint;

    public FakeRecordingsSite()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        string root = $"http://localhost:{port}/";
        _listener.Prefixes.Add(root);
        _listener.Start();
        endpoint = root + "recordings";
        _listener.BeginGetContext(OnRequest, null);
    }

    public int completed
    {
        get
        {
            lock (_puts)
                return _completed.Count;
        }
    }

    public byte[] Completed(int index)
    {
        lock (_puts)
            return _completed[index];
    }

    public string Ticket(int index)
    {
        lock (_puts)
            return _tickets[index];
    }

    /// <summary>How long the file's bytes took to arrive, from its request to its last byte.</summary>
    public double Seconds(int index)
    {
        lock (_puts)
            return _seconds[index];
    }

    void OnRequest(System.IAsyncResult result)
    {
        System.Net.HttpListenerContext context;
        try
        {
            context = _listener.EndGetContext(result);
            _listener.BeginGetContext(OnRequest, null);
        }
        catch (System.Exception)
        {
            return;
        }

        var request = context.Request;
        var body = new MemoryStream();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        request.InputStream.CopyTo(body);
        double took = clock.Elapsed.TotalSeconds;
        string path = request.Url.AbsolutePath;
        string answer = "{}";
        lock (_puts)
        {
            if (request.HttpMethod == "POST" && path.EndsWith("/uploads"))
            {
                string id = (++_next).ToString();
                _tickets.Add(Encoding.UTF8.GetString(body.ToArray()));
                answer = "{\"skip\":false,\"id\":\"" + id + "\",\"url\":\"" + endpoint + "/put/" + id +
                         "\",\"contentType\":\"application/octet-stream\"}";
            }
            else if (request.HttpMethod == "PUT")
            {
                string id = path.Substring(path.LastIndexOf('/') + 1);
                _puts[id] = body.ToArray();
                _putSeconds[id] = took;
            }
            else if (request.HttpMethod == "POST" && path.EndsWith("/complete"))
            {
                string[] segments = path.Split('/');
                if (_puts.TryGetValue(segments[segments.Length - 2], out var bytes))
                {
                    _completed.Add(bytes);
                    _seconds.Add(_putSeconds[segments[segments.Length - 2]]);
                }
                answer = "{\"status\":\"complete\"}";
            }
        }

        var reply = Encoding.UTF8.GetBytes(answer);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = reply.Length;
        context.Response.OutputStream.Write(reply, 0, reply.Length);
        context.Response.Close();
    }

    public void Dispose() => _listener.Close();
}

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
    public IEnumerator ALongRecordingIsSplitIntoPiecesThatEachStandAlone() => SplitsIntoPieces(false);

    [UnityTest]
    public IEnumerator ARecordingOnAnExtensionsClockIsSplitTheSameWay() => SplitsIntoPieces(true);

    IEnumerator SplitsIntoPieces(bool onAnExtensionsClock)
    {
        TestClockExtension.enabled = onAnExtensionsClock;
        yield return StartHost(false);
        var wall = new GameObject("SplitWall");
        wall.AddComponent<BoxCollider>();
        _created.Add(wall);
        var instance = UnityProxy.Instantiate(_prefab, new Vector3(2, 0, 0), Quaternion.identity, _manager.gameObject.scene);
        instance.SetActive(true);
        _manager.Spawn(instance);
        yield return WaitUntil(() => instance.GetComponent<RecordedThing>().isSpawned);

        Set(_recorder, "_splitMinutes", 20f / (60f * _manager.tickModule.tickRate));
        var finished = new List<string>();
        var closed = new List<bool>();
        _recorder.onRecordingFinished += path =>
        {
            finished.Add(path);
            closed.Add(ReadMeta(path).Exists(m => m.kind == RecordKind.Footer));
        };
        _recorder.StartRecording("network-recorder-split.purrrec");
        string first = _recorder.currentPath;
        bool recording = true;
        yield return WaitUntil(() =>
        {
            recording &= _recorder.isRecording;
            return _recorder.splits >= 2;
        }, 30f);
        Assert.That(_recorder.splits, Is.GreaterThanOrEqualTo(2), "the recording was split");
        Assert.That(recording, Is.True, "without a frame in which nothing was being recorded");
        yield return Ticks(_manager, 3);
        _recorder.StopRecording();
        int pieces = _recorder.splits + 1;
        yield return WaitUntil(() => finished.Count >= pieces);
        Assert.That(finished.Count, Is.EqualTo(pieces), "every piece was reported as finished, once");
        Assert.That(closed, Has.None.False, "and was complete and closed when it was");

        static string Text(string json, string key) =>
            System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\":\"([^\"]*)\"").Groups[1].Value;
        static ulong Number(string json, string key) =>
            ulong.Parse(System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\":(\\d+)").Groups[1].Value);

        string session = null;
        ulong lastTick = 0;
        var ids = new HashSet<string>();
        for (int i = 0; i < pieces; i++)
        {
            Assert.That(finished[i], Is.EqualTo(i == 0 ? first : first.Replace(".purrrec", $"-{i + 1}.purrrec")),
                "pieces are finished in order, and named after the first with their number");
            var meta = ReadMeta(finished[i]);
            string header = meta.Find(m => m.kind == RecordKind.Header).json;
            string footer = meta.Find(m => m.kind == RecordKind.Footer).json;
            string id = Text(header, "recordingId");
            session ??= id;
            Assert.That(ids.Add(id), Is.True, "every piece is a recording with an id of its own");
            Assert.That(Text(header, "session"), Is.EqualTo(session), "that names the session: the first piece's id");
            Assert.That(Number(header, "segment"), Is.EqualTo((ulong)i), "and which piece of it this is");
            ulong firstTick = Number(header, "firstTick");
            Assert.That(firstTick >= TestClockExtension.Offset, Is.EqualTo(onAnExtensionsClock), "every piece runs on the clock the recording started on");
            if (i > 0)
                Assert.That(firstTick, Is.EqualTo(lastTick + 1), "a piece goes on with the tick after the last one's");
            lastTick = Number(footer, "lastTick");
            Assert.That(footer.Contains("\"continued\":true"), Is.EqualTo(i < pieces - 1), "only the last piece ends the session:\n" + footer);
            if (i < pieces - 1)
                Assert.That(Number(footer, "ticks"), Is.InRange(19UL, 22UL), "a piece is as long as asked for");

            Assert.That(meta.Exists(m => m.kind == RecordKind.Entity && m.json.Contains("\"name\":\"Recorded")),
                $"piece {i + 1} declares the object that was there all along:\n" + Dump(meta));
            Assert.That(meta.Exists(m => m.kind == RecordKind.Stream && m.json.Contains("\"field\":\"health\"")), "and its sync types");
            Assert.That(meta.Exists(m => m.kind == RecordKind.Player && m.json.Contains("\"event\":\"joined\"")), "and its players");
            Assert.That(meta.Exists(m => m.kind == RecordKind.Colliders && m.json.Contains($"\"entity\":null,\"tick\":{firstTick},") &&
                                         m.json.Contains("\"name\":\"SplitWall")),
                $"piece {i + 1} has the static world from its first tick:\n" + Dump(meta));
        }
    }

    [UnityTest]
    public IEnumerator ADedicatedServerSendsEachPieceWhileItKeepsRunning()
    {
        yield return StartHost(false);
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../test-results/recorder-upload-" + System.Guid.NewGuid().ToString("N").Substring(0, 8)));
        Set(_recorder, "_directory", directory);
        const string prefs = "PurrDiction.RecordingUploader";
        string savedPrefs = PlayerPrefs.GetString(prefs, "");
        bool wasServer = RecordingUploader.dedicatedServer, wasSending = RecordingUploader.sendWhileServing;
        double wasShare = RecordingUploader.sendingShare;
        var site = new FakeRecordingsSite();
        var random = new System.Random(7);
        var text = new StringBuilder();
        uint filled = 0;
        void Fill()
        {
            uint tick = _manager.tickModule.localTick;
            if (tick == filled || !_recorder.isRecording)
                return;
            filled = tick;
            text.Clear();
            for (int i = 0; i < 8192; i++)
                text.Append((char)('0' + random.Next(64)));
            _recorder.WriteSample("filler", new RecordedFiller { text = text.ToString() });
        }
        try
        {
            RecordingUploader.Configure(site.endpoint, "test-key", directory, 1L << 30);
            RecordingUploader.dedicatedServer = true;
            RecordingUploader.sendWhileServing = false;
            RecordingUploader.sendingShare = 0.5;
            Set(_recorder, "_splitMinutes", 20f / (60f * _manager.tickModule.tickRate));
            _recorder.StartRecording("upload-split.purrrec");
            string first = _recorder.currentPath;
            yield return WaitUntil(() =>
            {
                Fill();
                return _recorder.splits >= 2;
            }, 30f);
            Assert.That(_recorder.splits, Is.GreaterThanOrEqualTo(2), "the recording was split");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(site.completed, Is.EqualTo(0), "with sending while serving turned off, nothing is sent while the server runs");
            Assert.That(File.Exists(first), Is.True, "and what is finished waits on disk");

            RecordingUploader.sendWhileServing = true;
            bool online = true;
            yield return WaitUntil(() =>
            {
                Fill();
                online &= _manager.isServer && _recorder.isRecording;
                return site.completed >= 2;
            }, 30f);
            Assert.That(site.completed, Is.GreaterThanOrEqualTo(2), "the finished pieces were sent");
            Assert.That(online, Is.True, "while the server stayed up and went on recording");
            yield return WaitUntil(() => !File.Exists(first));
            Assert.That(File.Exists(first), Is.False, "a piece that was sent is deleted");

            string writing = _recorder.session.recordingId;
            _recorder.StopRecording();
            int pieces = _recorder.splits + 1;
            yield return WaitUntil(() => site.completed >= pieces, 30f);
            yield return WaitUntil(() => Directory.GetFiles(directory).Length == 0);
            Assert.That(site.completed, Is.EqualTo(pieces), "every piece is sent once, the last one when it ends");
            Assert.That(_manager.isServer, Is.True, "and the server is still up");
            Assert.That(Directory.GetFiles(directory), Is.Empty, "nothing is left on disk");

            static string Id(string json) =>
                System.Text.RegularExpressions.Regex.Match(json, "\"recordingId\":\"([^\"]*)\"").Groups[1].Value;
            var sent = new HashSet<string>();
            int ends = 0, lastAt = -1;
            for (int i = 0; i < pieces; i++)
            {
                var meta = ReadMeta(site.Completed(i));
                string header = meta.Find(m => m.kind == RecordKind.Header).json;
                string footer = meta.Find(m => m.kind == RecordKind.Footer).json;
                Assert.That(footer, Is.Not.Null, "what is sent is a whole file, with its footer:\n" + Dump(meta));
                Assert.That(sent.Add(Id(header)), Is.True, "no piece is sent twice");
                Assert.That(Id(site.Ticket(i)), Is.EqualTo(Id(header)), "under its own id");
                Assert.That(site.Ticket(i), Does.Contain("\"kind\":\"main\""));
                if (!footer.Contains("\"continued\":true"))
                {
                    ends++;
                    lastAt = i;
                }
                else
                {
                    int bytes = site.Completed(i).Length;
                    Assert.That(bytes, Is.GreaterThan(64 * 1024), "a piece of this test is big enough to see how fast it is sent");
                    Assert.That(site.Seconds(i), Is.GreaterThan(0.2),
                        $"a piece of a second, {bytes / 1024} KiB, is spread over half a second (the share set here), not sent at once");
                }
                if (Id(header) == writing)
                    Assert.That(i, Is.GreaterThanOrEqualTo(2), "the piece that was still being written was not among the first sent");
            }
            Assert.That(ends, Is.EqualTo(1), "one piece ends the session");
            Assert.That(sent.Contains(writing), Is.True, "the last piece was sent after it ended");
            Assert.That(lastAt, Is.EqualTo(pieces - 1), "after the others");
        }
        finally
        {
            RecordingUploader.dedicatedServer = wasServer;
            RecordingUploader.sendWhileServing = wasSending;
            RecordingUploader.sendingShare = wasShare;
            foreach (var uploader in Resources.FindObjectsOfTypeAll<RecordingUploader>())
                Object.DestroyImmediate(uploader.gameObject);
            if (string.IsNullOrEmpty(savedPrefs))
                PlayerPrefs.DeleteKey(prefs);
            else
                PlayerPrefs.SetString(prefs, savedPrefs);
            site.Dispose();
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
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

    /// <summary>
    /// What the recorder allocates for an object it has not seen before, by what the object is made of. Run it by
    /// name; the numbers go to test-local/recorder-cost/alloc-probe.txt.
    /// </summary>
    [UnityTest, Explicit]
    public IEnumerator WhatANewObjectAllocates([Values(0, 1, 2, 3, 4, 5)] int variant)
    {
        yield return StartHost(false);
        var prefab = new GameObject("Probe" + variant);
        prefab.SetActive(false);
        prefab.AddComponent<RecordedPlumbingFree>();
        string what = "one identity, nothing else";
        if (variant == 1 || variant == 5)
        {
            for (int i = 0; i < 3; i++)
                prefab.AddComponent<RecordedPlumbingFree>();
            what = "+ 3 identities without sync fields";
        }
        if (variant == 2 || variant == 5)
        {
            prefab.AddComponent<RecordedThing>();
            what = "+ 1 identity with 3 recorded sync fields";
        }
        if (variant == 3 || variant == 5)
        {
            for (int i = 0; i < 5; i++)
            {
                var part = new GameObject("Hit" + i);
                part.transform.SetParent(prefab.transform, false);
                part.transform.localPosition = new Vector3(0f, i, 0f);
                part.AddComponent<BoxCollider>();
            }
            what = "+ 5 child box colliders";
        }
        if (variant == 4 || variant == 5)
        {
            var mesh = Grid("ProbeBody", 4, false);
            for (int i = 0; i < 3; i++)
            {
                var part = new GameObject("Body" + i);
                part.transform.SetParent(prefab.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>();
            }
            what = "+ 3 child mesh renderers";
        }
        if (variant == 5)
            what = "all of the above";
        _created.Add(prefab);
        _provider.AddRuntimePrefab(prefab.name, prefab);

        _recorder.session.stats.enabled = true;
        _recorder.StartRecording("alloc-probe-" + variant + ".purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        yield return Ticks(_manager, 5);
        const int count = 200;
        for (int i = 0; i < count; i++)
        {
            var instance = UnityProxy.Instantiate(prefab, new Vector3(i * 2f, 0f, 0f), Quaternion.identity, _manager.gameObject.scene);
            instance.SetActive(true);
            _manager.Spawn(instance);
            if (i % 10 == 9)
                yield return Ticks(_manager, 1);
        }
        yield return Ticks(_manager, 120);
        _recorder.StopRecording();
        string summary = _recorder.DescribeLastRecording();

        var line = new StringBuilder(what).Append(": per object");
        foreach (string section in new[] { "tick", "identities", "spawn", "managed", "colliders", "checks", "effects", "poses" })
        {
            var match = System.Text.RegularExpressions.Regex.Match(summary, "\\n\\s+" + section + "\\s+ticks=[^\\n]*gcAlloc=([0-9.]+)KiB");
            double bytes = match.Success ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 1024.0 / count : 0;
            line.Append(' ').Append(section).Append('=').Append(bytes.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('B');
        }
        Directory.CreateDirectory("test-local/recorder-cost");
        File.AppendAllText("test-local/recorder-cost/alloc-probe.txt", line.Append('\n').ToString());
        Debug.Log(line.ToString());
    }

    [UnityTest]
    public IEnumerator TheStaticWorldIsRecordedAsWhatChanged()
    {
        yield return StartHost(false);
        var world = new List<GameObject>();
        for (int i = 0; i < 8; i++)
        {
            var box = new GameObject("StaticBox" + i);
            box.transform.position = new Vector3(i * 3f, 0f, 20f);
            box.AddComponent<BoxCollider>();
            world.Add(box);
            _created.Add(box);
        }
        const int terrainSide = 513;
        var data = new TerrainData { heightmapResolution = terrainSide, size = new Vector3(64f, 10f, 64f) };
        var terrain = Terrain.CreateTerrainGameObject(data);
        terrain.name = "StaticTerrain";
        terrain.transform.position = new Vector3(0f, -5f, 40f);
        world.Add(terrain);
        _created.Add(terrain);

        _recorder.StartRecording("network-recorder-static.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        var tickModule = _manager.tickModule;
        uint firstTick = tickModule.localTick;
        yield return Ticks(_manager, 70);

        var late = new GameObject("StaticLate");
        late.transform.position = new Vector3(0f, 0f, 30f);
        late.AddComponent<SphereCollider>();
        world.Add(late);
        _created.Add(late);
        world.Remove(world[0]);
        Object.DestroyImmediate(_created.Find(o => o && o.name == "StaticBox0"));
        yield return Ticks(_manager, 130);

        foreach (var part in world)
            part.transform.position += new Vector3(100f, 0f, 0f);
        uint shiftedAt = tickModule.localTick;
        yield return Ticks(_manager, 3);

        const int sampleX = 7, sampleZ = 131;
        data.SetHeights(sampleX, sampleZ, new float[,] { { 0.5f } });
        yield return Ticks(_manager, 140);
        data.SetHoles(sampleX, sampleZ, new bool[,] { { false } });
        yield return Ticks(_manager, 140);

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        var terrainRecords = new List<byte[]>();
        var meta = ReadMeta(File.ReadAllBytes(path), terrainRecords);
        var statics = meta.FindAll(m => m.kind == RecordKind.Colliders && m.json.StartsWith("{\"entity\":null,"));
        var text = new StringBuilder();
        foreach (var record in statics)
            text.Append(record.json).Append('\n');
        string all = text.ToString();

        Assert.That(statics.Count, Is.GreaterThan(0), "the static world is in the recording:\n" + Dump(meta));
        uint Tick(string json) => uint.Parse(System.Text.RegularExpressions.Regex.Match(json, "\"tick\":(\\d+)").Groups[1].Value);
        Assert.That(Tick(statics[0].json), Is.LessThanOrEqualTo(firstTick + 1), "what was there when the recording started is there from its first tick");
        Assert.That(all, Does.Not.Contain("\"colliders\":["), "never written as a whole");
        Assert.That(statics[0].json, Does.Contain("StaticBox7").And.Contain("StaticTerrain"), all);
        Assert.That(all, Does.Contain("\"removed\":["), "the collider that was destroyed is taken out:\n" + all);
        Assert.That(statics.Exists(m => m.json.Contains("StaticLate") && !m.json.Contains("StaticBox7")), Is.True,
            "the one that was added is written without the others:\n" + all);
        var moved = statics.Find(m => m.json.Contains("\"shift\":[100,0,0]"));
        Assert.That(moved.json, Is.Not.Null, "the world moving at once is one record:\n" + all);
        Assert.That(Tick(moved.json) - shiftedAt, Is.LessThanOrEqualTo(2), "on the tick it moved");
        var surfaces = new HashSet<string>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(all, "\"name\":\"StaticTerrain\",\"mesh\":(\\d+)"))
            surfaces.Add(match.Groups[1].Value);
        Assert.That(surfaces.Count, Is.GreaterThanOrEqualTo(3), "height and hole edits both record a new surface:\n" + all);
        Assert.That(terrainRecords.Count, Is.GreaterThanOrEqualTo(3));
        var payload = terrainRecords[terrainRecords.Count - 1];
        int at = 0;
        Assert.That(surfaces, Does.Contain(VarUInt(payload, ref at).ToString()));
        int nameLength = (int)VarUInt(payload, ref at);
        at += nameLength;
        Assert.That(VarUInt(payload, ref at), Is.EqualTo(terrainSide));
        Assert.That(System.BitConverter.ToSingle(payload, at + 4), Is.EqualTo(data.size.y));
        at += 12;
        int heightOffset = (sampleZ * terrainSide + sampleX) * sizeof(float);
        Assert.That(System.BitConverter.ToSingle(payload, at + heightOffset),
            Is.EqualTo(data.GetHeights(sampleX, sampleZ, 1, 1)[0, 0]), "the off-grid sample survives exactly");
        at += terrainSide * terrainSide * sizeof(float);
        int quad = sampleZ * (terrainSide - 1) + sampleX;
        Assert.That(payload.Length - at, Is.EqualTo((terrainSide - 1) * (terrainSide - 1) / 8));
        Assert.That(payload[at + (quad >> 3)], Is.EqualTo(1 << (quad & 7)));
        Assert.That(meta.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no recorder errors");
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    [UnityTest]
    public IEnumerator AnEditDiscardsPartialTerrainAndStoppingKeepsItsBounds()
    {
        yield return StartHost(false);
        var data = new TerrainData { heightmapResolution = 1025, size = new Vector3(64f, 10f, 64f) };
        var terrain = Terrain.CreateTerrainGameObject(data);
        terrain.name = "InterruptedTerrain";
        _created.Add(data);
        _created.Add(terrain);
        _recorder.StartRecording("network-recorder-terrain-interrupted.purrrec");
        var requests = (IDictionary)typeof(RecordingSession).GetField("_terrainRequests", Fields).GetValue(_recorder.session);
        object capture = null;
        bool StartedReading()
        {
            foreach (var value in requests.Values)
            {
                var rows = (int)value.GetType().GetField("nextHeight", Fields).GetValue(value);
                if (rows == 0)
                    continue;
                capture = value;
                return true;
            }
            return false;
        }
        yield return WaitUntil(StartedReading);
        Assert.That(capture, Is.Not.Null);
        var firstRead = capture;
        data.SetHeights(0, 0, new float[,] { { 0.5f } });
        Assert.That(requests.Count, Is.Zero, "the already sampled rows are discarded when the heightmap changes");
        yield return WaitUntil(StartedReading);
        Assert.That(capture, Is.Not.SameAs(firstRead), "the edited terrain gets a new snapshot");

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        var terrains = new List<byte[]>();
        var meta = ReadMeta(File.ReadAllBytes(path), terrains);
        Assert.That(terrains, Is.Empty, "closing does not force either unfinished heightmap through the main thread");
        Assert.That(meta.Exists(m => m.kind == RecordKind.Colliders && m.json.Contains("\"name\":\"InterruptedTerrain\",\"mesh\":")
            && m.json.Contains("\"size\":[")), Is.True, Dump(meta));
    }

    [Test]
    public void TerrainUsesManagedCompressionWhenTheNativeCompressorIsUnavailable()
    {
        string path = Path.GetTempFileName();
        var state = typeof(NativeDeflate).GetField("_state", BindingFlags.Static | BindingFlags.NonPublic);
        var previous = state.GetValue(null);
        try
        {
            state.SetValue(null, -1);
            var prefix = new RecordingBuffer();
            prefix.WriteByte(0x12);
            var heights = new float[65, 65];
            heights[3, 7] = 0.5f;
            var holes = new byte[512];
            holes[7] = 0x10;
            var file = new RecordingFile(path);
            using (file)
                file.EnqueueTerrain(prefix, new List<float[,]> { new float[1, 65], heights }, holes);

            Assert.That(file.error, Is.Null);
            Assert.That(file.nativeDeflate, Is.False);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes[10], Is.EqualTo((byte)RecordKind.Terrain | 0x80));
            var terrains = new List<byte[]>();
            ReadMeta(bytes, terrains);
            Assert.That(terrains.Count, Is.EqualTo(1));
            var payload = terrains[0];
            Assert.That(payload[0], Is.EqualTo(0x12));
            Assert.That(System.BitConverter.ToSingle(payload, 1 + (4 * 65 + 7) * sizeof(float)), Is.EqualTo(0.5f));
            Assert.That(payload[1 + (65 + heights.Length) * sizeof(float) + 7], Is.EqualTo(0x10));
        }
        finally
        {
            state.SetValue(null, previous);
            File.Delete(path);
        }
    }
#endif

    [UnityTest]
    public IEnumerator ARecorderThatFailsStopsAndLeavesTheGameAlone()
    {
        yield return StartHost(true);
        yield return WaitUntil(() => _recorder.isRecording);
        Assert.That(_recorder.isRecording, Is.True, "recording started by itself");
        string path = _recorder.currentPath;
        var instance = UnityProxy.Instantiate(_prefab, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
        instance.SetActive(true);
        _manager.Spawn(instance);
        yield return Ticks(_manager, 5);

        var tickModule = _manager.tickModule;
        int after = 0;
        System.Action count = () => after++;
        tickModule.onPostTick += count;
        uint from = tickModule.localTick;
        var failing = new FailingRecordingSource();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("The recorder failed and has stopped recording"));
        _recorder.session.AddSource(failing);
        yield return Ticks(_manager, 6);
        tickModule.onPostTick -= count;
        uint ticks = tickModule.localTick - from;
        _recorder.session.RemoveSource(failing);

        Assert.That(failing.asked, Is.EqualTo(1), "it failed once and was not asked again");
        Assert.That(after, Is.EqualTo((int)ticks), "what comes after the recorder in a tick ran on every tick, the failing one too");
        Assert.That(_recorder.isRecording, Is.False, "the recording ended");
        yield return Ticks(_manager, 4);
        Assert.That(_recorder.isRecording, Is.False, "and no new one starts by itself");

        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        var meta = ReadMeta(path);
        string footer = meta.Find(m => m.kind == RecordKind.Footer).json;
        Assert.That(footer, Is.Not.Null, "the file has its footer:\n" + Dump(meta));
        Assert.That(footer, Does.Contain("\"errors\"").And.Contain(FailingRecordingSource.Message), "which says what went wrong");
        Assert.That(footer, Does.Contain("\"chunks\":1"), "and what was recorded until then is in it");
    }

    [UnityTest]
    public IEnumerator ARecordingThatCannotBeWrittenEnds()
    {
        yield return StartHost(false);
        Set(_recorder, "_chunkSeconds", 0.2f);
        _recorder.StartRecording("network-recorder-unwritable.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        string path = _recorder.currentPath;
        yield return Ticks(_manager, 3);

        var file = Get(_recorder.session, "_file");
        var real = (FileStream)Get(file, "_stream");
        var broken = new UnwritableFile(Path.ChangeExtension(path, ".tmp"));
        Set(file, "_stream", broken);
        real.Dispose();
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("The recorder failed and has stopped recording"));
        yield return WaitUntil(() => !_recorder.isRecording, 20f);
        Assert.That(_recorder.isRecording, Is.False, "a recording whose file cannot be written ends");
        yield return Ticks(_manager, 4);
        Assert.That(_recorder.isRecording, Is.False, "and stays ended");
    }

    static object Get(object target, string name) => target.GetType().GetField(name, Fields)!.GetValue(target);

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
    internal static List<(RecordKind kind, string json)> ReadMeta(string path) => ReadMeta(File.ReadAllBytes(path));

    internal static List<(RecordKind kind, string json)> ReadMeta(byte[] data, List<byte[]> terrainRecords = null)
    {
        var result = new List<(RecordKind, string)>();
        Read(data, 10, data.Length, result, terrainRecords);
        return result;
    }

    static void Read(byte[] data, int pos, int end, List<(RecordKind, string)> into, List<byte[]> terrainRecords)
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
                Read(payload, 0, payload.Length, into, terrainRecords);
            else if (k == RecordKind.Terrain)
                terrainRecords?.Add(payload);
            else if (k != RecordKind.Chunk && k != RecordKind.Mesh)
                into.Add((k, Encoding.UTF8.GetString(payload)));
        }
    }
}
