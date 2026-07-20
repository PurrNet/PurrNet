using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SceneTransferCorrectionRoot : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _alive = new();
    private static readonly HashSet<NetworkID> _aliveInTargetScene = new();

    public static SceneTransferCorrectionRoot LocalInstance;
    public static int AliveCount => _alive.Count;
    public static int AliveInTargetSceneCount => _aliveInTargetScene.Count;
    public static bool SawBadId;
    public static int SpawnCount;
    public static int ServerDoneCount;
    public static int VictimReturnedCount;
    public static ulong VictimId;
    public static bool VictimIdReceived;
    public static bool TransferCommandReceived;
    public static bool PhaseDoneReceived;

    public static void ResetAll()
    {
        _alive.Clear();
        _aliveInTargetScene.Clear();
        LocalInstance = null;
        SawBadId = false;
        SpawnCount = 0;
        ServerDoneCount = 0;
        VictimReturnedCount = 0;
        VictimId = 0;
        VictimIdReceived = false;
        TransferCommandReceived = false;
        PhaseDoneReceived = false;
    }

    private NetworkID? _trackedId;
    private bool _trackedInTargetScene;

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned()
    {
        if (!id.HasValue)
        {
            SawBadId = true;
            return;
        }

        SpawnCount++;
        _trackedId = id.Value;
        _trackedInTargetScene = IsInScene(SceneTransferCorrectionScenario.TargetSceneName);

        _alive.Add(id.Value);
        if (_trackedInTargetScene)
            _aliveInTargetScene.Add(id.Value);

        LocalInstance = this;
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
        {
            _alive.Remove(_trackedId.Value);
            if (_trackedInTargetScene)
                _aliveInTargetScene.Remove(_trackedId.Value);
        }

        _trackedId = null;
        _trackedInTargetScene = false;

        if (LocalInstance == this)
            LocalInstance = null;
    }

    public bool IsInScene(string sceneName)
    {
        return gameObject.scene.name == sceneName;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalVictimReturned(RPCInfo info = default) => VictimReturnedCount++;

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimId = victimId;
        VictimIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastTransferCommand()
    {
        TransferCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone()
    {
        PhaseDoneReceived = true;
    }
}
