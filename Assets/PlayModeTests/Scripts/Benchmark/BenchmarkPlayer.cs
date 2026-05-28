using System.Collections.Generic;
using PurrNet;
using UnityEngine;

// Plain MonoBehaviour (NOT a second NetworkIdentity) sitting next to the NetworkTransform so the
// NT remains the only identity and owns the replication. Each instance random-walks its transform
// only on the peer that controls it; the owner-auth NetworkTransform relays the movement.
[RequireComponent(typeof(NetworkTransform))]
public class BenchmarkPlayer : MonoBehaviour
{
    public static readonly List<BenchmarkPlayer> All = new();

    private const float Speed = 6f;
    private const float TurnChance = 0.02f;
    private const float Bound = 25f;

    private NetworkTransform _nt;
    private Vector3 _velocity;

    public bool isController => _nt && _nt.isController;

    private void Awake()
    {
        _nt = GetComponent<NetworkTransform>();
        _velocity = RandomDir() * Speed;
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    public void Step(float dt)
    {
        if (Random.value < TurnChance)
            _velocity = RandomDir() * Speed;

        var p = transform.position + _velocity * dt;

        if (p.x < -Bound || p.x > Bound) { _velocity.x = -_velocity.x; p.x = Mathf.Clamp(p.x, -Bound, Bound); }
        if (p.z < -Bound || p.z > Bound) { _velocity.z = -_velocity.z; p.z = Mathf.Clamp(p.z, -Bound, Bound); }

        transform.position = p;
    }

    private static Vector3 RandomDir()
    {
        var a = Random.value * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }
}
