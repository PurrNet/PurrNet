using PurrNet;
using UnityEngine;

public class PositionWithSyncVar : NetworkIdentity
{
    [SerializeField] private SyncVar<Vector3> _position = new (ownerAuth: true);

    float timer = 3;
    Vector3 moveDir = Vector3.zero;

    void Update()
    {
        if (isOwner)
        {
            transform.position += moveDir * Time.deltaTime;
            timer += 1 * Time.deltaTime;
            if (timer > 3)
            {
                transform.position = Vector3.zero;
                moveDir = Vector3.left * Random.Range(-2, 2);
                moveDir += Vector3.forward * Random.Range(-2, 2);
                timer = 0;
            }

            _position.value = transform.position;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, _position, 8 * Time.deltaTime);
        }
    }
}
