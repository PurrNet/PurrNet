using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen.Tests
{
    public sealed class PackerDependencyDiscoveryTests
    {
        private ModuleDefinition _module;
        private MethodInfo _expand;

        [SetUp]
        public void SetUp()
        {
            _module = ModuleDefinition.CreateModule(nameof(PackerDependencyDiscoveryTests), ModuleKind.Dll);
            var processor = typeof(RegisterSerializersProcessor).Assembly.GetType("PurrNet.Codegen.PostProcessor");
            _expand = processor.GetMethod("ExpandNested", BindingFlags.NonPublic | BindingFlags.Static);
        }

        [TearDown]
        public void TearDown() => _module.Dispose();

        [Test]
        public void ReferenceCodecDoesNotDiscoverItsImplementationCache()
        {
            var reference = Type("Reference");
            var cache = Type("ImplementationCache");
            reference.Fields.Add(new FieldDefinition("cache", FieldAttributes.Private, cache));
            Dependencies(reference);
            AssertTypes(Expand(reference), reference);
        }

        [Test]
        public void ExplicitPayloadDependenciesIncludeTheirNestedFields()
        {
            var reference = Type("Reference");
            var payload = Type("Payload");
            var nested = Type("NestedPayload");
            var cache = Type("ImplementationCache");
            reference.Fields.Add(new FieldDefinition("cache", FieldAttributes.Private, cache));
            payload.Fields.Add(new FieldDefinition("value", FieldAttributes.Public, nested));
            Dependencies(reference, payload);
            AssertTypes(Expand(reference), reference, payload, nested);
        }

        [Test]
        public void ApplicationSubclassKeepsItsOwnAutomaticFieldDiscovery()
        {
            var reference = Type("Reference");
            var cache = Type("ImplementationCache");
            reference.Fields.Add(new FieldDefinition("cache", FieldAttributes.Private, cache));
            Dependencies(reference);
            var application = Type("ApplicationIdentity");
            application.BaseType = reference;
            var payload = Type("ApplicationPayload");
            application.Fields.Add(new FieldDefinition("payload", FieldAttributes.Private, payload));
            AssertTypes(Expand(reference, application), reference, application, payload);
        }

        [Test]
        public void ClosedGenericPayloadsStillDiscoverNestedPayloadTypes()
        {
            var reference = Type("GenericReference`1");
            reference.GenericParameters.Add(new GenericParameter("T", reference));
            Dependencies(reference);
            var payload = Type("Payload");
            var nested = Type("NestedPayload");
            payload.Fields.Add(new FieldDefinition("value", FieldAttributes.Public, nested));
            var closed = new GenericInstanceType(reference);
            closed.GenericArguments.Add(payload);
            AssertTypes(Expand(closed), closed, payload, nested);
        }

        [Test]
        public void ArraysKeepTheClosedElementCodecAndItsPayloadDependencies()
        {
            var reference = Type("GenericReference`1");
            reference.GenericParameters.Add(new GenericParameter("T", reference));
            Dependencies(reference);
            var payload = Type("Payload");
            var nested = Type("NestedPayload");
            payload.Fields.Add(new FieldDefinition("value", FieldAttributes.Public, nested));
            var closed = new GenericInstanceType(reference);
            closed.GenericArguments.Add(payload);
            var array = new ArrayType(closed);
            AssertTypes(Expand(array), array, closed, payload, nested);
        }

        private TypeDefinition Type(string name)
        {
            var type = new TypeDefinition("Tests", name, TypeAttributes.Public | TypeAttributes.Class,
                _module.TypeSystem.Object);
            _module.Types.Add(type);
            return type;
        }

        private void Dependencies(TypeDefinition target, params TypeReference[] types)
        {
            var constructor = typeof(PackerDependenciesAttribute).GetConstructor(new[] { typeof(System.Type[]) });
            var attribute = new CustomAttribute(_module.ImportReference(constructor));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(_module.ImportReference(typeof(System.Type[])),
                types.Select(type => new CustomAttributeArgument(_module.ImportReference(typeof(System.Type)), type)).ToArray()));
            target.CustomAttributes.Add(attribute);
        }

        private HashSet<TypeReference> Expand(params TypeReference[] roots)
        {
            var types = new HashSet<TypeReference>(roots, TypeReferenceEqualityComparer.Default);
            _expand.Invoke(null, new object[] { _module.Assembly, types });
            return types;
        }

        private static void AssertTypes(IEnumerable<TypeReference> actual, params TypeReference[] expected)
            => Assert.That(actual.Select(type => type.FullName), Is.EquivalentTo(expected.Select(type => type.FullName)));
    }
}
