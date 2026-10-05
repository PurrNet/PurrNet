using System;
using System.IO;
using Mono.Cecil;
using NUnit.Framework;
using UnityEngine;

namespace PurrNet.Codegen.Tests
{
    public sealed class KnownUnmanagedTypeTests
    {
        private DefaultAssemblyResolver _resolver;
        private ModuleDefinition _module;

        [SetUp]
        public void SetUp()
        {
            _resolver = new DefaultAssemblyResolver();
            _resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
            _resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Vector3).Assembly.Location));
            _module = ModuleDefinition.CreateModule(nameof(KnownUnmanagedTypeTests), new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = _resolver
            });
        }

        [TearDown]
        public void TearDown()
        {
            _module.Dispose();
            _resolver.Dispose();
        }

        [TestCase(typeof(bool))]
        [TestCase(typeof(char))]
        [TestCase(typeof(sbyte))]
        [TestCase(typeof(byte))]
        [TestCase(typeof(short))]
        [TestCase(typeof(ushort))]
        [TestCase(typeof(int))]
        [TestCase(typeof(uint))]
        [TestCase(typeof(long))]
        [TestCase(typeof(ulong))]
        [TestCase(typeof(float))]
        [TestCase(typeof(double))]
        [TestCase(typeof(IntPtr))]
        [TestCase(typeof(UIntPtr))]
        public void PrimitiveValuesAreKnownUnmanaged(Type type)
        {
            Assert.That(_module.ImportReference(type).IsKnownUnmanaged(), Is.True);
        }

        [Test]
        public void ImportedUnityVectorIsKnownUnmanaged()
        {
            var vector = _module.ImportReference(typeof(Vector3));
            Assert.That(vector.IsDefinition, Is.False);
            Assert.That(vector.IsKnownUnmanaged(), Is.True);
        }

        [Test]
        public void EnumAndEmptyStructAreKnownUnmanaged()
        {
            var enumeration = Struct("EnumValue");
            enumeration.BaseType = _module.ImportReference(typeof(Enum));
            enumeration.Fields.Add(new FieldDefinition("value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                _module.TypeSystem.Int32));

            Assert.That(enumeration.IsKnownUnmanaged(), Is.True);
            Assert.That(Struct("Empty").IsKnownUnmanaged(), Is.True);
        }

        [Test]
        public void NestedValueReferencesAndRepeatedFieldsAreInspected()
        {
            var inner = Struct("Inner");
            Field(inner, "value", _module.TypeSystem.Double);
            var outer = Struct("Outer");
            var innerReference = Reference(inner);
            Field(outer, "left", innerReference);
            Field(outer, "right", innerReference).IsInitOnly = true;

            Assert.That(innerReference.IsDefinition, Is.False);
            Assert.That(outer.IsKnownUnmanaged(), Is.True);

            Field(inner, "label", _module.ImportReference(typeof(string)));
            Assert.That(outer.IsKnownUnmanaged(), Is.False,
                "Ordinary TypeReferences and repeated checks must not hide a managed nested field.");
        }

        [Test]
        public void ImportedReferenceFieldsPreventTheDirectMemoryPath()
        {
            var state = Struct("State");
            var text = _module.ImportReference(typeof(string));
            Assert.That(text.IsDefinition, Is.False);
            Field(state, "label", text);

            Assert.That(state.IsKnownUnmanaged(), Is.False);
        }

        [Test]
        public void IgnoredReferenceFieldsStillCountTowardTheMemoryLayout()
        {
            var state = Struct("IgnoredState");
            var field = Field(state, "ignored", _module.TypeSystem.String);
            field.CustomAttributes.Add(new CustomAttribute(
                _module.ImportReference(typeof(DontPackAttribute).GetConstructor(Type.EmptyTypes))));

            Assert.That(state.IsKnownUnmanaged(), Is.False);
        }

        [Test]
        public void StaticReferenceFieldsAreExcludedFromTheMemoryLayout()
        {
            var state = Struct("StaticState");
            Field(state, "value", _module.TypeSystem.Int32);
            Field(state, "cache", _module.TypeSystem.String).IsStatic = true;

            Assert.That(state.IsKnownUnmanaged(), Is.True);
        }

        [TestCase(typeof(object))]
        [TestCase(typeof(string))]
        [TestCase(typeof(Action))]
        [TestCase(typeof(int[]))]
        [TestCase(typeof(void))]
        [TestCase(typeof(TypedReference))]
        [TestCase(typeof(ArgIterator))]
        [TestCase(typeof(RuntimeArgumentHandle))]
        public void ReferenceAndUnsupportedTypesUseRuntimeDispatch(Type type)
        {
            var reference = _module.ImportReference(type);
            Assert.That(reference.IsKnownUnmanaged(), Is.False);
            var state = Struct("State");
            Field(state, "value", reference);
            Assert.That(state.IsKnownUnmanaged(), Is.False);
        }

        [Test]
        public void OpenAndClosedGenericTypesUseRuntimeDispatch()
        {
            var generic = Struct("Generic`1");
            var parameter = new GenericParameter("T", generic)
            {
                Attributes = GenericParameterAttributes.NotNullableValueTypeConstraint |
                             GenericParameterAttributes.DefaultConstructorConstraint
            };
            generic.GenericParameters.Add(parameter);
            Field(generic, "value", parameter);
            var closed = new GenericInstanceType(generic);
            closed.GenericArguments.Add(_module.TypeSystem.Int32);
            var holder = Struct("GenericHolder");
            Field(holder, "value", closed);

            Assert.That(parameter.IsKnownUnmanaged(), Is.False);
            Assert.That(generic.IsKnownUnmanaged(), Is.False);
            Assert.That(closed.IsKnownUnmanaged(), Is.False);
            Assert.That(holder.IsKnownUnmanaged(), Is.False);
        }

        [Test]
        public void ConcreteFieldsOfGenericParentsCanStillUseTheDirectPath()
        {
            var generic = Struct("Generic`1");
            generic.GenericParameters.Add(new GenericParameter("T", generic));
            var field = Field(generic, "position", _module.ImportReference(typeof(Vector3)));

            Assert.That(generic.IsKnownUnmanaged(), Is.False);
            Assert.That(field.FieldType.IsKnownUnmanaged(), Is.True);
        }

        [Test]
        public void PointersAreAcceptedOnlyInsideConcreteStructs()
        {
            var pointer = new Mono.Cecil.PointerType(_module.TypeSystem.Int32);
            var function = new FunctionPointerType { ReturnType = _module.TypeSystem.Void };
            var state = Struct("PointerState");
            Field(state, "pointer", pointer);
            Field(state, "function", function);

            Assert.That(pointer.IsKnownUnmanaged(), Is.False);
            Assert.That(function.IsKnownUnmanaged(), Is.False);
            Assert.That(state.IsKnownUnmanaged(), Is.True);
        }

        [Test]
        public void ByrefsAndByrefLikeStructsAreRejected()
        {
            var byref = new ByReferenceType(_module.TypeSystem.Int32);
            Assert.That(byref.IsKnownUnmanaged(), Is.False);
            var state = Struct("ByrefState");
            Field(state, "value", byref);
            Assert.That(state.IsKnownUnmanaged(), Is.False);

            var byrefLike = Struct("ByrefLikeState");
            Field(byrefLike, "value", _module.TypeSystem.Int32);
            var attribute = new TypeReference("System.Runtime.CompilerServices", "IsByRefLikeAttribute",
                _module, _module.TypeSystem.CoreLibrary);
            byrefLike.CustomAttributes.Add(new CustomAttribute(
                new MethodReference(".ctor", _module.TypeSystem.Void, attribute) { HasThis = true }));
            Assert.That(byrefLike.IsKnownUnmanaged(), Is.False);
        }

        [Test]
        public void UnresolvedTypesFallBackWhetherResolutionReturnsNullOrThrows()
        {
            var missing = new UnresolvedReference(_module, false);
            var throwing = new UnresolvedReference(_module, true);
            Assert.That(missing.IsKnownUnmanaged(), Is.False);
            Assert.That(throwing.IsKnownUnmanaged(), Is.False);
            var state = Struct("MissingState");
            Field(state, "value", missing);
            Assert.That(state.IsKnownUnmanaged(), Is.False);
            Assert.That(((TypeReference)null).IsKnownUnmanaged(), Is.False);
        }

        [Test]
        public void CyclicValueLayoutsAreRejected()
        {
            var first = Struct("First");
            var second = Struct("Second");
            Field(first, "value", Reference(second));
            Field(second, "value", Reference(first));

            Assert.That(first.IsKnownUnmanaged(), Is.False);
        }

        private TypeDefinition Struct(string name)
        {
            var type = new TypeDefinition("Tests", name,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                _module.ImportReference(typeof(ValueType)));
            _module.Types.Add(type);
            return type;
        }

        private static FieldDefinition Field(TypeDefinition type, string name, TypeReference fieldType)
        {
            var field = new FieldDefinition(name, FieldAttributes.Public, fieldType);
            type.Fields.Add(field);
            return field;
        }

        private TypeReference Reference(TypeDefinition type)
            => new TypeReference(type.Namespace, type.Name, _module, _module, true);

        private sealed class UnresolvedReference : TypeReference
        {
            private readonly bool _throw;

            public UnresolvedReference(ModuleDefinition module, bool shouldThrow)
                : base("Missing", "Value", module,
                    new AssemblyNameReference("MissingAssembly", new Version(1, 0)), true)
            {
                _throw = shouldThrow;
            }

            public override TypeDefinition Resolve()
            {
                if (_throw)
                    throw new AssemblyResolutionException((AssemblyNameReference)Scope);
                return null;
            }
        }
    }
}
