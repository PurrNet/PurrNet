#if UNITY_EDITOR
using UnityEditor;

namespace PurrNet.Editor
{
    internal static class PurrnetDefineSymbol
    {
        const string DefineSymbol = "PURRNET";

        [InitializeOnLoadMethod]
        public static void AddDefineSymbols() => SymbolsHelper.AddSymbol(DefineSymbol);

    }
}
#endif