using System.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class AsyncPackableTest : NetworkIdentity
{
    [SerializeField] private Renderer _renderer;

    [PurrButton]
    private void TestAsyncPackable()
    {
        RunTest(new NetRenderer() { renderer = _renderer });
    }

    [ObserversRpc(bufferLast: true)]
    private void RunTest(NetRenderer rend)
    {
        if (rend.renderer)
            rend.renderer.material.color = Color.green;
    }

    [System.Serializable]
    private struct NetRenderer : IAsyncPackable
    {
        [DontPack] private string _goName;
        public Renderer renderer;
        
        public async Task PrepareForPackAsync()
        {
            if (!renderer)
                return;
            
            await Task.Delay(300);
            _goName = renderer.gameObject.name;
        }

        public async Task PrepareAfterUnpackAsync()
        {
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
