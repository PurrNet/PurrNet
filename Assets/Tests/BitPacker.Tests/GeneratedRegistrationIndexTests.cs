using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Packing;

namespace PurrNet.Tests
{
    public sealed class GeneratedRegistrationIndexTests
    {
        [Test]
        public void GeneratedIndicesMatchLegacyDiscoveryIncludingPrivateRegistrations()
        {
            foreach (var assembly in new[] { typeof(NetworkManager).Assembly, typeof(GeneratedRegistrationIndexTests).Assembly })
            {
                var entries = ReadIndex(assembly);
                var actual = entries.Select(entry => Describe(
                    (MethodInfo)MethodBase.GetMethodFromHandle(entry.method, entry.declaringType), entry.priority));
                var expected = assembly.GetTypes().Where(type => type.IsAbstract && type.IsSealed)
                    .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    .Select(method => (method, attribute: method.GetCustomAttributes(false).OfType<RegisterPackersAttribute>().FirstOrDefault()))
                    .Where(entry => entry.attribute != null)
                    .Select(entry => Describe(entry.method, entry.attribute.priority));
                Assert.That(actual.ToArray(), Is.EqualTo(expected.ToArray()),
                    assembly.GetName().Name);
            }
        }

        [Test]
        public void PrivateMethodHandlesInvokeInTheExistingPriorityOrder()
        {
            PrivateRegistrations.calls.Clear();
            var entries = ReadIndex(typeof(GeneratedRegistrationIndexTests).Assembly)
                .Select(entry => (method: (MethodInfo)MethodBase.GetMethodFromHandle(entry.method, entry.declaringType), entry.priority))
                .Where(entry => entry.method.DeclaringType == typeof(PrivateRegistrations))
                .OrderBy(entry => entry.priority).ToArray();
            Assert.That(entries.Length, Is.EqualTo(2));
            foreach (var entry in entries) entry.method.Invoke(null, null);
            Assert.That(PrivateRegistrations.calls, Is.EqualTo(new[] { -7, 9 }));
        }

        private static PackerRegistrationEntry[] ReadIndex(Assembly assembly)
        {
            var index = assembly.GetType(PackerRegistrationEntry.IndexTypeName, false);
            Assert.That(index, Is.Not.Null, "The actual postprocessed assembly needs a generated index");
            return (PackerRegistrationEntry[])index.GetMethod(PackerRegistrationEntry.IndexMethodName,
                BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        }

        private static string Describe(MethodInfo method, int priority)
            => method.DeclaringType.FullName + "/" + method + ":" + priority;

        private static class PrivateRegistrations
        {
            internal static readonly List<int> calls = new();

            [RegisterPackers(9)]
            private static void Later() => calls.Add(9);

            [RegisterPackers(-7)]
            private static void Earlier() => calls.Add(-7);
        }
    }
}
