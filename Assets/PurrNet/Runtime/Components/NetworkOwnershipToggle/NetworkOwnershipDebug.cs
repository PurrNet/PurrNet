namespace PurrNet
{
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
