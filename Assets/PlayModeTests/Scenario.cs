using Cysharp.Threading.Tasks;
using UnityEngine;

public abstract class Scenario : MonoBehaviour
{
    public abstract UniTask<ScenarioResult> RunScenario(ScenarioContext ctx);
}
