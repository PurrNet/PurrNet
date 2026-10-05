using System;
using System.Collections.Generic;
using System.Reflection;
using PurrNet;
using UnityEngine;

public class NRBHandoffMover : NetworkIdentity
{
    public struct Sample
    {
        public float time;
        public float x;
        public bool controller;
        public bool hasOwner;
        public ulong ownerId;
        public double snapshotLead;
    }

    public const float Speed = 3.5f;

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo _bufferCountField = typeof(NetworkRigidbodyBase).GetField("_bufferCount", PrivateInstance);
    private static readonly FieldInfo _bufferHeadField = typeof(NetworkRigidbodyBase).GetField("_bufferHead", PrivateInstance);
    private static readonly FieldInfo _bufferField = typeof(NetworkRigidbodyBase).GetField("_snapshotBuffer", PrivateInstance);

    public static NRBHandoffMover localInstance;

    public readonly List<Sample> samples = new(4096);

    private NetworkRigidbody _nrb;
    private Rigidbody _rb;
    private bool _active;

    public static void ResetAll() => localInstance = null;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => localInstance = this;

    protected override void OnDespawned()
    {
        if (localInstance == this)
            localInstance = null;
    }

    private void Awake()
    {
        _nrb = GetComponent<NetworkRigidbody>();
        _rb = GetComponent<Rigidbody>();
    }

    public void Begin()
    {
        samples.Clear();
        _active = true;
    }

    public void End()
    {
        _active = false;
        if (_nrb && _nrb.isController)
            NetworkRigidbodyPhysics.SetLinearVelocity(_rb, Vector3.zero);
    }

    private void FixedUpdate()
    {
        if (_active && _nrb && _nrb.isController)
            NetworkRigidbodyPhysics.SetLinearVelocity(_rb, Speed * Vector3.right);
    }

    private void LateUpdate()
    {
        if (!_active || !_nrb)
            return;

        var owner = _nrb.owner;
        samples.Add(new Sample
        {
            time = Time.unscaledTime,
            x = transform.position.x,
            controller = _nrb.isController,
            hasOwner = owner.HasValue,
            ownerId = owner?.id.value ?? 0,
            snapshotLead = NewestSnapshotLead()
        });
    }

    private double NewestSnapshotLead()
    {
        int count = (int)_bufferCountField.GetValue(_nrb);
        if (count == 0)
            return 0;

        var buffer = (Array)_bufferField.GetValue(_nrb);
        int head = (int)_bufferHeadField.GetValue(_nrb);
        object newest = buffer.GetValue((head - 1 + buffer.Length) % buffer.Length);
        double time = (double)newest.GetType().GetField("time").GetValue(newest);
        return time - (Time.unscaledTimeAsDouble + NetworkRigidbodyBase.clockSkewForTests);
    }
}
