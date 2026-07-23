using System;
using UnityEngine;
using PurrNet;

public class JumpTest : NetworkIdentity
{
    [SerializeField] private float _jumpForce = 10;
    [SerializeField] private float _gravity = 9.81f;
    [SerializeField] private Rigidbody _rigidbody;
    
    protected override void OnSpawned()
    {
        base.OnSpawned();
        _rigidbody.isKinematic = !isOwner;
        _rigidbody.useGravity = false;
    }

    private void FixedUpdate()
    {
        if (!isOwner)
            return;
        
        _rigidbody.AddForce(Vector3.down * _gravity);
    }

    private void Update()
    {
        if (!isOwner)
            return;

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Mouse0))
            _rigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
    }
}
