using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen.Tests
{
    public sealed class UnityMessageRpcWarningTests
    {
        private ModuleDefinition _module;
        private TypeDefinition _type;
        private MethodInfo _warn;
        private IList _messages;

        [SetUp]
        public void SetUp()
        {
            _module = ModuleDefinition.CreateModule(nameof(UnityMessageRpcWarningTests), ModuleKind.Dll);
            _type = new TypeDefinition("Tests", "Flashlight", TypeAttributes.Public, _module.TypeSystem.Object);
            _module.Types.Add(_type);

            var processor = typeof(RegisterSerializersProcessor).Assembly.GetType("PurrNet.Codegen.PostProcessor");
            Assert.That(processor, Is.Not.Null);
            _warn = processor.GetMethod("WarnIfUnityMessageRpc", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(_warn, Is.Not.Null);
            var diagnosticType = _warn.GetParameters()[2].ParameterType.GetGenericArguments()[0];
            _messages = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(diagnosticType));
        }

        [TearDown]
        public void TearDown()
        {
            _module.Dispose();
        }

        [Test]
        public void RunLocallyServerRpcOnUpdateWarns()
        {
            var signature = new RPCSignature { type = RPCType.ServerRPC, runLocally = true };

            Assert.That(Warn(AddMethod("Update", false), signature), Is.True);
            Assert.That(_messages.Count, Is.EqualTo(1));
            var message = (string)GetMember(_messages[0], "MessageData");
            Assert.That(message, Does.Contain("'Update' is a Unity message"));
            Assert.That(message, Does.Contain("runLocally"));
        }

        [Test]
        public void ObserversRpcOnCollisionMessageWarnsWithoutRunLocallyNote()
        {
            var signature = new RPCSignature { type = RPCType.ObserversRPC };

            Assert.That(Warn(AddMethod("OnTriggerEnter", false), signature), Is.True);
            Assert.That((string)GetMember(_messages[0], "MessageData"), Does.Not.Contain("runLocally"));
        }

        [Test]
        public void OrdinaryRpcNameDoesNotWarn()
        {
            var signature = new RPCSignature { type = RPCType.ServerRPC, runLocally = true };

            Assert.That(Warn(AddMethod("SetViewed", false), signature), Is.False);
            Assert.That(_messages, Is.Empty);
        }

        [Test]
        public void StaticRpcNamedLikeUnityMessageDoesNotWarn()
        {
            var signature = new RPCSignature { type = RPCType.ObserversRPC, isStatic = true };

            Assert.That(Warn(AddMethod("Update", true), signature), Is.False);
            Assert.That(_messages, Is.Empty);
        }

        private bool Warn(MethodDefinition method, RPCSignature signature)
        {
            return (bool)_warn.Invoke(null, new object[] { method, signature, _messages });
        }

        private MethodDefinition AddMethod(string name, bool isStatic)
        {
            var attributes = MethodAttributes.Private;
            if (isStatic)
                attributes |= MethodAttributes.Static;
            var method = new MethodDefinition(name, attributes, _module.TypeSystem.Void);
            _type.Methods.Add(method);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static object GetMember(object diagnostic, string name)
        {
            var type = diagnostic.GetType();
            var property = type.GetProperty(name);
            if (property != null)
                return property.GetValue(diagnostic);

            var field = type.GetField(name);
            Assert.That(field, Is.Not.Null, $"Diagnostic type {type.FullName} has no public member {name}.");
            return field.GetValue(diagnostic);
        }
    }
}
