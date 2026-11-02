using UnityEngine;

namespace PurrNet.Utils
{
    public static class ApplicationContext
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            isQuitting = false;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            isQuitting = true;
            Application.quitting -= OnQuitting;
        }

#if UNITY_SERVER && !UNITY_EDITOR
        public static bool isServerBuild => true;
        public static bool isClientBuild => false;
#elif PURRNET_CLIENT_ONLY_BUILDS && !UNITY_EDITOR
        public static bool isServerBuild => false;
        public static bool isClientBuild => true;
#elif !UNITY_EDITOR
        public static bool isServerBuild => Application.isBatchMode;
        public static bool isClientBuild => !Application.isBatchMode;
#else
        public static bool isServerBuild => !Application.isEditor && Application.isBatchMode;
        public static bool isClientBuild => false;
#endif

#if !UNITY_EDITOR
        public static bool isClone => false;
        public static bool isMainEditor => false;
#else
        public static bool isClone => ClonesContext.isClone;
        public static bool isMainEditor => Application.isEditor && !isClone;
#endif

        public static bool isQuitting { get; private set; }
    }
}