using PurrNet;
using UnityEngine;

public class StrategyBenchmarkScenario : BenchmarkScenario
{
    [SerializeField] private float _maxSendInterval = 0.2f;
    [SerializeField, Range(0f, 1f)] private float _extrapolation;
    [SerializeField] private bool _sendConfirmations = true;

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        var nt = CreatePrefab(manager, nameof(StrategyBenchmarkScenario) + "_Obj");

        var strategy = ScriptableObject.CreateInstance<NetworkTransformArcStrategy>();
        strategy.maxSendInterval = _maxSendInterval;
        strategy.extrapolation = _extrapolation;
        strategy.sendConfirmations = _sendConfirmations;
        nt.SetStrategySettings(strategy);
    }
}
