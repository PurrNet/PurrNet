using UnityEditor;

namespace PurrNet.Editor
{
    public static class EnableDebugTooling
    {
        const string DEBUG_PATH = "Tools/PurrNet/Features/Debug";

#if PURR_DELTA_CHECK
        [MenuItem(DEBUG_PATH + "/Disable Delta Validator", priority = 105)]
        public static void UninstallDeltaCheck()
        {
            SymbolsHelper.RemoveSymbol("PURR_DELTA_CHECK");
        }
#else
        [MenuItem(DEBUG_PATH + "/Enable Delta Validator", priority = 105)]
        public static void InstallDeltaCheck()
        {
            SymbolsHelper.AddSymbol("PURR_DELTA_CHECK");
        }
#endif

#if PURR_LEAKS_CHECK
        [MenuItem(DEBUG_PATH + "/Disable Leak Detection", priority = 105)]
        public static void Uninstall()
        {
            SymbolsHelper.RemoveSymbol("PURR_LEAKS_CHECK");
        }
#else
        [MenuItem(DEBUG_PATH + "/Enable Leak Detection", priority = 105)]
        public static void Install()
        {
            SymbolsHelper.AddSymbol("PURR_LEAKS_CHECK");
        }
#endif

#if PURRNET_DEBUG_POOLING
        [MenuItem(DEBUG_PATH + "/Disable Pool Debug", priority = 105)]
        public static void UninstallPool()
        {
            SymbolsHelper.RemoveSymbol("PURRNET_DEBUG_POOLING");
        }
#else
        [MenuItem(DEBUG_PATH + "/Enable Pool Debug", priority = 105)]
        public static void InstallPool()
        {
            SymbolsHelper.AddSymbol("PURRNET_DEBUG_POOLING");
        }
#endif
    }
}
