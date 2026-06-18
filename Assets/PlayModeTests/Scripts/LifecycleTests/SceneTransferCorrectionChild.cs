using System.Collections.Generic;
using PurrNet;

public class SceneTransferCorrectionChild : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _alive = new();
    private static readonly HashSet<NetworkID> _aliveInTargetScene = new();

    public static int AliveCount => _alive.Count;
    public static int AliveInTargetSceneCount => _aliveInTargetScene.Count;
    public static bool SawBadId;
    public static int SpawnCount;

    private NetworkID? _trackedId;
    private bool _trackedInTargetScene;

    public static void ResetAll()
    {
        _alive.Clear();
        _aliveInTargetScene.Clear();
        SawBadId = false;
        SpawnCount = 0;
    }

    protected override void OnSpawned()
    {
        if (!id.HasValue || id.Value == default)
        {
            SawBadId = true;
            return;
        }

        SpawnCount++;
        _trackedId = id.Value;
        _trackedInTargetScene = gameObject.scene.name == SceneTransferCorrectionScenario.TargetSceneName;

        _alive.Add(id.Value);
        if (_trackedInTargetScene)
            _aliveInTargetScene.Add(id.Value);
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
    }
}
