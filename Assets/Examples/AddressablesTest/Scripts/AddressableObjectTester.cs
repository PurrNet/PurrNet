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
        async void SpawnByReference()
        {
            var go = await networkManager.SpawnAddressableAsync(prefabReference);
            if (!go) return;
            _spawned.Add(go);
            Debug.Log($"[{go.name}] Spawn by reference. | Total spawned: {_spawned.Count}", go);
        }

        [PurrButton]
        void DespawnByReference()
        {
            if (_spawned.Count == 0) return;

            var go = _spawned[^1];
            _spawned.Remove(go);
            networkManager.DespawnAddressable(go);
            Debug.Log($"[{go.name}] Despawn by reference. | Total spawned: {_spawned.Count}", go);
        }

        [PurrButton]
        async void InstantiateByReference()
        {
            var handle = prefabReference.InstantiateAsync(Random.insideUnitSphere * 3f, Quaternion.identity);
            await handle.Task;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _handles.Add(handle);
                Debug.Log($"[{handle.Result.name}] InstantiateAsync by reference. Total: {_handles.Count}", handle.Result);
            }
        }

        [PurrButton]
        async void InstantiateByGuid()
        {
            var handle = Addressables.InstantiateAsync(prefabReference.AssetGUID, Random.insideUnitSphere * 3f, Quaternion.identity);
            await handle.Task;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _handles.Add(handle);
                Debug.Log($"[{handle.Result.name}] InstantiateAsync by GUID. Total: {_handles.Count}", handle.Result);
            }
        }

        [PurrButton]
        void ReleaseLast()
        {
            if (_handles.Count == 0) return;

            var handle = _handles[^1];
            _handles.RemoveAt(_handles.Count - 1);

            Addressables.ReleaseInstance(handle);
            Debug.Log($"Released last. Remaining: {_handles.Count}");
        }

        [PurrButton]
        private void SendTestReference()
        {
            TestSendAddressableReference(prefabReference);
        }

        [ObserversRpc]
        private void TestSendAddressableReference(NetworkAddressable addressable)
        {
            Debug.Log($"Received addressable: {addressable}", addressable.Asset);
        }

        [PurrButton]
        void ReleaseAll()
        {
            foreach (var handle in _handles)
                Addressables.ReleaseInstance(handle);

            _handles.Clear();
            Debug.Log("Released all");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseAll();
        }
    }
}
