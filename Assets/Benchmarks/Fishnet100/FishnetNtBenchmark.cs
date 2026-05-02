using PurrNet;
using UnityEngine;

public class FishnetNtBenchmark : NetworkIdentity
{
    [SerializeField] private GameObject _fishPrefab;
    [SerializeField] private int _fishCount = 100;


    protected override void OnSpawned(bool asServer)
    {
        if (!asServer)
            return;

        for (int i = 0; i < _fishCount; i++)
        {
            Instantiate(_fishPrefab, Random.insideUnitSphere * 10f, Random.rotation);
        }
    }
}
