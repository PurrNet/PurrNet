using System;
using Mono.Cecil;
using NUnit.Framework;
using PurrNet.Packing;

namespace PurrNet.Codegen.Tests
{
    public sealed class SerializerMethodDiscoveryTests
    {
        private ModuleDefinition _module;
        private TypeReference _bitPacker;

        [SetUp]
        public void SetUp()
        {
            _module = ModuleDefinition.CreateModule("SerializerMethodDiscoveryTests", ModuleKind.Dll);
            _bitPacker = _module.ImportReference(typeof(BitPacker));
        }

        [TearDown]
        public void TearDown()
        {
            _module.Dispose();
        }

        [Test]
        public void NonVoidBitPackerMethodIsNotDiscoveredAsWriter()
        {
            var method = Method(
                "Compute",
                _module.TypeSystem.UInt16,
                _bitPacker,
                _module.TypeSystem.UInt64);

            Assert.That(RegisterSerializersProcessor.IsWriteMethod(method, out var type), Is.False);
            Assert.That(type, Is.Null);
        }

        [Test]
        public void VoidBitPackerMethodIsDiscoveredAsWriter()
        {
            var method = Method(
                "Write",
                _module.TypeSystem.Void,
                _bitPacker,
                _module.TypeSystem.UInt64);

            Assert.That(RegisterSerializersProcessor.IsWriteMethod(method, out var type), Is.True);
            Assert.That(type.FullName, Is.EqualTo(_module.TypeSystem.UInt64.FullName));
        }

        [Test]
        public void DeltaWriterMustReturnBoolean()
        {
            var invalid = Method(
                "Compute",
                _module.TypeSystem.UInt16,
                _bitPacker,
                _module.TypeSystem.UInt64,
                _module.TypeSystem.UInt64);
            var valid = Method(
                "WriteDelta",
                _module.TypeSystem.Boolean,
                _bitPacker,
                _module.TypeSystem.UInt64,
                _module.TypeSystem.UInt64);

            Assert.That(RegisterSerializersProcessor.IsDeltaWriteMethod(invalid, out _), Is.False);
            Assert.That(RegisterSerializersProcessor.IsDeltaWriteMethod(valid, out var type), Is.True);
            Assert.That(type.FullName, Is.EqualTo(_module.TypeSystem.UInt64.FullName));
        }

        [Test]
        public void ReadersMustReturnVoid()
        {
            var value = new ByReferenceType(_module.TypeSystem.UInt64);
            var invalid = Method(
                "ReadAndReturn",
                _module.TypeSystem.UInt64,
                _bitPacker,
                value);
            var valid = Method(
                "Read",
                _module.TypeSystem.Void,
                _bitPacker,
                value);

            Assert.That(RegisterSerializersProcessor.IsReadMethod(invalid, out _), Is.False);
            Assert.That(RegisterSerializersProcessor.IsReadMethod(valid, out var type), Is.True);
            Assert.That(type.FullName, Is.EqualTo(_module.TypeSystem.UInt64.FullName));
        }

        [Test]
        public void DeltaReadersMustReturnVoid()
        {
            var value = new ByReferenceType(_module.TypeSystem.UInt64);
            var invalid = Method(
                "ReadDeltaAndReturn",
                _module.TypeSystem.Boolean,
                _bitPacker,
                _module.TypeSystem.UInt64,
                value);
            var valid = Method(
                "ReadDelta",
                _module.TypeSystem.Void,
                _bitPacker,
                _module.TypeSystem.UInt64,
                value);

            Assert.That(RegisterSerializersProcessor.IsDeltaReadMethod(invalid, out _), Is.False);
            Assert.That(RegisterSerializersProcessor.IsDeltaReadMethod(valid, out var type), Is.True);
            Assert.That(type.FullName, Is.EqualTo(_module.TypeSystem.UInt64.FullName));
        }

        private MethodDefinition Method(string name, TypeReference returnType, params TypeReference[] parameters)
        {
            var method = new MethodDefinition(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType);

            for (var i = 0; i < parameters.Length; i++)
                method.Parameters.Add(new ParameterDefinition($"arg{i}", ParameterAttributes.None, parameters[i]));

            return method;
        }
    }
}
