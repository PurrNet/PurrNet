using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using PurrNet.Utils;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen.Tests
{
    public sealed class DerivedTypeIndexGenerationTests
    {
        private AssemblyDefinition _assembly;
        private TypeDefinition _root;
        private const string IndexName = DerivedTypeDiscovery.PlayerIdentityIndexTypeName;

        [SetUp]
        public void SetUp()
        {
            _assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(
                typeof(PlayerIdentity<>).Assembly.GetName().Name, new Version(1, 0)), "IndexTest", ModuleKind.Dll);
            _root = Add("PlayerIdentity`1", null, true, "PurrNet");
            _root.GenericParameters.Add(new GenericParameter("T", _root));
        }

        [TearDown]
        public void TearDown() => _assembly.Dispose();

        private TypeDefinition Add(string name, TypeReference parent, bool abstractType = false, string ns = "Tests")
        {
            var type = new TypeDefinition(ns, name, TypeAttributes.Class | TypeAttributes.Public |
                (abstractType ? TypeAttributes.Abstract : 0), parent);
            _assembly.MainModule.Types.Add(type);
            return type;
        }

        private GenericInstanceType ClosedRoot()
        {
            var root = new GenericInstanceType(_root);
            root.GenericArguments.Add(_assembly.MainModule.TypeSystem.Int32);
            return root;
        }

        [Test]
        public void IncludesDirectIndirectAndNestedTypesInEmissionOrder()
        {
            var direct = Add("Direct", ClosedRoot());
            var abstractBase = Add("Intermediate", ClosedRoot(), true);
            var indirect = Add("Indirect", abstractBase);
            var nested = new TypeDefinition("", "PrivateNested", TypeAttributes.NestedPrivate | TypeAttributes.Class, direct);
            direct.NestedTypes.Add(nested);
            Add("Unrelated", null);
            Assert.That(GenerateDerivedTypeIndex.Generate(_assembly, typeof(PlayerIdentity<>), IndexName), Is.True);
            var get = _assembly.MainModule.GetType(IndexName).Methods.Single();
            var handles = get.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldtoken).Select(i => i.Operand).ToArray();
            CollectionAssert.AreEqual(new object[] { direct, nested, indirect }, handles);
            Assert.That(nested.IsNestedPrivate, Is.True);
        }

        [Test]
        public void EmptyFamilyGetsAnExplicitEmptyIndex()
        {
            Assert.That(GenerateDerivedTypeIndex.Generate(_assembly, typeof(PlayerIdentity<>), IndexName), Is.True);
            var get = _assembly.MainModule.GetType(IndexName).Methods.Single();
            Assert.That(get.Body.Instructions[0].Operand, Is.EqualTo(0));
        }

        [Test]
        public void UnknownBaseRetainsTheReflectionFallback()
        {
            var missingAssembly = new AssemblyNameReference("MissingPlugin", new Version(1, 0));
            var unknown = new TypeReference("Missing", "UnknownBase", _assembly.MainModule, missingAssembly);
            Add("PotentialPlayer", unknown);
            Assert.That(GenerateDerivedTypeIndex.Generate(_assembly, typeof(PlayerIdentity<>), IndexName), Is.False);
            Assert.That(_assembly.MainModule.GetType(IndexName), Is.Null);
        }

        [Test]
        public void RegenerationReplacesTheIndex()
        {
            GenerateDerivedTypeIndex.Generate(_assembly, typeof(PlayerIdentity<>), IndexName);
            Add("Later", ClosedRoot());
            Assert.That(GenerateDerivedTypeIndex.Generate(_assembly, typeof(PlayerIdentity<>), IndexName), Is.True);
            Assert.That(_assembly.MainModule.Types.Count(t => t.FullName == IndexName), Is.EqualTo(1));
            Assert.That(_assembly.MainModule.GetType(IndexName).Methods.Single().Body.Instructions[0].Operand, Is.EqualTo(1));
        }

        [Test]
        public void ReservedApplicationTypeIsNotOverwritten()
        {
            Add("PlayerIdentityTypeIndexV1", null, false, "PurrNet.Generated");
            Assert.Throws<InvalidOperationException>(() =>
                GenerateDerivedTypeIndex.Generate(_assembly, typeof(PlayerIdentity<>), IndexName));
        }
    }
}
