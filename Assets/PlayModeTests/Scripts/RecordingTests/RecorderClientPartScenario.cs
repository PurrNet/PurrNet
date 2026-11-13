using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Recording;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

/// <summary>A networked object whose one value is the server's tick: what a client has of it says which tick's state it is showing.</summary>
public class RecorderScenarioBeacon : NetworkBehaviour
{
    public static RecorderScenarioBeacon instance;

    public SyncVar<uint> serverTick = new SyncVar<uint>();

    protected override void OnSpawned(bool asServer) => instance = this;

    protected override void OnDestroy()
    {
        if (instance == this)
            instance = null;
        base.OnDestroy();
    }
}

/// <summary>
/// A server records a match; each remote client writes its own part of it (its camera, raw input, its UI and a
/// custom sample with a string) to a file of its own, and none of that goes over the connection. Devices are virtual and the HUD
/// is built here, so it runs headless. One client joins after the recording started, one leaves and comes back
/// during it. Files go to -recordingsDir (default: a folder under the persistent data path), with what the scene
/// set next to them, for recording/purrrec.py and recording/ui_check.py to check.
/// For lining the files up in time: the server moves a beacon whose value is its tick and every client's camera
/// carries the value that client has; and a client's "press" (written to its part, sent to the server, which writes
/// an "ack" into the recording when it arrives).
/// -partUpload url uploads to a recordings service at url (project key from the environment variable
/// PURRREC_E2E_KEY); -partCapMb n sets the local folder cap; -partLinger s goes offline at the end and stays for s
/// seconds so the uploads run, with a copy of every finished file kept next to the folder; a game started with
/// -uploadFolder dir (and -partUpload) only sends what is in that folder.
/// -partCase stay|late|bounce picks a client's case (default: by its player id); -partNoInput turns its input
/// recording off; -partClock runs the recording on an extension's clock (as PurrDiction does) on every machine.
/// </summary>
public class RecorderClientPartScenario : Scenario
{
    private const int BarrierStart = 10400;
    private const int BarrierBaseline = 10401;
    private const int BarrierWritten = 10402;
    private const int BarrierEnd = 10403;

    [SerializeField] private float _timeoutSeconds = 30f;
    [SerializeField] private float _measureSeconds = 1.5f;

    public struct NoteSample
    {
        public int number;
        public string text;
    }

    public struct PressSample
    {
        public int number;
    }

    [Serializable]
    private struct Expected
    {
        public string role;
        public string partCase;
        public ulong player, playerBefore;
        public string[] parts;
        public string main;
        public bool input;
        public float deltaX, deltaY;
        public string typingVia;
        public long idleBytesSent, recordingBytesSent;
    }

    private const string BeaconName = "RecorderScenario_Beacon";

    private static int _lateClients;
    private static NetworkRecorder _serverRecorder;
    private static string _mainPath;
    private GameObject _beaconPrefab;
    private Transform _camera;

    private NetworkRecorder _recorder;
    private string _directory;
    private long _bytesSent;
    private bool _recordInput;

    // The HUD the clients record.
    private readonly List<UnityEngine.Object> _hud = new List<UnityEngine.Object>();
    private Image _health;
    private Text _score;
    private GameObject _popup;
    private Label _status;
    private readonly StringBuilder _phases = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRunState() => _lateClients = 0;

    private static string ProjectKey => Environment.GetEnvironmentVariable("PURRREC_E2E_KEY") ?? "scenario-key";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void UploadFolder()
    {
        if (CommandLineUtils.TryGetArgument("-uploadFolder", out var folder) && CommandLineUtils.TryGetArgument("-partUpload", out var endpoint))
            RecordingUploader.Configure(endpoint, ProjectKey, folder, 1L << 30);
    }

    private void Keep(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        string kept = _directory.TrimEnd('/', '\\') + "-kept";
        Directory.CreateDirectory(kept);
        File.Copy(path, Path.Combine(kept, Path.GetFileName(path)), true);
    }

    /// <summary>Goes offline and stays, so that what was recorded is uploaded the way a game does it after a match.</summary>
    private async UniTask Linger(ScenarioContext ctx)
    {
        if (!CommandLineUtils.TryGetArgument("-partLinger", out var text) || !float.TryParse(text, out float seconds))
            return;
        var manager = ctx.networkManager;
        if (manager.isClient)
            manager.StopClient();
        if (manager.isServer)
            manager.StopServer();
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
            await UniTask.NextFrame();
    }

    [ServerRpc(requireOwnership: false)]
    private static void ReportLate() => _lateClients++;

    /// <summary>A client pressed something: the server acts on it by noting it in the recording, on the tick it arrived.</summary>
    [ServerRpc(requireOwnership: false)]
    private static void ReportPress(ulong player, int number)
    {
        if (!_serverRecorder || !_serverRecorder.isRecording)
            return;
        foreach (var id in NetworkManager.main.players)
            if (id.id.value == player)
                _serverRecorder.WriteSampleFor("ack", id, new PressSample { number = number });
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _beaconPrefab = new GameObject(BeaconName);
        _beaconPrefab.SetActive(false);
        _beaconPrefab.AddComponent<RecorderScenarioBeacon>();
        manager.prefabProvider.AddRuntimePrefab(BeaconName, _beaconPrefab, false, 0);
    }

    private void Update()
    {
        // The camera shows what this machine has of the beacon.
        if (_camera && RecorderScenarioBeacon.instance)
            _camera.position = new Vector3(RecorderScenarioBeacon.instance.serverTick.value, 2, 3);
    }

    /// <summary>A clock of its own for the recording, like PurrDiction's prediction ticks: network ticks plus an offset.</summary>
    public sealed class ScenarioClock : RecorderExtension, IRecordingContext
    {
        public const ulong Offset = 100000;

        private sealed class NoSource : RecordingSource
        {
            public override string name => "scenarioclock";
            public override bool IsEntityObject(GameObject go) => false;
            public override Transform NearestEntity(Transform t, bool includeInactive) => null;
            public override bool HasNestedEntities(RecordedEntity entity) => false;
        }

        private readonly NoSource _source = new NoSource();
        private PurrNet.Modules.TickManager _ticks;

        public override RecordingSource source => _source;
        public override IRecordingContext clock => this;

        private NetworkManager manager => recorder.networkManager ? recorder.networkManager : NetworkManager.main;
        public bool isReady => manager && manager.tickModule != null;
        public ulong localTick => manager.tickModule.localTick + Offset;
        public double viewTick => manager.tickModule.syncedPreciseTick + Offset;
        public bool isServer => manager.isServer;
        public bool isClient => manager.isClient;
        public PlayerID? localPlayer => manager.isClient ? manager.localPlayer : (PlayerID?)null;
        public int tickRate => manager.tickModule.tickRate;

        public override void OnRecordingStarted()
        {
            _ticks = manager.tickModule;
            _ticks.onPreTick += Tick;
        }

        public override void OnRecordingStopping()
        {
            if (_ticks != null)
                _ticks.onPreTick -= Tick;
            _ticks = null;
        }

        private void Tick() => recorder.RecordTick(localTick);
    }

    private static bool UsesClock => CommandLineUtils.HasFlag("-partClock");

    private void CreateRecorder()
    {
        if (UsesClock)
            NetworkRecorder.RegisterExtension<ScenarioClock>();
        if (!CommandLineUtils.TryGetArgument("-recordingsDir", out _directory))
            _directory = Path.Combine(Application.persistentDataPath, "RecorderClientPartScenario",
                System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        Directory.CreateDirectory(_directory);
        _recordInput = !CommandLineUtils.HasFlag("-partNoInput");

        var go = new GameObject("Scenario recorder");
        go.SetActive(false);
        _recorder = go.AddComponent<NetworkRecorder>();
        if (!Camera.main)
        {
            var camera = new GameObject("Scenario camera", typeof(Camera)) { tag = "MainCamera" };
            camera.transform.SetParent(go.transform);
            camera.transform.position = new Vector3(0, 2, 3);
        }
        _camera = Camera.main.transform;
        string upload = "\"_upload\":false";
        if (CommandLineUtils.TryGetArgument("-partUpload", out var endpoint))
            upload = "\"_upload\":true,\"_projectKey\":\"" + ProjectKey + "\",\"_uploadEndpoint\":\"" + endpoint + "\"";
        if (CommandLineUtils.TryGetArgument("-partCapMb", out var cap))
            upload += ",\"_maxLocalMegabytes\":" + cap;
        // Started by hand, kept on disk, short chunks.
        JsonUtility.FromJsonOverwrite("{\"_recordOnServer\":false," + upload + ",\"_chunkSeconds\":1" +
                                      ",\"_recordInput\":" + (_recordInput ? "true" : "false") +
                                      ",\"_directory\":\"" + _directory.Replace('\\', '/') + "\"}", _recorder);
        go.SetActive(true);
    }

    private void OnDataSent(Connection conn, ByteData data, bool asServer)
    {
        if (!asServer)
            _bytesSent += data.length;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        CreateRecorder();
        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierStart, _timeoutSeconds);
            var result = await RunSplit(ctx, RunClient, RunServer);
            if (result.success)
                await Linger(ctx);
            return result;
        }
        finally
        {
            if (_recorder)
                Destroy(_recorder.gameObject);
            foreach (var created in _hud)
                if (created)
                    Destroy(created);
            _hud.Clear();
        }
    }

    private async UniTask<ScenarioResult> RunServer(ScenarioContext ctx)
    {
        await ScenarioBarrier.Wait(ctx, BarrierBaseline, _timeoutSeconds);
        // The clients that join late have left by the time the recording starts (on a slow connection the server
        // may only notice later that they left; they stay away longer than this waits).
        var manager = ctx.networkManager;
        float leaveBy = Time.realtimeSinceStartup + 2f;
        while (manager.playerCount > ctx.expectedConnections - _lateClients && Time.realtimeSinceStartup < leaveBy)
            await UniTask.NextFrame();
        // Uploaded files get the name a game would give them.
        _recorder.StartRecording(CommandLineUtils.HasFlag("-partUpload") ? null : "main.purrrec");
        if (!_recorder.isRecording)
            return ScenarioResult.Fail("the server did not start recording");
        string path = _recorder.currentPath;
        _mainPath = path;
        _serverRecorder = _recorder;

        // The beacon carries the tick the recording is on.
        var beacon = Instantiate(_beaconPrefab);
        beacon.SetActive(true);
        var beaconValue = beacon.GetComponent<RecorderScenarioBeacon>();
        var ticks = manager.tickModule;
        uint offset = UsesClock ? (uint)ScenarioClock.Offset : 0;
        Action move = () => beaconValue.serverTick.value = ticks.localTick + offset;
        ticks.onPreTick += move;

        await ScenarioBarrier.Wait(ctx, BarrierWritten, _timeoutSeconds * 2);
        ticks.onPreTick -= move;
        _recorder.StopRecording();
        Keep(path);
        Destroy(beacon);
        if (!File.Exists(path) || new FileInfo(path).Length < 64)
            return ScenarioResult.Fail($"no main recording at {path}");
        if (ctx.role == NetworkRole.Server)
            File.WriteAllText(Path.Combine(_directory, "expected.json"),
                JsonUtility.ToJson(new Expected { role = "server", main = path }));

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _timeoutSeconds);
        return ScenarioResult.Ok($"late={_lateClients}");
    }

    // ---- The HUD ----------------------------------------------------------------------------------------

    private static RectTransform Place(GameObject go, Transform parent, Vector2 at, Vector2 size)
    {
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(at.x, -at.y);
        rect.sizeDelta = size;
        return rect;
    }

    private void BuildHud()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvas = new GameObject("HUD", typeof(Canvas));
        _hud.Add(canvas);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        var health = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
        Place(health, canvas.transform, new Vector2(20, 20), new Vector2(200, 20));
        _health = health.GetComponent<Image>();
        var texture = new Texture2D(4, 4) { name = "bar-texture" };
        _hud.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        sprite.name = "bar-sprite";
        _hud.Add(sprite);
        _health.sprite = sprite;
        _health.type = Image.Type.Filled;
        _health.fillMethod = Image.FillMethod.Horizontal;

        var score = new GameObject("Score", typeof(RectTransform), typeof(Text));
        Place(score, canvas.transform, new Vector2(20, 50), new Vector2(200, 30));
        _score = score.GetComponent<Text>();
        _score.font = font;

        var ammo = new GameObject("Ammo", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        Place(ammo, canvas.transform, new Vector2(20, 90), new Vector2(200, 30));
        ammo.GetComponent<TMPro.TextMeshProUGUI>().text = "30 / 90";

        _popup = new GameObject("Popup", typeof(RectTransform), typeof(Image));
        Place(_popup, canvas.transform, new Vector2(200, 200), new Vector2(100, 50));

        // A password field: what is in it must never be in a file.
        var passwordObject = new GameObject("Password", typeof(RectTransform), typeof(Image), typeof(InputField));
        passwordObject.SetActive(false);
        Place(passwordObject, canvas.transform, new Vector2(20, 240), new Vector2(160, 30));
        var passwordText = new GameObject("PasswordText", typeof(RectTransform), typeof(Text));
        Place(passwordText, passwordObject.transform, Vector2.zero, new Vector2(160, 30));
        passwordText.GetComponent<Text>().font = font;
        var password = passwordObject.GetComponent<InputField>();
        password.textComponent = passwordText.GetComponent<Text>();
        password.contentType = InputField.ContentType.Password;
        passwordObject.SetActive(true);
        password.text = "hunter2";

        // An asset, not one made here: a build only has what UI Toolkit text needs for panel settings it was built with.
        var settings = Resources.Load<PanelSettings>("RecorderScenarioPanel");
        var documentObject = new GameObject("Document");
        documentObject.SetActive(false);
        _hud.Add(documentObject);
        var document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        documentObject.SetActive(true);
        var root = document.rootVisualElement;
        root.name = "doc-root";
        _status = new Label("") { name = "tk-status" };
        root.Add(_status);
    }

    /// <summary>Sets the HUD, lets it be recorded, and notes what it shows at a tick nothing changes on.</summary>
    private async UniTask Phase(ScenarioContext ctx, string label, float fill, string score, string status, bool popup)
    {
        _health.fillAmount = fill;
        _score.text = score;
        _status.text = status;
        _popup.SetActive(popup);

        var ticks = ctx.networkManager.tickModule;
        uint until = ticks.localTick + 4;
        await UniTaskUtils.WaitWithTimeout(() => ticks.localTick >= until, _timeoutSeconds, ctx.cancellationToken);
        ulong offset = UsesClock ? ScenarioClock.Offset : 0;
        ulong tick = (ulong)ticks.syncedPreciseTick + offset;
        _phases.Append(_phases.Length > 0 ? ",\n" : "").Append("{\"tick\":").Append(tick).Append(",\"label\":\"").Append(label)
            .Append("\",\"elements\":[")
            .Append("{\"name\":\"HUD\",\"kind\":\"Canvas\",\"toolkit\":false},")
            .Append("{\"name\":\"HealthBar\",\"kind\":\"Image\",\"sprite\":\"bar-sprite\",\"fill\":\"Horizontal\",\"fillAmount\":")
            .Append(fill.ToString("R", CultureInfo.InvariantCulture)).Append(",\"parentName\":\"HUD\"},")
            .Append("{\"name\":\"Score\",\"kind\":\"Text\",\"text\":\"").Append(score).Append("\"},")
            .Append("{\"name\":\"Ammo\",\"kind\":\"Text\",\"type\":\"TextMeshProUGUI\",\"text\":\"30 / 90\"},")
            .Append("{\"name\":\"PasswordText\",\"text\":null,\"redacted\":true},")
            .Append("{\"name\":\"tk-status\",\"text\":\"").Append(status).Append("\",\"toolkit\":true},")
            .Append(popup ? "{\"name\":\"Popup\",\"visible\":true}" : "{\"name\":\"Popup\",\"absent\":true}")
            .Append("]}");

        uint after = ticks.localTick + 3;
        await UniTaskUtils.WaitWithTimeout(() => ticks.localTick >= after && (ulong)ticks.syncedPreciseTick + offset > tick + 1,
            _timeoutSeconds, ctx.cancellationToken);
    }

    private void WriteUIExpectations(ulong player)
    {
        File.WriteAllText(Path.Combine(_directory, "ui.expect.json"),
            "{\"phases\":[\n" + _phases + "],\n\"recording\":\"\",\"player\":" + player + ",\"screen\":[" + Screen.width + "," +
            Screen.height + "],\"forbidden\":[\"hunter2\"]}");
    }

    // ---- Clients ----------------------------------------------------------------------------------------

    private static string CaseOf(ScenarioContext ctx)
    {
        if (ctx.isServer)
            return "stay";
        if (CommandLineUtils.TryGetArgument("-partCase", out var chosen))
            return chosen;
        return (ctx.networkManager.localPlayer.id.value % 3) switch { 1 => "bounce", 2 => "late", _ => "stay" };
    }

    private async UniTask Reconnect(ScenarioContext ctx, bool leave, float awaySeconds)
    {
        var manager = ctx.networkManager;
        if (leave)
            manager.StopClient();
        await UniTaskUtils.WaitWithTimeout(() => manager.clientState == ConnectionState.Disconnected, _timeoutSeconds, ctx.cancellationToken);
        await UniTask.WaitForSeconds(awaySeconds, cancellationToken: ctx.cancellationToken);
        manager.StartClient();
        await UniTaskUtils.WaitWithTimeout(() => manager.isClient && manager.isLocalPlayerReady, _timeoutSeconds, ctx.cancellationToken);
    }

    private async UniTask<ScenarioResult> RunClient(ScenarioContext ctx)
    {
        var session = _recorder.session;
        var manager = ctx.networkManager;
        var transport = manager.transport.transport;
        string partCase = CaseOf(ctx);
        var expected = new Expected
        {
            role = ctx.isServer ? "host" : "client", partCase = partCase, input = _recordInput,
            playerBefore = manager.localPlayer.id.value
        };
        var parts = new List<string>();
        transport.onDataSent += OnDataSent;
        GameObject ui = null;
        Keyboard keyboard = null;
        Mouse mouse = null;
        Gamepad gamepad = null;
        _phases.Clear();
        try
        {
            BuildHud();
            _health.fillAmount = 1f;
            _score.text = "Score: 0";
            if (partCase == "late")
                ReportLate();

            // What this client sends while nothing is recorded.
            _bytesSent = 0;
            await UniTask.WaitForSeconds(_measureSeconds);
            expected.idleBytesSent = _bytesSent;
            if (!ctx.isServer && (session.wantsSamples || session.sampleEpoch != 0))
                return ScenarioResult.Fail("the client records before the server announced a recording");

            await ScenarioBarrier.Wait(ctx, BarrierBaseline, _timeoutSeconds);
            // Joining a recording that is already running.
            if (partCase == "late")
                await Reconnect(ctx, true, 3.5f);
            await UniTaskUtils.WaitWithTimeout(() => session.wantsSamples && session.sampleEpoch != 0, _timeoutSeconds, ctx.cancellationToken);

            // And while its camera, its input and its changing HUD are recorded.
            _bytesSent = 0;
            float until = Time.realtimeSinceStartup + _measureSeconds;
            for (int i = 0; Time.realtimeSinceStartup < until; i++)
            {
                _score.text = "Score: " + i;
                _health.fillAmount = (i % 100) / 100f;
                _status.text = "warmup " + i;
                await UniTask.NextFrame();
            }
            expected.recordingBytesSent = _bytesSent;
            if (!ctx.isServer && expected.recordingBytesSent > expected.idleBytesSent * 1.3f + 256)
                return ScenarioResult.Fail($"the client sent {expected.recordingBytesSent} bytes while its camera, input and " +
                                           $"UI were recorded, {expected.idleBytesSent} before");
            if (!ctx.isServer && !session.isPart)
                return ScenarioResult.Fail("the client has no part open");

            await Phase(ctx, "initial", 1f, "Score: 0", "ready", true);

            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            await UniTask.NextFrame();

            // Held keys and a held mouse button, with the pointer moving for ten frames.
            for (int i = 0; i < 10; i++)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(5, -2), position = new Vector2(10, 20) }
                    .WithButton(MouseButton.Left));
                expected.deltaX += 5;
                expected.deltaY -= 2;
                await UniTask.NextFrame();
            }
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(10, 20) }.WithButton(MouseButton.Left));
            await UniTask.WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(10, 20) });
            await UniTask.WaitForSeconds(0.2f);

            // A key pressed and released within one frame.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await UniTask.WaitForSeconds(0.2f);

            // Typing into a text field: P is held the whole time and must not be recorded.
            ui = new GameObject("Scenario UI", typeof(Canvas), typeof(EventSystem));
            var fieldObject = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldObject.transform.SetParent(ui.transform, false);
            var textObject = new GameObject("FieldText", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(fieldObject.transform, false);
            var field = fieldObject.GetComponent<InputField>();
            field.textComponent = textObject.GetComponent<Text>();
            field.textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EventSystem.current.SetSelectedGameObject(fieldObject);
            field.ActivateInputField();
            await UniTask.DelayFrame(3);
            expected.typingVia = field.isFocused ? "InputField" : "IMGUI";
            if (!field.isFocused)
                GUIUtility.keyboardControl = 4321;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.P));
            await UniTask.WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await UniTask.DelayFrame(3);
            GUIUtility.keyboardControl = 0;
            field.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null);
            Destroy(ui);
            await UniTask.WaitForSeconds(0.2f);

            // A gamepad.
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(0.5f, -0.25f), rightTrigger = 0.75f }
                .WithButton(GamepadButton.South));
            await UniTask.WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            await UniTask.WaitForSeconds(0.2f);

            // What any other client-side recorder does: rows with managed fields, through the active session.
            for (int i = 0; i < 3; i++)
            {
                RecordingSession.active.WriteSample("note", new NoteSample { number = i, text = "hello " + i });
                await UniTask.DelayFrame(2);
            }

            // Presses the server acts on: the same moment in the part ("press", and the E key) and in the recording ("ack").
            for (int i = 0; i < 3; i++)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                RecordingSession.active.WriteSample("press", new PressSample { number = i });
                ReportPress(manager.localPlayer.id.value, i);
                await UniTask.WaitForSeconds(0.15f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                await UniTask.WaitForSeconds(0.15f);
            }

            await Phase(ctx, "changed", 0.4f, "Score: 7", "fight", true);
            // Long enough for the part to hold several chunks.
            await UniTask.WaitForSeconds(1.2f);
            await Phase(ctx, "second chunk", 0.6f, "Score: 9", "fight on", true);

            if (partCase == "bounce")
            {
                // Leaving ends the part; coming back starts another.
                string first = session.currentPath;
                parts.Add(first);
                manager.StopClient();
                await UniTaskUtils.WaitWithTimeout(() => !session.isRecording, _timeoutSeconds, ctx.cancellationToken);
                // With uploads on, the part is sent as soon as the game is offline: a copy is taken before it is gone.
                Keep(first);
                await UniTask.DelayFrame(3);
                if (session.isRecording || session.wantsSamples || session.sampleEpoch != 0)
                    return ScenarioResult.Fail("the part stayed open after the client disconnected");
                await Reconnect(ctx, false, 0.5f);
                await UniTaskUtils.WaitWithTimeout(() => session.isPart, _timeoutSeconds, ctx.cancellationToken);
                if (session.currentPath == first)
                    return ScenarioResult.Fail("the reconnected client did not start a new part");
            }

            await Phase(ctx, "late", 0.9f, "Score: 12", "done", false);
            await UniTask.WaitForSeconds(0.2f);

            if (!ctx.isServer)
            {
                if (!session.isPart)
                    return ScenarioResult.Fail("the client has no part open at the end");
                parts.Add(session.currentPath);
            }
            expected.player = manager.localPlayer.id.value;
            expected.parts = parts.ToArray();

            await ScenarioBarrier.Wait(ctx, BarrierWritten, _timeoutSeconds * 2);

            if (!ctx.isServer)
            {
                // The server's stop closes the part.
                await UniTaskUtils.WaitWithTimeout(() => !session.wantsSamples, _timeoutSeconds, ctx.cancellationToken);
                foreach (string part in parts)
                {
                    bool sent = !File.Exists(part) && CommandLineUtils.HasFlag("-partUpload");
                    if (!sent && (!File.Exists(part) || new FileInfo(part).Length < 64))
                        return ScenarioResult.Fail($"no part at {part}");
                    Keep(part);
                }
            }
            else
                expected.main = _mainPath;
            File.WriteAllText(Path.Combine(_directory, "expected.json"), JsonUtility.ToJson(expected));
            WriteUIExpectations(expected.player);

            await ScenarioBarrier.Wait(ctx, BarrierEnd, _timeoutSeconds);
            return ScenarioResult.Ok($"case={partCase}");
        }
        finally
        {
            transport.onDataSent -= OnDataSent;
            GUIUtility.keyboardControl = 0;
            if (ui)
                Destroy(ui);
            if (keyboard != null)
                InputSystem.RemoveDevice(keyboard);
            if (mouse != null)
                InputSystem.RemoveDevice(mouse);
            if (gamepad != null)
                InputSystem.RemoveDevice(gamepad);
        }
    }
}
