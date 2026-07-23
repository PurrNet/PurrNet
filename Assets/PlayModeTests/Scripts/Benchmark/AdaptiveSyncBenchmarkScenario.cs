using PurrNet;

public class AdaptiveSyncBenchmarkScenario : BenchmarkScenario
{
    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        var nt = CreatePrefab(manager, nameof(AdaptiveSyncBenchmarkScenario) + "_Obj");
        nt.adaptiveSync = true;
    }
}
