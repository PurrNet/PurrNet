using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public interface INetworkTransformPositionTransform
    {
        double3 ToAbsolute(NetworkTransform self, Vector3 localWorldPos);

        Vector3 ToLocal(NetworkTransform self, double3 absolutePosition);
    }
}
