using UnityEngine;

public class WheelSpawningTest : MonoBehaviour
{
    [SerializeField] private GameObject _wheelPrefab;
    [SerializeField] private Transform[] _spawnPoints;

    private int _spawnPointIndex = 0;

    private void OnMouseUpAsButton()
    {
        var wheel = Instantiate(_wheelPrefab, transform, true);
        int idx = _spawnPointIndex++ % _spawnPoints.Length;
        var trs = _spawnPoints[idx];

        wheel.transform.position = trs.position;
        wheel.transform.rotation = trs.rotation;
    }
}
