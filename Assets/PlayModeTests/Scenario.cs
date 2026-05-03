using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public abstract class Scenario : MonoBehaviour
{
    public virtual void Setup(ScenarioContext ctx, NetworkManager manager) { }

    public abstract UniTask<ScenarioResult> RunScenario(ScenarioContext ctx);
}
