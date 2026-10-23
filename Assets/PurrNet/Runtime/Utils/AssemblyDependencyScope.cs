using System;
using System.Collections.Generic;
using System.Reflection;

namespace PurrNet.Utils
{
    public static class AssemblyDependencyScope
    {
        // A subclass must depend on its base type's assembly, possibly through
        // another assembly or a type forwarder. Unresolved references stay eligible.
        public static HashSet<Assembly> Find(Assembly[] loaded, Assembly target)
        {
            var names = new Dictionary<Assembly, string>();
            var references = new Dictionary<string, string[]>(StringComparer.Ordinal);
            for (int i = 0; i < loaded.Length; i++)
            {
                var assembly = loaded[i];
                string name;
                try { name = assembly.GetName().Name; }
                catch { continue; }
                if (name == null) continue;
                names[assembly] = name;
                if (references.ContainsKey(name))
                {
                    // Multiple load contexts or versions cannot be distinguished by
                    // simple names alone. Keep them and their dependents eligible.
                    references[name] = null;
                    continue;
                }
                try
                {
                    var dependencies = assembly.GetReferencedAssemblies();
                    var dependencyNames = new string[dependencies.Length];
                    for (int j = 0; j < dependencies.Length; j++)
                        dependencyNames[j] = dependencies[j].Name;
                    references[name] = dependencyNames;
                }
                catch { references[name] = null; }
            }

            var targetName = target.GetName().Name;
            var result = new HashSet<Assembly>();
            for (int i = 0; i < loaded.Length; i++)
            {
                var assembly = loaded[i];
                if (assembly == target || !names.TryGetValue(assembly, out var name) ||
                    MayReference(name, targetName, references, new HashSet<string>(StringComparer.Ordinal)))
                    result.Add(assembly);
            }
            return result;
        }

        private static bool MayReference(string name, string target, Dictionary<string, string[]> references,
            HashSet<string> visited)
        {
            if (name == target) return true;
            if (name == null || !references.TryGetValue(name, out var dependencies) || dependencies == null)
                return true;
            if (!visited.Add(name)) return false;
            for (int i = 0; i < dependencies.Length; i++)
                if (MayReference(dependencies[i], target, references, visited)) return true;
            return false;
        }
    }
}
