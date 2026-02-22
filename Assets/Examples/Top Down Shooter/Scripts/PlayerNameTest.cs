using PurrNet;
using UnityEngine;
using Random = UnityEngine.Random;

public class PlayerNameTest : NetworkIdentity
{
    public SyncVar<string> PlayerName = new SyncVar<string>("Loading...", ownerAuth: true);

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        Debug.Log($"Owner changed from {oldOwner} to {newOwner} asServer: {asServer}; owner {owner}; me: {localPlayer}", this);
        if (!isOwner) return;

        PlayerName.value = Random.Range(1, 1000).ToString();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && isController)
        {
            Test("Hello World");
        }
    }

    [ObserversRpc]
    static void Test(string wtf)
    {
        Debug.Log(wtf + " fefe");
    }
}
