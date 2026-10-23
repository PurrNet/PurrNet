using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using PurrNet.Packing;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen.Tests
{
    public sealed class RegistrationIndexGenerationTests
    {
        private AssemblyDefinition _assembly;

        [SetUp]
        public void SetUp()
        {
            _assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(nameof(RegistrationIndexGenerationTests), new Version(1, 0)),
                nameof(RegistrationIndexGenerationTests), ModuleKind.Dll);
            _assembly.MainModule.ImportReference(typeof(RegisterPackersAttribute));
        }

        [TearDown]
        public void TearDown() => _assembly.Dispose();

        [Test]
        public void IndexUsesExactHandlesAndRetainsNegativePriorities()
        {
            var module = _assembly.MainModule;
            var type = StaticType("Owner");
            var method = new MethodDefinition("PrivateRegister", MethodAttributes.Private | MethodAttributes.Static,
                module.TypeSystem.Void);
            method.Body.GetILProcessor().Emit(OpCodes.Ret);
            type.Methods.Add(method);
            var attribute = new CustomAttribute(module.ImportReference(typeof(RegisterPackersAttribute).GetConstructor(new[] { typeof(int) })));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Int32, -3));
            method.CustomAttributes.Add(attribute);

            Assert.That(GenerateRegistrationIndex.Generate(_assembly), Is.True);
            var get = module.GetType(PackerRegistrationEntry.IndexTypeName).Methods.Single();
            Assert.That(get.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldtoken && i.Operand == method), Is.True);
            Assert.That(get.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldtoken && i.Operand == type), Is.True);
            Assert.That(get.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == -3), Is.True);
            Assert.That(method.IsPrivate, Is.True, "Indexing must not widen application method visibility");
        }

        [Test]
        public void RegenerationReplacesTheIndexWithoutIndexingItself()
        {
            Assert.That(GenerateRegistrationIndex.Generate(_assembly), Is.True);
            Assert.That(GenerateRegistrationIndex.Generate(_assembly), Is.True);
            Assert.That(_assembly.MainModule.Types.Count(t => t.FullName == PackerRegistrationEntry.IndexTypeName), Is.EqualTo(1));
            var get = _assembly.MainModule.GetType(PackerRegistrationEntry.IndexTypeName).Methods.Single();
            Assert.That(get.Body.Instructions[0].Operand, Is.EqualTo(0));
        }

        [Test]
        public void DerivedRegistrationAttributesKeepTheReflectionFallback()
        {
            var module = _assembly.MainModule;
            var registerAttribute = new TypeDefinition("PurrNet.Packing", nameof(RegisterPackersAttribute),
                TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(registerAttribute);
            var derived = new TypeDefinition("Tests", "CustomRegistrationAttribute",
                TypeAttributes.Public | TypeAttributes.Class, registerAttribute);
            module.Types.Add(derived);
            var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            derived.Methods.Add(constructor);
            var method = new MethodDefinition("Register", MethodAttributes.Static | MethodAttributes.Private, module.TypeSystem.Void);
            StaticType("Owner").Methods.Add(method);
            method.CustomAttributes.Add(new CustomAttribute(constructor));
            Assert.That(GenerateRegistrationIndex.Generate(_assembly), Is.False);
            Assert.That(module.GetType(PackerRegistrationEntry.IndexTypeName), Is.Null);
        }

        [Test]
        public void ExistingApplicationTypeCannotBeReplaced()
        {
            _assembly.MainModule.Types.Add(new TypeDefinition("PurrNet.Generated", "PackerRegistrationIndexV1",
                TypeAttributes.Public | TypeAttributes.Class, _assembly.MainModule.TypeSystem.Object));
            Assert.Throws<InvalidOperationException>(() => GenerateRegistrationIndex.Generate(_assembly));
        }

        [Test]
        public void UnrelatedAssembliesDoNotAcquireARuntimeDependency()
        {
            _assembly.MainModule.AssemblyReferences.Clear();
            Assert.That(GenerateRegistrationIndex.Generate(_assembly), Is.False);
            Assert.That(_assembly.MainModule.AssemblyReferences, Is.Empty);
            Assert.That(_assembly.MainModule.GetType(PackerRegistrationEntry.IndexTypeName), Is.Null);
        }

        private TypeDefinition StaticType(string name)
        {
            var type = new TypeDefinition("Tests", name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
                _assembly.MainModule.TypeSystem.Object);
            _assembly.MainModule.Types.Add(type);
            return type;
        }
    }
}
