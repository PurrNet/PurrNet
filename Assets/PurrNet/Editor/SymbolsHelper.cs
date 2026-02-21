using System;
using UnityEditor;
using UnityEditor.Build;

namespace PurrNet.Editor
{
    public static class SymbolsHelper
    {
        public static void RemoveSymbol(string symbol)
        {
            var activeBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);

            var content = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
            int idxOf = content.IndexOf(symbol, StringComparison.Ordinal);
            if (idxOf == -1)
                return;
            bool isNextSemicolon = idxOf < content.Length - 1 && content[idxOf + 1] == ';';
            if (isNextSemicolon)
                idxOf++;
            content = content.Remove(idxOf, symbol.Length);
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, content);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void AddSymbol(string symbol)
        {
            var activeBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);

            var content = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
            bool needsSemicolon = content.Length > 0 && content[^1] != ';';
            content += needsSemicolon ? ";" : "";
            content += symbol;
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, content);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
