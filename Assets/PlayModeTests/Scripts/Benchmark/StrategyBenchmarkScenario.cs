using PurrNet;
using UnityEngine;

public class StrategyBenchmarkScenario : BenchmarkScenario
{
    [SerializeField] private float _maxSendInterval = 0.2f;

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        var nt = CreatePrefab(manager, nameof(StrategyBenchmarkScenario) + "_Obj");
        nt.SetSyncStrategy(new NetworkTransformDefaultStrategy { maxSendInterval = _maxSendInterval });
    }
}
