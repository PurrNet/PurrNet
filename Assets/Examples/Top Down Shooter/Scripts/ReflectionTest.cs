using PurrNet;
using UnityEngine;

public class ReflectionTest : MonoBehaviour
{
    [PurrButton]
    private void TestServer()
    {
        Debug.Log($"Server!");
    }
    
    [PurrButton]
    private void TestObserver()
    {
        Debug.Log($"Observer!");
    }
}
