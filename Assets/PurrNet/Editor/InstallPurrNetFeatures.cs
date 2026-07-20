using UnityEditor;

namespace PurrNet.Editor
{
    public static class InstallPurrNetFeatures
    {
#if PURR_MTU_DEBUGGING
        [MenuItem("Tools/PurrNet/Features/Debug/Disable MTU Debugging", priority = 105)]
        public static void Uninstall_PURR_MTU_DEBUGGING()
        {
            SymbolsHelper.RemoveSymbol("PURR_MTU_DEBUGGING");
        }
#else
        [MenuItem("Tools/PurrNet/Features/Debug/Enable MTU Debugging", priority = 105)]
        public static void Install_PURR_MTU_DEBUGGING()
        {
            SymbolsHelper.AddSymbol("PURR_MTU_DEBUGGING");
        }
#endif

#if PURR_RUNTIME_PROFILING
        [MenuItem("Tools/PurrNet/Features/Disable Runtime Profiling", priority = 105)]
        public static void Uninstall_PURR_RUNTIME_PROFILING()
        {
            SymbolsHelper.RemoveSymbol("PURR_RUNTIME_PROFILING");
        }
#else
        [MenuItem("Tools/PurrNet/Features/Enable Runtime Profiling", priority = 105)]
        public static void Install_PURR_RUNTIME_PROFILING()
        {
            SymbolsHelper.AddSymbol("PURR_RUNTIME_PROFILING");
        }
#endif

#if PURR_BUTTONS
        [MenuItem("Tools/PurrNet/Features/Disable PurrButtons", priority = 105)]
        public static void UninstallPurrButtons()
        {
            SymbolsHelper.RemoveSymbol("PURR_BUTTONS");
        }
#else
        [MenuItem("Tools/PurrNet/Features/Enable PurrButtons", priority = 105)]
        public static void InstallPurrButtons()
        {
            SymbolsHelper.AddSymbol("PURR_BUTTONS");
        }
#endif

#if PURR_CONTEXT_BUTTONS
        [MenuItem("Tools/PurrNet/Features/Disable PurrContextButtons", priority = 105)]
        public static void UninstallPURR_CONTEXT_BUTTONS()
        {
            SymbolsHelper.RemoveSymbol("PURR_CONTEXT_BUTTONS");
        }
#else
        [MenuItem("Tools/PurrNet/Features/Enable PurrContextButtons", priority = 105)]
        public static void InstallPURR_CONTEXT_BUTTONS()
        {
            SymbolsHelper.AddSymbol("PURR_CONTEXT_BUTTONS");
        }
#endif

#if PURR_ENDIAN
        [MenuItem("Tools/PurrNet/Features/Disable Endianness Check", priority = 105)]
        public static void UninstallEndianness()
        {
            SymbolsHelper.RemoveSymbol("PURR_ENDIAN");
        }
#else
        [MenuItem("Tools/PurrNet/Features/Enable Endianness Check", priority = 105)]
        public static void InstallEndianness()
        {
            SymbolsHelper.AddSymbol("PURR_ENDIAN");
        }
#endif

#if PURRNET_DISABLE_ASSET_REGISTRY_AUTO_GENERATION
        [MenuItem("Tools/PurrNet/Features/Enable Asset Registry Auto Generation", priority = 105)]
        private static void InstallAssetRegistryAutoGeneration()
        {
            SymbolsHelper.RemoveSymbol("PURRNET_DISABLE_ASSET_REGISTRY_AUTO_GENERATION");
        }
#else
        [MenuItem("Tools/PurrNet/Features/Disable Asset Registry Auto Generation", priority = 105)]
        private static void UninstallAssetRegistryAutoGeneration()
        {
            SymbolsHelper.AddSymbol("PURRNET_DISABLE_ASSET_REGISTRY_AUTO_GENERATION");
        }
#endif
    }
}
