using System.Collections;
using System.Reflection;
using PurrNet;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;

namespace AddressablesTest
{
    //This is absolutely AI made, I'm too lazy to write testing with reflection. Sue me!
    public class AddressableLogger : MonoBehaviour
    {
        [PurrButton, ContextMenu("Log Loaded Addressables")]
        void LogLoaded()
        {
            var rm = Addressables.ResourceManager;
            var field = typeof(ResourceManager).GetField("m_AssetOperationCache", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                Debug.LogError("Could not find m_AssetOperationCache field");
                return;
            }

            var cache = field.GetValue(rm) as IDictionary;
            if (cache == null)
            {
                Debug.LogError("Could not read operation cache");
                return;
            }

            var sb = new System.Text.StringBuilder();
            int count = 0;

            foreach (DictionaryEntry entry in cache)
            {
                count++;
                var op = entry.Value;
                var debugName = op.GetType().GetProperty("DebugName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(op);
                var refCount = op.GetType().GetProperty("ReferenceCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(op);
                sb.AppendLine($"  • {debugName ?? "unknown"} | RefCount: {refCount ?? "?"}");
            }

            Debug.Log($"Cached Addressable Operations ({count}):\n{sb}");
        }
    }
}