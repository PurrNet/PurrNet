using System;
using System.Linq;
using System.Reflection;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// The point of this is to properly handle initializing PlayerIdentities with generics
    /// to remain safe for domain reload as well as remain AOT-safe
    /// </summary>
    public static class PlayerIdentityInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializePurrNetGenerics()
        {
            var genericTypeDefinition = typeof(PlayerIdentity<>);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var candidates = AssemblyDependencyScope.Find(assemblies, genericTypeDefinition.Assembly);
            var allDerivedTypes = assemblies
                .Where(a => 
                    candidates.Contains(a) &&
                    !a.FullName.StartsWith("Unity.") && 
                    !a.FullName.StartsWith("Microsoft."))
                .SelectMany(a => 
                {
                    try
                    {
                        return DerivedTypeDiscovery.ReadIndex(a, DerivedTypeDiscovery.PlayerIdentityIndexTypeName) ??
                               a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        Debug.LogError($"Failed to load types from assembly {a.FullName}. Error: {ex.Message}");
                        return ex.Types.Where(t => t != null);
                    }
                })
                .Where(t => 
                    !t.IsAbstract && 
                    t.BaseType != null && 
                    t.BaseType.IsGenericType && 
                    t.BaseType.GetGenericTypeDefinition() == genericTypeDefinition);

            const string initMethodName = "Init";
        
            foreach (var concreteType in allDerivedTypes)
            {
                var specificGenericType = genericTypeDefinition.MakeGenericType(concreteType);
            
                var initMethod = specificGenericType.GetMethod(
                    initMethodName, 
                    BindingFlags.Static | BindingFlags.NonPublic); 

                if (initMethod != null)
                {
                    initMethod.Invoke(null, null);
                }
            }
        }
    }
}
