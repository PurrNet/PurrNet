using System.Collections.Generic;
using UnityEngine;
using PurrNet;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressablesTest
{
    public class AddressableObjectTester : NetworkIdentity
    {
        public AssetReferenceGameObject prefabReference;
        
        List<AsyncOperationHandle<GameObject>> _handles = new();

        private List<GameObject> _spawned = new();
    
        [PurrButton]
        void SpawnByReference()
        {
            var go = networkManager.SpawnAddressable(prefabReference);
            _spawned.Add(go);
            Debug.Log($"[{go.name}] Spawn by reference. | Total spawned: {_spawned}", go);
        }
    
        [PurrButton]
        void DespawnByReference()
        {
            if (_spawned.Count == 0) return;
            
            var go = _spawned[^1];
            _spawned.Remove(go);
            networkManager.DespawnAddressable(go);
            Debug.Log($"[{go.name}] Despawn by reference. | Total spawned: {_spawned}", go);
        }
    
        [PurrButton]
        async void InstantiateByReference()
        {
            var handle = prefabReference.InstantiateAsync(Random.insideUnitSphere * 3f, Quaternion.identity);
            await handle.Task;
            
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _handles.Add(handle);
                Debug.Log($"Instantiate by reference. Total: {_handles.Count}");
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
