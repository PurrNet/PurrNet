using PurrNet;
using UnityEngine;

namespace AddressablesTest
{
    public class TestAddressable : NetworkIdentity
    {
        protected override void OnSpawned()
        {
            base.OnSpawned();
            if (isController)
            {
                transform.position += new Vector3(Random.Range(-5f, 5f), Random.Range(0f, 2f), Random.Range(-5f, 5f));
                TestRpc(); 
            }
        }

        [ObserversRpc]
        private void TestRpc()
        {
            Debug.Log($"Yo dawg, I was just spawned!");
        }

        [ObserversRpc, PurrButton]
        private void AnotherTestRpc()
        {
            Debug.Log($"WITNESS ME!");
        }
    }
}
