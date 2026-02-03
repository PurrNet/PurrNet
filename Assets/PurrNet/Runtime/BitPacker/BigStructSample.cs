using UnityEngine;

namespace PurrNet.Packing
{
    /// <summary>
    /// Sample struct with multiple fields for DeltaPacker Analysis visualization.
    /// Implements IPackedAuto so Packer/DeltaPacker are codegen'd; use it to compare
    /// full pack vs delta (0 fields changed) vs delta (1 field changed) vs delta (all changed).
    /// </summary>
    public struct BigStructSample : IPackedAuto
    {
        public int Id;
        public float Health;
        public Vector3 Position;
        public Quaternion Rotation;
        public int State;
        public float Speed;

        public static BigStructSample Default => new BigStructSample
        {
            Id = 0,
            Health = 100f,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            State = 0,
            Speed = 5f
        };

        /// <summary>Same as Default except Health = 50 (1 field changed).</summary>
        public static BigStructSample OneFieldChanged => new BigStructSample
        {
            Id = 0,
            Health = 50f,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            State = 0,
            Speed = 5f
        };

        /// <summary>All fields different from Default.</summary>
        public static BigStructSample AllFieldsChanged => new BigStructSample
        {
            Id = 42,
            Health = 25f,
            Position = Vector3.one,
            Rotation = Quaternion.Euler(90f, 0f, 0f),
            State = 3,
            Speed = 12f
        };
    }
}
