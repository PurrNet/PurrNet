using System;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("")]
    public class UnityUpdate : MonoBehaviour
    {
        private static UnityUpdate _instance;

        public static event Action onUpdate;
        public static event Action onLateUpdate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            onUpdate = null;
            onLateUpdate = null;

            if (_instance)
                return;

            var go = new GameObject("PurrNet_UnityUpdate")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<UnityUpdate>();
        }

        private void Update()
        {
            onUpdate?.Invoke();
        }

        private void LateUpdate()
        {
            onLateUpdate?.Invoke();
        }
    }
}
