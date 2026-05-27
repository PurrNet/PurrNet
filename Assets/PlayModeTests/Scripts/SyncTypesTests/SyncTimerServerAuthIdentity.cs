using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative <see cref="SyncTimer"/>. The server drives Start/Pause/Resume/Stop and every
/// observer must converge on the matching timer state at each phase.
/// State codes: 0 = Stopped, 1 = Running, 2 = Paused.
/// </summary>
public class SyncTimerServerAuthIdentity : NetworkIdentity
{
    [SerializeField, HideInInspector] private SyncTimer _timer = new(ownerAuth: false, reconcileInterval: 1f);

    public static SyncTimerServerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int CurrentPhase;
    public static int PhaseAckCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;

    public const int PhaseCount = 4;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        CurrentPhase = 0;
        PhaseAckCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
    }

    public int StateCode() => _timer.isRunning ? 1 : _timer.isPaused ? 2 : 0;

    /// <summary>Expected state code for each 1-based phase.</summary>
    public static int ExpectedState(int phase) => phase switch
    {
        1 => 1, // Start  -> Running
        2 => 2, // Pause  -> Paused
        3 => 1, // Resume -> Running
        4 => 0, // Stop   -> Stopped
        _ => -1
    };

    public void DoPhaseOp(int phase)
    {
        switch (phase)
        {
            case 1: _timer.StartTimer(60f); break;
            case 2: _timer.PauseTimer(); break;
            case 3: _timer.ResumeTimer(); break;
            case 4: _timer.StopTimer(); break;
        }
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void AckPhase(RPCInfo info = default) => PhaseAckCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhase(int phase) => CurrentPhase = phase;

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
