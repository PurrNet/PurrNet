using System.Collections.Generic;
using UnityEngine;
using PurrNet;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressablesTest
{
    public class AddressableObjectTester : MonoBehaviour
    {
        public AssetReference prefabReference;
        public string prefabAddress = "MyPrefab";
        
        List<AsyncOperationHandle<GameObject>> _handles = new();
    
        [PurrButton]
        async void SpawnByReference()
        {
            var handle = prefabReference.InstantiateAsync(Random.insideUnitSphere * 3f, Quaternion.identity);
            await handle.Task;
            
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _handles.Add(handle);
                Debug.Log($"Spawned by reference. Total: {_handles.Count}");
            }
        }
    
        [PurrButton]
        async void SpawnByAddress()
        {
            var handle = Addressables.InstantiateAsync(prefabAddress, Random.insideUnitSphere * 3f, Quaternion.identity);
            await handle.Task;
            
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _handles.Add(handle);
                Debug.Log($"Spawned by address. Total: {_handles.Count}");
            }
        }
    
        [PurrButton]
        void DestroyLast()
        {
            if (_handles.Count == 0) return;
            
            var handle = _handles[^1];
            _handles.RemoveAt(_handles.Count - 1);
            
            Addressables.ReleaseInstance(handle);
            Debug.Log($"Destroyed last. Remaining: {_handles.Count}");
        }
    
        [PurrButton]
        void DestroyAll()
        {
            foreach (var handle in _handles)
                Addressables.ReleaseInstance(handle);
            
            _handles.Clear();
            Debug.Log("Destroyed all");
        }
    
        void OnDestroy()
        {
            DestroyAll();
        }
    }
}
