using PurrNet;
using PurrNet.Examples.TopDownShooter;
using UnityEngine;

public class ParentSnapperTest : MonoBehaviour
{
    [PurrButton]
    private void DoSetup()
    {
        if (PlayerMovement.TryGetLocal(out var localPlayer))
        {
            localPlayer.enabled = false;
            localPlayer.controller.enabled = false;
            localPlayer.transform.SetParent(transform);
            localPlayer.transform.localPosition = transform.position;
            localPlayer.transform.localRotation = Quaternion.identity;
        }
    }

    [PurrButton]
    private void LetGo()
    {
        if (PlayerMovement.TryGetLocal(out var localPlayer))
        {
            localPlayer.enabled = true;
            localPlayer.controller.enabled = true;
            localPlayer.transform.SetParent(null);
        }
    }
}
