using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Codegen.Tests
{
    public sealed class GeneratedEqualityDispatchTests
    {
        private ModuleDefinition _module;

        [SetUp]
        public void ReadPostprocessedRuntimeFixtures()
        {
            // Inspect real generated IL without adding a runtime test assembly dependency.
            var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "BitPacker.Tests");
            var assemblyPath = runtimeAssembly != null && !string.IsNullOrEmpty(runtimeAssembly.Location)
                ? runtimeAssembly.Location
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies", "BitPacker.Tests.dll"));
            Assert.That(File.Exists(assemblyPath), Is.True, "The runtime equality fixtures must be compiled first.");
            _module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { InMemory = true });
        }

        [TearDown]
        public void DisposeModule() => _module?.Dispose();

        [TestCase("GeneratedEqualityVectorState", "position", "AreEqualUnmanagedRef", "UnityEngine.Vector3")]
        [TestCase("GeneratedEqualityFloatState", "value", "AreEqualUnmanagedRef", "System.Single")]
        [TestCase("GeneratedEqualityClassState", "position", "AreEqualUnmanagedRef", "UnityEngine.Vector3")]
        [TestCase("GeneratedEqualityVectorState", "label", "AreEqualRef", "System.String")]
        [TestCase("GeneratedEqualityReferenceState", "value", "AreEqualRef", "PurrNet.Tests.GeneratedEqualityReferenceValue")]
        [TestCase("GeneratedEqualityNestedState", "inner", "AreEqualRef", "PurrNet.Tests.GeneratedEqualityVectorState")]
        [TestCase("GeneratedEqualityGenericState`1", "value", "AreEqualRef", "T")]
        public void GeneratedFieldCallsUseTheSafeComparisonPath(
            string fixtureName, string fieldName, string expectedMethod, string expectedArgument)
        {
            var fixture = _module.Types.Single(type => type.FullName == "PurrNet.Tests." + fixtureName);
            var equality = fixture.Methods.SingleOrDefault(method => method.Name == "PurrEquals");
            Assert.That(equality, Is.Not.Null, "The real postprocessor must generate the equality method.");

            var calls = equality.Body.Instructions
                .Where(instruction => instruction.OpCode == OpCodes.Call &&
                    instruction.Operand is GenericInstanceMethod target &&
                    target.DeclaringType.FullName == typeof(Packer).FullName &&
                    instruction.Previous?.OpCode == OpCodes.Ldflda &&
                    instruction.Previous.Operand is FieldReference field && field.Name == fieldName)
                .Select(instruction => (GenericInstanceMethod)instruction.Operand)
                .ToArray();

            Assert.That(calls.Length, Is.EqualTo(1), $"Expected one comparison of {fixtureName}.{fieldName}.");
            Assert.That(calls[0].Name, Is.EqualTo(expectedMethod));
            Assert.That(calls[0].GenericArguments.Single().FullName, Is.EqualTo(expectedArgument));
        }

        [TestCase("value")]
        [TestCase("wideValue")]
        [TestCase("flag")]
        [TestCase("character")]
        public void IntegralAndBooleanFieldsUseDirectScalarEquality(string fieldName)
        {
            var fixture = _module.Types.Single(type => type.FullName == "PurrNet.Tests.GeneratedEqualityScalarState");
            var equality = fixture.Methods.SingleOrDefault(method => method.Name == "PurrEquals");
            Assert.That(equality, Is.Not.Null);

            var comparisons = equality.Body.Instructions.Count(instruction =>
                instruction.OpCode == OpCodes.Ceq && instruction.Previous?.OpCode == OpCodes.Ldfld &&
                instruction.Previous.Operand is FieldReference field && field.Name == fieldName);

            Assert.That(comparisons, Is.EqualTo(1), $"{fieldName} should compare loaded values directly.");
        }
    }
}
