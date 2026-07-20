using System.Collections.Generic;
using UnityEngine;
using PurrNet.Logging;
using PurrNet.Packing;

namespace PurrNet.Examples.TopDownShooter
{
    public class PlayerBody : NetworkIdentity
    {
        [SerializeField] private List<GameObject> bodies = new List<GameObject>();

        [SerializeField] private int _random;

        protected override void OnSerialize(BitPacker packer)
        {
            if (_random == default)
                _random = Random.Range(int.MinValue, int.MaxValue);
            Debug.Log("Serializing " + _random);
            Packer<int>.Write(packer, _random);
        }

        protected override void OnDeserialize(BitPacker packer)
        {
            Packer<int>.Read(packer, ref _random);
            Debug.Log("Deserialized " + _random);
        }

        protected override void OnSpawned(bool asServer)
        {
            if (!owner.HasValue)
            {
                PurrLogger.LogError($"No owner for player {asServer}", this);
                return;
            }

            int index = 0;
            if (owner.Value.id != 0)
                index = (int)(owner.Value.id % (ulong)bodies.Count);

            for (int i = 0; i < bodies.Count; i++)
            {
                bodies[i].SetActive(i == index);
            }
        }
    }
}
