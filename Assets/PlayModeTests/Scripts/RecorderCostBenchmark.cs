#if PURRSERVICES_RECORDER
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Recording;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.Rendering;

public class RecorderCostMover : NetworkBehaviour
{
    public SyncVar<int> health = new SyncVar<int>(100);
    public SyncVar<Vector3> aim = new SyncVar<Vector3>();
    public SyncDictionary<int, int> scores = new SyncDictionary<int, int>();
    public int shots;

    [ServerRpc(requireOwnership: false)]
    public void Fire(int weapon, Vector3 direction) => shots++;
}

public class RecorderCostBullet : NetworkBehaviour
{
    public SyncVar<int> health = new SyncVar<int>(100);
    public SyncVar<string> label = new SyncVar<string>("start");
    public SyncList<int> items = new SyncList<int>();
}

public class RecorderCostPiece : NetworkBehaviour { }

public struct RecorderCostPing : IPackedAuto
{
    public int value;
    public string text;
}

/// <summary>
/// What recording costs the main thread in a busy match, measured by the recorder itself: a host with a level of big
/// mesh colliders spawning in, a few hundred moving identities with sync types, an animator, a particle system and an
/// audio source each, objects spawning and despawning every tick, effect objects no other machine has, RPCs,
/// broadcasts and the game's own effect calls.
///
/// The same run in the editor (the explicit test NetworkRecorderCostTests.BusyMatch) and in a built player
/// (<c>PurrNetTests.exe -batchmode -nographics -recorderCost -costOut dir</c>). Options: <c>-costLabel name</c>,
/// <c>-costTicks n</c>, <c>-costEffects off</c>, <c>-costEffectsBudget ms</c>, <c>-costEffectChanges off</c>,
/// <c>-costTransformJob off</c>.
/// </summary>
/// <summary>Nothing in a player refers to it: it starts itself, so the linker is told to keep it.</summary>
[UnityEngine.Scripting.Preserve]
public sealed class RecorderCostBenchmark : MonoBehaviour
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const int Movers = 300, LevelPieces = 12, ChurnPerTick = 4, CallsPerTick = 40;

    /// <summary>The last run's report, and whether the whole level was spawned during it.</summary>
    public static string report;
    public static bool complete;

    [UnityEngine.Scripting.Preserve]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (Application.isEditor || !CommandLineUtils.HasFlag("-recorderCost"))
            return;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            DestroyImmediate(root);
        var go = new GameObject(nameof(RecorderCostBenchmark));
        DontDestroyOnLoad(go);
        go.AddComponent<RecorderCostBenchmark>();
    }

    IEnumerator Start()
    {
        string directory = Option("-costOut", Path.Combine(Application.persistentDataPath, "recorder-cost"));
        yield return Run(Path.GetFullPath(directory));
        Application.Quit(complete ? 0 : 1);
    }

    static string Option(string name, string fallback) =>
        CommandLineUtils.TryGetArgument(name, out var value) ? value : fallback;

    static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Fields)!.SetValue(target, value);

    static IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 20f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    /// <summary>A bumpy grid: a level-sized collider mesh (side * side vertices).</summary>
    static Mesh Terrain(List<Object> created, string name, int side, float seed)
    {
        var vertices = new Vector3[side * side];
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                vertices[z * side + x] = new Vector3(x * 0.2f, Mathf.PerlinNoise(x * 0.05f + seed, z * 0.05f) * 3f, z * 0.2f);
        var triangles = new int[(side - 1) * (side - 1) * 6];
        int t = 0;
        for (int z = 0; z < side - 1; z++)
            for (int x = 0; x < side - 1; x++)
            {
                int a = z * side + x;
                triangles[t++] = a; triangles[t++] = a + side; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = a + side; triangles[t++] = a + side + 1;
            }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, vertices = vertices, triangles = triangles };
        mesh.RecalculateBounds();
        created.Add(mesh);
        return mesh;
    }

    static GameObject Prefab(List<Object> created, NetworkPrefabs provider, string name, System.Action<GameObject> build)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        build(go);
        created.Add(go);
        provider.AddRuntimePrefab(name, go);
        return go;
    }

    /// <summary>Runs the match and writes cost-&lt;label&gt;.txt (and the recording) to <paramref name="directory"/>.</summary>
    public static IEnumerator Run(string directory)
    {
        report = null;
        complete = false;
        uint ticks = uint.Parse(Option("-costTicks", "300"));
        string label = Option("-costLabel", "run");
        bool effectChanges = Option("-costEffectChanges", "on") != "off";
        var created = new List<Object>();

        var go = new GameObject("RecorderCostManager");
        go.SetActive(false);
        created.Add(go);
        var transport = go.AddComponent<LocalTransport>();
        var manager = go.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        created.Add(rules);
        manager.SetNetworkRules(rules);
        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        created.Add(provider);
        manager.SetPrefabProvider(provider);
        manager.transport = transport;

        var controller = Resources.Load<RuntimeAnimatorController>("NetAnimTestController");
        var shot = AudioClip.Create("Shot", 4410, 1, 44100, false);
        created.Add(shot);

        var body = Terrain(created, "MoverBody", 8, 0);
        var mover = Prefab(created, provider, "Mover", o =>
        {
            o.AddComponent<RecorderCostMover>();
            o.AddComponent<BoxCollider>();
            o.AddComponent<MeshFilter>().sharedMesh = body;
            o.AddComponent<MeshRenderer>();
            var graphics = new GameObject("Graphics");
            graphics.transform.SetParent(o.transform, false);
            graphics.AddComponent<Animator>().runtimeAnimatorController = controller;
            var muzzle = graphics.AddComponent<ParticleSystem>().main;
            muzzle.playOnAwake = false;
            muzzle.loop = false;
            muzzle.duration = 0.3f;
            var gun = graphics.AddComponent<AudioSource>();
            gun.playOnAwake = false;
            gun.clip = shot;
        });
        var bullet = Prefab(created, provider, "Bullet", o =>
        {
            o.AddComponent<RecorderCostBullet>();
            o.AddComponent<MeshFilter>().sharedMesh = body;
            o.AddComponent<MeshRenderer>();
            o.AddComponent<ParticleSystem>();
        });
        var flash = new GameObject("Flash");
        flash.SetActive(false);
        flash.AddComponent<ParticleSystem>();
        created.Add(flash);
        var flashes = new Queue<GameObject>();

        var terrains = new[] { Terrain(created, "TerrainA", 500, 0), Terrain(created, "TerrainB", 500, 40) };
        var pieces = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            var mesh = terrains[i];
            pieces[i] = Prefab(created, provider, "Level" + i, o =>
            {
                o.AddComponent<RecorderCostPiece>();
                o.AddComponent<MeshCollider>().sharedMesh = mesh;
            });
        }

        var recorder = go.AddComponent<NetworkRecorder>();
        Set(recorder, "_recordOnServer", false);
        Set(recorder, "_recordInEditor", true);
        Set(recorder, "_upload", false);
        Set(recorder, "_directory", directory);
        Set(recorder, "_networkManager", manager);
        Set(recorder, "_recordEffects", Option("-costEffects", "on") != "off");
        if (CommandLineUtils.TryGetArgument("-costEffectsBudget", out var effectsBudget))
            Set(recorder, "_effectsBudgetMs", float.Parse(effectsBudget, CultureInfo.InvariantCulture));
        go.SetActive(true);
        manager.StartHost();
        yield return WaitUntil(() => manager.isServer && manager.isClient && manager.players.Count == 1);

        var scene = go.scene;
        var movers = new List<RecorderCostMover>();
        for (int i = 0; i < Movers; i++)
        {
            var instance = UnityProxy.Instantiate(mover, new Vector3(i % 20 * 2, 1, i / 20 * 2), Quaternion.identity, scene);
            instance.SetActive(true);
            manager.Spawn(instance);
            movers.Add(instance.GetComponent<RecorderCostMover>());
        }
        var animators = new List<Animator>();
        var muzzles = new List<ParticleSystem>();
        var guns = new List<AudioSource>();
        foreach (var m in movers)
        {
            animators.Add(m.GetComponentInChildren<Animator>());
            muzzles.Add(m.GetComponentInChildren<ParticleSystem>());
            guns.Add(m.GetComponentInChildren<AudioSource>());
        }
        yield return WaitUntil(() => movers[movers.Count - 1].isSpawned);

        int pings = 0;
        PlayerBroadcastDelegate<RecorderCostPing> onPing = (player, data, asServer) => pings++;
        manager.Subscribe(onPing, true);

        int jobFrom = RecordingSession.transformJobFrom;
        if (Option("-costTransformJob", "on") == "off")
            RecordingSession.transformJobFrom = int.MaxValue;
        recorder.session.stats.enabled = true;
        if (CommandLineUtils.TryGetArgument("-costSplitTicks", out var splitTicks))
            Set(recorder, "_splitMinutes", float.Parse(splitTicks, CultureInfo.InvariantCulture) / (60f * manager.tickModule.tickRate));
        recorder.StartRecording($"cost-{label}.purrrec");
        if (!recorder.isRecording)
        {
            report = "the recording did not start";
            Debug.LogError(report);
            yield break;
        }

        var tickModule = manager.tickModule;
        var bullets = new Queue<GameObject>();
        var frameMs = new List<double>();
        uint lastTick = tickModule.localTick, firstTick = lastTick, endTick = lastTick + ticks;
        int levelSpawned = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (tickModule.localTick < endTick)
        {
            uint tick = tickModule.localTick;
            if (tick != lastTick)
            {
                lastTick = tick;
                if (levelSpawned < LevelPieces && (tick - firstTick) % 10 == 5)
                {
                    var piece = UnityProxy.Instantiate(pieces[levelSpawned % 2], new Vector3(levelSpawned * 100, -5, 0), Quaternion.identity, scene);
                    piece.SetActive(true);
                    manager.Spawn(piece);
                    levelSpawned++;
                }
                for (int i = 0; i < ChurnPerTick; i++)
                {
                    var b = UnityProxy.Instantiate(bullet, new Vector3(tick % 50, 2, i), Quaternion.identity, scene);
                    b.SetActive(true);
                    manager.Spawn(b);
                    bullets.Enqueue(b);
                }
                while (bullets.Count > ChurnPerTick * 20)
                    Destroy(bullets.Dequeue());
                for (int i = 0; effectChanges && i < 2; i++)
                {
                    var f = Instantiate(flash, new Vector3(tick % 30, 3, i), Quaternion.identity);
                    f.SetActive(true);
                    flashes.Enqueue(f);
                }
                while (flashes.Count > 40)
                    Destroy(flashes.Dequeue());
                for (int i = 0; i < CallsPerTick; i++)
                {
                    var m = movers[(int)((tick * 7 + i) % movers.Count)];
                    m.Fire(i, new Vector3(i, tick, 1));
                    if (i % 4 == 0)
                        manager.SendToServer(new RecorderCostPing { value = i, text = "ping" });
                }
                for (int i = 0; i < movers.Count; i += 10)
                {
                    movers[i].health.value = (int)tick + i;
                    movers[i].scores[i % 4] = (int)tick;
                }
                for (int i = 0; effectChanges && i < 12; i++)
                {
                    int m = (int)((tick * 13 + i * 5) % movers.Count);
                    if (controller)
                    {
                        if (i % 2 == 0)
                            animators[m].Play("Idle", 0, 0f);
                        else
                            animators[m].SetTrigger("TrigA");
                    }
                    if (i < 8)
                    {
                        muzzles[m].Play();
                        guns[m].Play();
                    }
                }
            }
            float time = Time.time;
            for (int i = 0; i < movers.Count; i++)
                movers[i].transform.position = new Vector3(i % 20 * 2 + Mathf.Sin(time + i), 1, i / 20 * 2 + Mathf.Cos(time + i));
            double before = clock.Elapsed.TotalMilliseconds;
            yield return null;
            frameMs.Add(clock.Elapsed.TotalMilliseconds - before);
        }

        string path = recorder.currentPath;
        long byJob = recorder.session.transformsReadByJob;
        recorder.StopRecording();
        RecordingSession.transformJobFrom = jobFrom;
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        manager.Unsubscribe(onPing, true);

        frameMs.Sort();
        string backend =
#if ENABLE_IL2CPP
            "IL2CPP";
#else
            "Mono";
#endif
        string where = Application.isEditor ? "editor" : Debug.isDebugBuild ? "development player" : "release player";
        report = $"{label} ({where}, {backend}, Unity {Application.unityVersion}): {Movers} movers, {levelSpawned} level pieces, " +
                 $"{ChurnPerTick} spawns and {CallsPerTick} RPCs per tick, {ticks} ticks at {tickModule.tickRate} Hz, controller {(controller ? "yes" : "MISSING")}\n" +
                 $"frames: median {frameMs[frameMs.Count / 2]:0.00} ms, p99 {frameMs[(int)(frameMs.Count * 0.99)]:0.00} ms, max {frameMs[frameMs.Count - 1]:0.00} ms\n" +
                 recorder.DescribeLastRecording() + $" | objects that entered late (spawn budget): {recorder.spawnsDeferred}" +
                 $" | effect checks that waited a tick (effects budget): {recorder.session.effectChecksDeferred}" +
                 $" | poses read by the job: {byJob}" +
                 $" | splits: {recorder.splits}, the last one took the main thread {recorder.lastSplitMs:0.000} ms";
        File.WriteAllText(Path.Combine(directory, $"cost-{label}.txt"), report);
        Debug.Log(report);
        complete = levelSpawned == LevelPieces;

        manager.StopClient();
        manager.StopServer();
        for (int i = 0; i < 10; i++)
            yield return null;
        foreach (var f in flashes)
            if (f)
                DestroyImmediate(f);
#if UNITY_6000_4_OR_NEWER
        foreach (var identity in FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include))
#else
        foreach (var identity in FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#endif
            if (identity)
                DestroyImmediate(identity.gameObject);
        for (int i = created.Count - 1; i >= 0; i--)
            if (created[i])
                DestroyImmediate(created[i]);
    }
}
#endif
