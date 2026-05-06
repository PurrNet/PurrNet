using System;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Centralized OnGUI dispatcher.
    /// Reduces GC overhead by having a single OnGUI MonoBehaviour
    /// instead of one per component.
    /// </summary>
    public static class PurrOnGUI
    {
        private static event Action _onGUI;
        private static PurrOnGUIRunner _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _onGUI = null;
            _runner = null;
        }

        public static void Subscribe(Action callback)
        {
            _onGUI += callback;
            EnsureRunner();
        }

        public static void Unsubscribe(Action callback)
        {
            _onGUI -= callback;
        }

        private static void EnsureRunner()
        {
            if (_runner)
                return;

            var go = new GameObject("[PurrNet] OnGUI");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<PurrOnGUIRunner>();
        }

        private class PurrOnGUIRunner : MonoBehaviour
        {
            private void OnGUI()
            {
                _onGUI?.Invoke();
            }
        }
    }
}
