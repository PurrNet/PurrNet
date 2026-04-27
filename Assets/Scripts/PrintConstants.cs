using PurrNet.Utils;
using UnityEngine;

public class PrintConstants : MonoBehaviour
{
    private void OnGUI()
    {
        GUI.color = Color.white;
        foreach (var (key, val) in ApplicationConstants.constants)
        {
            GUILayout.Label($"{key}: {val}");
        }
    }
}
