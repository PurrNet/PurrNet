using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Utils;

namespace PurrNet.Codegen.Tests
{
    public sealed class AssemblyDependencyScopeTests
    {
        [TestCase("Direct", true)]
        [TestCase("Indirect", true)]
        [TestCase("Forwarder", true)]
        [TestCase("Unrelated", false)]
        [TestCase("CycleA", false)]
        [TestCase("ReachableCycleB", true)]
        [TestCase("MissingDependency", true)]
        [TestCase("UnknownReferences", true)]
        public void TransitiveScopeDoesNotExcludePossibleSubclasses(string source, bool expected)
        {
            var graph = new Dictionary<string, string[]>
            {
                ["Root"] = Array.Empty<string>(),
                ["Direct"] = new[] { "Root" },
                ["Indirect"] = new[] { "Direct" },
                ["Forwarder"] = new[] { "Indirect" },
                ["Unrelated"] = new[] { "Core" },
                ["Core"] = Array.Empty<string>(),
                ["CycleA"] = new[] { "CycleB" },
                ["CycleB"] = new[] { "CycleA" },
                ["ReachableCycleA"] = new[] { "ReachableCycleB", "Root" },
                ["ReachableCycleB"] = new[] { "ReachableCycleA" },
                ["MissingDependency"] = new[] { "NotLoaded" },
                ["UnknownReferences"] = null
            };
            var method = typeof(AssemblyDependencyScope).GetMethod("MayReference", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)method.Invoke(null, new object[] { source, "Root", graph, new HashSet<string>() }), Is.EqualTo(expected));
        }

        [Test]
        public void CoreLibraryIsExcludedAndTheDefiningAssemblyIsIncluded()
        {
            var root = typeof(NetworkManager).Assembly;
            var core = typeof(object).Assembly;
            var actual = AssemblyDependencyScope.Find(new[] { core, root }, root);
            Assert.That(actual.Contains(root), Is.True);
            Assert.That(actual.Contains(core), Is.False);
        }

        [Test]
        public void PlayerIdentityCandidatesMatchTheExistingDiscovery()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var candidates = AssemblyDependencyScope.Find(assemblies, typeof(PlayerIdentity<>).Assembly);
            var expected = Discover(assemblies);
            var actual = Discover(assemblies.Where(candidates.Contains));
            Assert.That(expected, Does.Contain(typeof(ScopePlayerIdentity).AssemblyQualifiedName));
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static string[] Discover(IEnumerable<Assembly> assemblies)
        {
            return assemblies.Where(a => !a.FullName.StartsWith("Unity.") && !a.FullName.StartsWith("Microsoft."))
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
                })
                .Where(t => !t.IsAbstract && t.BaseType != null && t.BaseType.IsGenericType &&
                    t.BaseType.GetGenericTypeDefinition() == typeof(PlayerIdentity<>))
                .Select(t => t.AssemblyQualifiedName).ToArray();
        }
    }

    public sealed class ScopePlayerIdentity : PlayerIdentity<ScopePlayerIdentity> { }
}
