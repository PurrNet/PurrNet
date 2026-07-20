using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public abstract class Scenario : MonoBehaviour
{
    public virtual void Setup(ScenarioContext ctx, NetworkManager manager) { }

    public abstract UniTask<ScenarioResult> RunScenario(ScenarioContext ctx);

    /// <summary>
    /// Runs a client-side and a server-side phase based on <paramref name="ctx"/>'s role.
    /// On Host, both run concurrently on the same thread; the client function is invoked
    /// first so it can register its callbacks / observers before the server begins acting.
    /// </summary>
    protected static async UniTask<ScenarioResult> RunSplit(
        ScenarioContext ctx,
        Func<ScenarioContext, UniTask<ScenarioResult>> client,
        Func<ScenarioContext, UniTask<ScenarioResult>> server)
    {
        if (ctx.role == NetworkRole.Host)
        {
            var clientTask = client(ctx);
            var serverTask = server(ctx);
            var (cr, sr) = await UniTask.WhenAll(clientTask, serverTask);

            if (cr.success && sr.success)
                return ScenarioResult.Ok();
            if (!cr.success && !sr.success)
                return ScenarioResult.Fail($"client: {cr.message} | server: {sr.message}");
            if (!cr.success)
                return ScenarioResult.Fail($"client: {cr.message}");
            return ScenarioResult.Fail($"server: {sr.message}");
        }

        if (ctx.isServer)
            return await server(ctx);
        return await client(ctx);
    }
}
