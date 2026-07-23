using System;
using UnityEngine;
using PurrNet;

public class JumpTest : NetworkIdentity
{
    [SerializeField] private float _jumpForce = 10;
    [SerializeField] private float _gravity = 9.81f;
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private Rigidbody _rigidbody;
    [SerializeField] private NetworkAnimator _animator;
    
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
        var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        _rigidbody.linearVelocity = new Vector3(input.x * _moveSpeed, _rigidbody.linearVelocity.y, input.y * _moveSpeed);
    }

    private void Update()
    {
        if (!isOwner)
            return;

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Mouse0))
            _rigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        _animator.SetFloat("Input", input.magnitude);
    }
}
