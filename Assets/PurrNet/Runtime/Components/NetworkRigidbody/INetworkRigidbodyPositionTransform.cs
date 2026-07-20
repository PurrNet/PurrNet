using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Pluggable origin converter for <see cref="NetworkRigidbody"/>.
    ///
    /// In an origin-shifted world each peer runs with a different local Unity
    /// origin, so a raw <see cref="Vector3"/> position is meaningless on another
    /// peer and loses precision long before a large world reaches the floor of
    /// double precision. This converter is invoked only at the wire boundary:
    /// outgoing positions are mapped into a shared, peer-agnostic absolute frame
    /// (a <see cref="double3"/>) and incoming absolute positions are mapped back
    /// into this peer's Unity world space.
    ///
    /// The absolute <see cref="double3"/> is what travels on the wire and what
    /// the interpolation buffer stores, so a local origin shift needs no extra
    /// bookkeeping — the next <see cref="ToLocal"/> simply reflects the new
    /// offset and every buffered snapshot stays correct.
    ///
    /// Resolution order on spawn (first non-null wins):
    /// 1. Runtime override set via <see cref="NetworkRigidbody.SetPositionTransform"/>.
    /// 2. Sibling component implementing this interface on the same GameObject.
    /// 3. Static fallback at <see cref="NetworkRigidbody.defaultPositionTransform"/>.
    ///
    /// When none is installed, positions travel on the wire as the legacy
    /// quantized <c>CompressedVector3</c> in this peer's own Unity world space
    /// (byte-identical to prior versions). Only translation is converted;
    /// rotation and velocity are origin-invariant.
    /// </summary>
    public interface INetworkRigidbodyPositionTransform
    {
        /// <summary>
        /// Sender side. Convert <paramref name="localWorldPos"/> (a position in this
        /// peer's Unity world space) into the shared peer-agnostic absolute frame
        /// that travels on the wire — e.g. add this peer's origin offset.
        /// </summary>
        double3 ToAbsolute(NetworkRigidbody self, Vector3 localWorldPos);

        /// <summary>
        /// Receiver side. Convert <paramref name="absolutePosition"/> (a position in
        /// the shared peer-agnostic absolute frame) back into this peer's Unity world
        /// space — e.g. subtract this peer's origin offset. Must be the inverse of
        /// <see cref="ToAbsolute"/> for the current origin.
        /// </summary>
        Vector3 ToLocal(NetworkRigidbody self, double3 absolutePosition);
    }
}
