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
        Debug.Log($"1 (sender entry)");
        RunTest(new NetRenderer() { renderer = _renderer });
    }

    [ObserversRpc(bufferLast: true, runLocally: false)]
    private void RunTest(NetRenderer rend)
    {
        Debug.Log($"4 (RunTest body)");
        if (rend.renderer)
            rend.renderer.material.color = Color.green;
    }

    [System.Serializable]
    private struct NetRenderer : IAsyncPackable
    {
        private string _goName;       // packed - used to resolve renderer on receiver
        [DontPack] public Renderer renderer;  // not packed - resolved via _goName in PrepareAfterUnpackAsync
        
        public async Task PrepareForPackAsync()
        {
            Debug.Log($"2");
            if (!renderer)
                return;
            
            await Task.Delay(300);
            _goName = renderer.gameObject.name;
        }

        public async Task PrepareAfterUnpackAsync()
        {
            Debug.Log($"3");
            await Task.Delay(300);
            var go = GameObject.Find(_goName);
            if (!go)
            {
                Debug.LogError($"No GO found by name: {_goName}");
                return;
            }

            renderer = go.GetComponent<Renderer>();
        }
    }
}
