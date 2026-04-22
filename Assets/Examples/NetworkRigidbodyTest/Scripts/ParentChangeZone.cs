using PurrNet;
using UnityEngine;

namespace NetworkRigidbodyTest
{
    [RequireComponent(typeof(Collider))]
    public class ParentChangeZone : NetworkIdentity
    {
        [Tooltip("Parent to assign to entering networked rigidbodies on trigger enter. Leave null to detach.")]
        [SerializeField] private Transform _newParent;

        [Tooltip("Optional local-Y spin speed (deg/s). Use to test reparenting onto a moving transform — leave 0 for a static parent.")]
        [SerializeField] private float _spinDegreesPerSecond;

        private void Update()
        {
            if (_spinDegreesPerSecond != 0f)
                transform.Rotate(0f, _spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!TryGetController(other, out var child))
                return;

            child.SetParent(_newParent, worldPositionStays: true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!TryGetController(other, out var child))
                return;

            if (child.parent == _newParent)
                child.SetParent(null, worldPositionStays: true);
        }

        private static bool TryGetController(Collider other, out Transform child)
        {
            child = null;
            var rb = other.attachedRigidbody;
            if (!rb)
                return false;

            var nrb = rb.GetComponent<NetworkRigidbody>();
            if (!nrb || !nrb.isController)
                return false;

            child = nrb.transform;
            return true;
        }
    }
}
