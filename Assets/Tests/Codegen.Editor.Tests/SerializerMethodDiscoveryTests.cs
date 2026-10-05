using System;
using System.Collections.Generic;
using System.IO;
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
            ((DefaultAssemblyResolver)_module.AssemblyResolver).AddSearchDirectory(
                Path.GetDirectoryName(typeof(Queue<>).Assembly.Location));
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

        [Test]
        public void OpenGenericNetworkModuleQueueFieldMakesPendingInputAccessibleInWrittenAssembly()
        {
            var owner = AddType("SyncInput`1");
            owner.GenericParameters.Add(new GenericParameter("T", owner));
            var pending = AddNestedType(owner, "PendingInput");
            pending.GenericParameters.Add(new GenericParameter("T", pending));
            AddField(pending, "Value", pending.GenericParameters[0]);
            var queue = CloseGeneric(_module.ImportReference(typeof(Queue<>)),
                CloseGeneric(pending, owner.GenericParameters[0]));
            AddField(owner, "PendingHostInputs", queue);

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(pending.IsNestedPublic, Is.True);
            using var stream = new MemoryStream();
            _module.Write(stream);
            stream.Position = 0;
            using var written = ModuleDefinition.ReadModule(stream);
            Assert.That(written.GetType(owner.FullName).NestedTypes[0].IsNestedPublic, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationRecursesOwnedGraphsAndTerminatesCycles()
        {
            var owner = AddGenericType("NetworkCell`1");
            var packet = AddGenericNestedType(owner, "Packet");
            var child = AddNestedType(owner, "Child");
            var unrelated = AddNestedType(owner, "Unrelated");
            AddField(owner, "State", CloseGeneric(packet, owner.GenericParameters[0]));
            AddField(packet, "Child", child);
            AddField(child, "Parent", packet);

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(packet.IsNestedPublic, Is.True);
            Assert.That(child.IsNestedPublic, Is.True);
            Assert.That(unrelated.IsNestedPrivate, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationUnwrapsArrayByReferenceAndPointerTypes()
        {
            var owner = AddGenericType("NetworkCell`1");
            var arrayItem = AddGenericNestedType(owner, "ArrayItem");
            var referenceItem = AddGenericNestedType(owner, "ReferenceItem");
            var pointerItem = AddGenericNestedType(owner, "PointerItem");
            AddField(owner, "Items", new ArrayType(CloseGeneric(arrayItem, owner.GenericParameters[0])));
            AddField(owner, "Reference", new ByReferenceType(CloseGeneric(referenceItem, owner.GenericParameters[0])));
            AddField(owner, "Pointer", new PointerType(CloseGeneric(pointerItem, owner.GenericParameters[0])));

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(arrayItem.IsNestedPublic, Is.True);
            Assert.That(referenceItem.IsNestedPublic, Is.True);
            Assert.That(pointerItem.IsNestedPublic, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationLeavesForeignDefinitionsUntouched()
        {
            using var foreign = ModuleDefinition.CreateModule("ForeignCollections", ModuleKind.Dll);
            var foreignContainer = new TypeDefinition("Foreign", "Container`1", TypeAttributes.NotPublic,
                foreign.TypeSystem.Object);
            foreign.Types.Add(foreignContainer);
            foreignContainer.GenericParameters.Add(new GenericParameter("T", foreignContainer));
            var foreignPayload = new TypeDefinition("", "Payload", TypeAttributes.NestedPrivate,
                foreign.TypeSystem.Object);
            foreignContainer.NestedTypes.Add(foreignPayload);
            AddField(foreignContainer, "State", foreignPayload);
            var owner = AddGenericType("NetworkCell`1");
            var ownedArgument = AddGenericNestedType(owner, "Argument");
            AddField(owner, "State", CloseGeneric(foreignContainer,
                CloseGeneric(ownedArgument, owner.GenericParameters[0])));

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(ownedArgument.IsNestedPublic, Is.True);
            Assert.That(foreignContainer.IsNotPublic, Is.True);
            Assert.That(foreignPayload.IsNestedPrivate, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationMakesTheEntireDeclaringChainAccessible()
        {
            var container = AddType("InternalContainer", TypeAttributes.NotPublic);
            var owner = AddGenericNestedType(container, "NetworkCell`1");
            var payload = AddGenericNestedType(owner, "Payload");
            AddField(owner, "State", CloseGeneric(payload, owner.GenericParameters[0]));

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(container.IsPublic, Is.True);
            Assert.That(owner.IsNestedPublic, Is.True);
            Assert.That(payload.IsNestedPublic, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationVisitsArgumentsOfRepeatedGenericDefinitions()
        {
            var owner = AddGenericType("NetworkCell`1");
            var first = AddNestedType(owner, "First");
            var second = AddNestedType(owner, "Second");
            var box = AddType("Box`2");
            box.GenericParameters.Add(new GenericParameter("T", box));
            box.GenericParameters.Add(new GenericParameter("TValue", box));
            AddField(box, "Value", box.GenericParameters[1]);
            AddField(owner, "FirstState", CloseGeneric(box, owner.GenericParameters[0], first));
            AddField(owner, "SecondState", CloseGeneric(box, owner.GenericParameters[0], second));

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(first.IsNestedPublic, Is.True);
            Assert.That(second.IsNestedPublic, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationDoesNotVisitConcreteServiceFields()
        {
            var owner = AddGenericType("NetworkCell`1");
            var pending = AddGenericNestedType(owner, "PendingInput");
            var queue = CloseGeneric(_module.ImportReference(typeof(Queue<>)),
                CloseGeneric(pending, owner.GenericParameters[0]));
            AddField(owner, "PendingInputs", queue);
            var service = AddType("InternalService", TypeAttributes.NotPublic);
            var servicePayload = AddNestedType(service, "Payload");
            AddField(service, "State", servicePayload);
            AddField(owner, "Service", service);

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(pending.IsNestedPublic, Is.True);
            Assert.That(service.IsNotPublic, Is.True);
            Assert.That(servicePayload.IsNestedPrivate, Is.True);
        }

        [Test]
        public void NetworkModuleFieldPreparationDoesNotVisitConcreteServicesOfReferencedNetworkModules()
        {
            var owner = AddGenericType("NetworkCell`1");
            var nestedModule = AddGenericType("ChildModule`1");
            nestedModule.BaseType = _module.ImportReference(typeof(NetworkModule));
            AddField(owner, "NestedState", CloseGeneric(nestedModule, owner.GenericParameters[0]));
            var pending = AddGenericNestedType(nestedModule, "PendingInput");
            var queue = CloseGeneric(_module.ImportReference(typeof(Queue<>)),
                CloseGeneric(pending, nestedModule.GenericParameters[0]));
            AddField(nestedModule, "PendingInputs", queue);
            var service = AddType("InternalService", TypeAttributes.NotPublic);
            var servicePayload = AddNestedType(service, "Payload");
            AddField(service, "State", servicePayload);
            AddField(nestedModule, "Service", service);

            RegisterSerializersProcessor.EnsureNetworkModuleFieldTypesAccessible(owner, _module);

            Assert.That(pending.IsNestedPublic, Is.True);
            Assert.That(service.IsNotPublic, Is.True);
            Assert.That(servicePayload.IsNestedPrivate, Is.True);
        }

        private TypeDefinition AddType(string name, TypeAttributes attributes = TypeAttributes.Public)
        {
            var type = new TypeDefinition("Tests", name, attributes, _module.TypeSystem.Object);
            _module.Types.Add(type);
            return type;
        }

        private TypeDefinition AddNestedType(TypeDefinition owner, string name)
        {
            var type = new TypeDefinition("", name, TypeAttributes.NestedPrivate, _module.TypeSystem.Object);
            owner.NestedTypes.Add(type);
            return type;
        }

        private TypeDefinition AddGenericType(string name)
        {
            var type = AddType(name);
            type.GenericParameters.Add(new GenericParameter("T", type));
            return type;
        }

        private TypeDefinition AddGenericNestedType(TypeDefinition owner, string name)
        {
            var type = AddNestedType(owner, name);
            type.GenericParameters.Add(new GenericParameter("T", type));
            return type;
        }

        private static void AddField(TypeDefinition owner, string name, TypeReference fieldType) =>
            owner.Fields.Add(new FieldDefinition(name, FieldAttributes.Private, fieldType));

        private static GenericInstanceType CloseGeneric(TypeReference type, params TypeReference[] arguments)
        {
            var result = new GenericInstanceType(type);
            foreach (var argument in arguments)
                result.GenericArguments.Add(argument);
            return result;
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
