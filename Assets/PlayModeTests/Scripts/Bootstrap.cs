using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.Assertions;

public class Bootstrap : Scenario
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private float _connectionTimeout = 15f;
    [SerializeField] private float _timeBetweenScenarios = 2f;

    [Header("Editor overrides (used when -role / -count are absent)")]
    [Tooltip("Role used by the main editor instance when no -role argument is provided. " +
             "Clones (Unity Multiplayer Playmode) always run as Client regardless of this.")]
    [SerializeField] private NetworkRole _editorRole = NetworkRole.Host;
    [Tooltip("Expected connection count when no -count argument is provided.")]
    [SerializeField] private int _editorExpectedConnections = 2;

    private NetworkRole _role;
    private int _expectedConnections;
    private string _resultsPath;

    private Scenario[] _scenarios;
    private ScenarioDetails?[] _results;

    private CancellationTokenSource _runCts;

    private void Awake()
    {
        _scenarios = GetComponentsInChildren<Scenario>();
        _results = new ScenarioDetails?[_scenarios.Length];
        LoadArgs();
    }

    private void Start()
    {
        if (!_networkManager.transport)
        {
            Debug.LogError($"No network transport found");
            Application.Quit(-1);
            return;
        }

        _runCts = new CancellationTokenSource();

        var ctx = new ScenarioContext
        {
            role = _role,
            expectedConnections = _expectedConnections,
            networkManager = _networkManager,
            cancellationToken = _runCts.Token
        };

        for (var i = 0; i < _scenarios.Length; i++)
            _scenarios[i].Setup(ctx, _networkManager);

        SubscribeToDataSent(_networkManager.transport.transport);

        RunScenarios();
    }

    private ITransport _lastTransport;

    private void SubscribeToDataSent(ITransport transport)
    {
        if (_lastTransport != null)
        {
            _lastTransport.onDataSent -= OnDataSentCallback;
            _lastTransport.onDataReceived -= OnDataReceivedCallback;
        }
        transport.onDataSent += OnDataSentCallback;
        transport.onDataReceived += OnDataReceivedCallback;
        _lastTransport = transport;
    }

    private ulong _dataSent = 0;
    private ulong _dataReceived = 0;

    private void OnDataSentCallback(Connection conn, ByteData data, bool asServer)
    {
        _dataSent += (ulong)data.length;
    }

    private void OnDataReceivedCallback(Connection conn, ByteData data, bool asServer)
    {
        _dataReceived += (ulong)data.length;
    }

    private void LoadArgs()
    {
        if (!TryResolveRole(out _role))
        {
            Application.Quit(-1);
            return;
        }

        if (_role != NetworkRole.Client)
        {
            if (CommandLineUtils.TryGetArgument("-count", out var countString))
            {
                if (!int.TryParse(countString, out _expectedConnections))
                {
                    Debug.LogError($"Could not parse -count value '{countString}'");
                    Application.Quit(-1);
                    return;
                }
            }
            else
            {
#if UNITY_EDITOR
                _expectedConnections = _editorExpectedConnections;
#else
                Debug.LogError($"Expected -count argument");
                Application.Quit(-1);
                return;
#endif
            }
        }

        CommandLineUtils.TryGetArgument("-results", out _resultsPath);
    }

    private bool TryResolveRole(out NetworkRole resolved)
    {
        resolved = default;

        if (CommandLineUtils.TryGetArgument("-role", out var role))
        {
            switch (role)
            {
                case "server": resolved = NetworkRole.Server; return true;
                case "client": resolved = NetworkRole.Client; return true;
                case "host":   resolved = NetworkRole.Host;   return true;
                default:
                    Debug.LogError($"Unknown role '{role}'");
                    return false;
            }
        }

#if UNITY_EDITOR
        // Clones (Unity Multiplayer Playmode) always join as plain clients; the main editor
        // uses the inspector-configured override so individual test scenes can opt in/out
        // of host mode without re-baking command-line scripts.
        resolved = PurrNet.Utils.ApplicationContext.isClone ? NetworkRole.Client : _editorRole;
        return true;
#else
        Debug.LogError($"Expected -role argument");
        return false;
#endif
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        Assert.IsTrue(_networkManager.isOffline);
        Assert.AreEqual(_networkManager.clientState, ConnectionState.Disconnected, "Client is not connected");
        Assert.AreEqual(_networkManager.serverState, ConnectionState.Disconnected, "Server is not started");

        if (ctx.isServer)
            _networkManager.StartServer();
        if (ctx.isClient)
            _networkManager.StartClient();

        await UniTaskUtils.WaitWithTimeout(IsConnected, _connectionTimeout, ctx.cancellationToken);

        if (!ctx.isServer)
            return ScenarioResult.Ok();

        await UniTaskUtils.WaitWithTimeout(AllConnected, _connectionTimeout, ctx.cancellationToken);
        return ScenarioResult.Ok();
    }

    bool IsConnected()
    {
        bool isServer = _networkManager.isServer;
        bool isClient = _networkManager.isClient;
        switch (_role)
        {
            case NetworkRole.Server or NetworkRole.Host when !isServer:
            case NetworkRole.Client or NetworkRole.Host when !isClient:
                return false;
            default:
                return isServer || isClient;
        }
    }

    bool AllConnected()
    {
        return _networkManager.playerCount >= _expectedConnections;
    }

    private async void RunScenarios()
    {
        bool anyFailed = false;

        try
        {
            var ctx = new ScenarioContext
            {
                role = _role,
                expectedConnections = _expectedConnections,
                networkManager = _networkManager,
                cancellationToken = _runCts.Token
            };

            // Scenario 0 is Bootstrap itself (the connection scenario). It must run
            // unconditionally on every peer before the sequencer can drive anything —
            // clients don't exist on the server yet, so there's no one to coordinate with.
            if (_scenarios.Length > 0)
            {
                if (await RunOne(0, ctx))
                    anyFailed = true;
            }

            if (ctx.isServer)
            {
                for (var i = 1; i < _scenarios.Length; i++)
                {
                    ScenarioSequencer.IssueStart(i);

                    if (await RunOne(i, ctx))
                        anyFailed = true;

                    await ScenarioSequencer.WaitForAllAcks(ctx, i);

                    if (i == _scenarios.Length - 1)
                        break;

                    await UniTask.WaitForSeconds(_timeBetweenScenarios);
                }

                ScenarioSequencer.IssueSequenceComplete();
                // End-of-run handshake: bounded wait so a crashed/already-exited peer
                // can't strand the server in the finally-then-Quit path.
                await ScenarioSequencer.WaitForEndOfRunHandshake(ctx);
            }
            else
            {
                for (var i = 1; i < _scenarios.Length; i++)
                {
                    await ScenarioSequencer.WaitForStart(ctx, i);

                    // Defensive: server signalled the run is over (or crashed). Stop
                    // running scenarios the server isn't tracking.
                    if (ScenarioSequencer.SequenceComplete)
                        break;

                    if (await RunOne(i, ctx))
                        anyFailed = true;

                    ScenarioSequencer.AckLocalDone(ctx, i);

                    if (i == _scenarios.Length - 1)
                        break;

                    await UniTask.WaitForSeconds(_timeBetweenScenarios);
                }

                // Wait for the server's sequence-complete broadcast, then ack so
                // the server can exit its handshake wait cleanly.
                await UniTaskUtils.WaitWithTimeout(
                    () => ScenarioSequencer.SequenceComplete,
                    30f,
                    ctx.cancellationToken);
                ScenarioSequencer.AckEndOfRun(ctx);
                // Give the ack a couple frames to flush through transport before
                // the finally block quits the process.
                await UniTask.NextFrame();
                await UniTask.NextFrame();
            }
        }
        catch (Exception e)
        {
            anyFailed = true;
            Debug.LogException(e);
        }
        finally
        {
            bool anyResultFailed = false;
            for (var i = 0; i < _results.Length; i++)
            {
                if (!_results[i].HasValue || !_results[i].Value.result.success)
                {
                    anyResultFailed = true;
                    break;
                }
            }

            WriteResults();

#if UNITY_EDITOR
            if (anyResultFailed || anyFailed)
                Debug.LogError("Some tests failed to run.");
#else
            Application.Quit(anyResultFailed || anyFailed ? -1 : 0);
#endif
        }
    }

    private async UniTask<bool> RunOne(int i, ScenarioContext ctx)
    {
        _dataSent = 0;
        _dataReceived = 0;

        var scenario = _scenarios[i];
        long startTick = DateTime.Now.Ticks;
        var result = await GetResult(scenario, ctx, i);
        var elapsedTick = DateTime.Now.Ticks - startTick;
        var elapsedMs = elapsedTick / (double)TimeSpan.TicksPerMillisecond;

        _results[i] = new ScenarioDetails
        {
            name = scenario.GetType().Name,
            result = result,
            durationInMs = elapsedMs,
            dataSent = _dataSent,
            dataReceived = _dataReceived
        };

        return !result.success;
    }

    private static async Task<ScenarioResult> GetResult(Scenario scenario, ScenarioContext ctx, int i)
    {
        ScenarioResult details;

        try
        {
            details = await scenario.RunScenario(ctx);
        }
        catch (Exception e)
        {
            details = ScenarioResult.Fail(e.Message);
            Debug.LogError($"Scenario [{i}] `{scenario.name}` failed with: {e}");
        }

        return details;
    }

    private void WriteResults()
    {
        var json = JArray.FromObject(_results).ToString(Formatting.Indented);
        Debug.Log(json);

        if (string.IsNullOrEmpty(_resultsPath))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_resultsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_resultsPath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write results to '{_resultsPath}': {e}");
        }
    }
}
