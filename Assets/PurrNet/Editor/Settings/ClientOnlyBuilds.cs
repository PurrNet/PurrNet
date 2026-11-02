using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace PurrNet.Editor
{
    public static class ClientOnlyBuilds
    {
        public const string Define = "PURRNET_CLIENT_ONLY_BUILDS";

        public static bool isEnabled
        {
            get
            {
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out var defines);
                return defines.Contains(Define);
            }
            set
            {
                foreach (var target in GetClientTargets())
                {
                    PlayerSettings.GetScriptingDefineSymbols(target, out var defines);
                    bool has = defines.Contains(Define);
                    if (has == value)
                        continue;

                    var updated = value ? defines.Append(Define) : defines.Where(d => d != Define);
                    PlayerSettings.SetScriptingDefineSymbols(target, updated.ToArray());
                }

                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Server, out var serverDefines);
                if (serverDefines.Contains(Define))
                    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Server,
                        serverDefines.Where(d => d != Define).ToArray());

                AssetDatabase.SaveAssets();
            }
        }

        static IEnumerable<NamedBuildTarget> GetClientTargets()
        {
            var seen = new HashSet<string>();

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

                var field = typeof(BuildTargetGroup).GetField(group.ToString());
                if (field == null || field.IsDefined(typeof(ObsoleteAttribute), false))
                    continue;

                NamedBuildTarget target;
                try
                {
                    target = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (target == NamedBuildTarget.Server || !seen.Add(target.TargetName))
                    continue;

                yield return target;
            }
        }
    }
}
