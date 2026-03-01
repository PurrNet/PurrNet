using System.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class AsyncPackableTest : NetworkIdentity
{
    [SerializeField] private Renderer _renderer;

    [ContextMenu("Test async packable"), PurrButton]
    private void TestAsyncPackable()
    {
        RunTest(new NetRenderer() { renderer = _renderer });
    }

    [ObserversRpc(bufferLast: true, runLocally: false)]
    private void RunTest(NetRenderer rend)
    {
        if (rend.renderer)
            rend.renderer.material.color = Color.green;
        else
            Debug.LogError($"No renderer resolved for goName='{rend.goName}'");
    }

    [System.Serializable]
    private struct NetRenderer : IAsyncPackable
    {
        public string goName;
        [DontPack] public Renderer renderer;

        public async Task<IAsyncPackable> PrepareForPackAsync()
        {
            if (!renderer)
            {
                Debug.LogWarning("PrepareForPackAsync: renderer is null");
                return this;
            }
            await Task.Delay(1000);
            goName = renderer.gameObject.name;
            return this;
        }

        public async Task<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            await Task.Delay(1000);
            var go = string.IsNullOrEmpty(goName) ? null : GameObject.Find(goName);
            if (!go)
            {
                Debug.LogError($"No GO found by name: '{goName}'");
                return this;
            }
            renderer = go.GetComponent<Renderer>();
            return this;
        }
    }
}
