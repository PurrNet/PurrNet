using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Debug/Network Ownership Debug")]
    public class NetworkOwnershipDebug : NetworkIdentity
    {
        [PurrButton]
        public void TakeOwnershipTest()
        {
            GiveOwnership(localPlayer);
        }

        [PurrButton]
        public void ReleaseOwnership()
        {
            RemoveOwnership();
        }
    }
}
