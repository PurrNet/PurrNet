#if UNITY_MONO_CECIL
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if UNITASK_PURRNET_SUPPORT
using Cysharp.Threading.Tasks;
#endif
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Editor;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEngine.Scripting;
using Channel = PurrNet.Transports.Channel;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace PurrNet.Codegen
{
    public enum SpecialParamType
    {
        SenderId,
        RPCInfo
    }

    public struct RPCMethod
    {
        public RPCSignature Signature;
        public MethodDefinition originalMethod;
        public string ogName;
    }

    [UsedImplicitly]
    public class PostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var name = compiledAssembly.Name;

            if (name.Contains("NuGetForUnity"))
                return false;

            if (name.StartsWith("Unity."))
                return false;

            if (name.StartsWith("UnityEngine."))
                return false;

            return !name.Contains("Editor");
        }

        private static int GetIDOffset(TypeDefinition type, ICollection<DiagnosticMessage> messages)
        {
            try
            {
                var baseType = type.BaseType?.Resolve();

                if (baseType == null)
                    return 0;

                return GetIDOffset(baseType, messages) +
                       baseType.Methods.Count(m => GetMethodRPCType(m, messages).HasValue);
            }
            catch (Exception e)
            {
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = "Failed to get ID offset: " + e.Message + "\n" + e.StackTrace
                });

                return 0;
            }
        }

        public static bool InheritsFrom(TypeDefinition type, string baseTypeName)
        {
            try
            {
                if (type?.FullName == baseTypeName)
                    return true;

                if (type?.BaseType == null)
                    return false;

                if (type.BaseType.FullName == baseTypeName)
                    return true;

                var btype = type.BaseType.Resolve();
                return btype != null && InheritsFrom(btype, baseTypeName);
            }
            catch
            {
                return false;
            }
        }

        private static RPCSignature? GetMethodRPCType(MethodDefinition method, ICollection<DiagnosticMessage> messages)
        {
            RPCSignature data = default;
            int rpcCount = 0;

            foreach (var attribute in method.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == typeof(ServerRpcAttribute).FullName)
                {
                    if (attribute.ConstructorArguments.Count != 7)
                    {
                        Error(messages, "ServerRPC attribute must have 7 arguments", method);
                        return null;
                    }

                    var channel = (Channel)attribute.ConstructorArguments[0].Value;
                    var runLocally = (bool)attribute.ConstructorArguments[1].Value;
                    var requireOwnership = (bool)attribute.ConstructorArguments[2].Value;
                    var compressionLevel = (CompressionLevel)attribute.ConstructorArguments[3].Value;
                    var asyncTimeoutInSec = (float)attribute.ConstructorArguments[4].Value;
                    var stripCode = (StripCodeModeOverride)attribute.ConstructorArguments[5].Value;
                    var deltaPacked = (bool)attribute.ConstructorArguments[6].Value;

                    data = new RPCSignature
                    {
                        type = RPCType.ServerRPC,
                        channel = channel,
                        runLocally = runLocally,
                        requireOwnership = requireOwnership,
                        requireServer = false,
                        bufferLast = false,
                        excludeOwner = false,
                        isStatic = method.IsStatic,
                        asyncTimeoutInSec = asyncTimeoutInSec,
                        compressionLevel = compressionLevel,
                        stripCodeMode = stripCode,
                        deltaPacked = deltaPacked
                    };
                    rpcCount++;
                }
                else if (attribute.AttributeType.FullName == typeof(ObserversRpcAttribute).FullName)
                {
                    if (attribute.ConstructorArguments.Count != 9)
                    {
                        Error(messages, "ObserversRPC attribute must have 9 arguments", method);
                        return null;
                    }

                    var channel = (Channel)attribute.ConstructorArguments[0].Value;
                    var runLocally = (bool)attribute.ConstructorArguments[1].Value;
                    var bufferLast = (bool)attribute.ConstructorArguments[2].Value;
                    var requireServer = (bool)attribute.ConstructorArguments[3].Value;
                    var excludeOwner = (bool)attribute.ConstructorArguments[4].Value;
                    var excludeSender = (bool)attribute.ConstructorArguments[5].Value;
                    var compressionLevel = (CompressionLevel)attribute.ConstructorArguments[6].Value;
                    var asyncTimeoutInSec = (float)attribute.ConstructorArguments[7].Value;
                    var deltaPacked = (bool)attribute.ConstructorArguments[8].Value;

                    data = new RPCSignature
                    {
                        type = RPCType.ObserversRPC,
                        channel = channel,
                        runLocally = runLocally,
                        bufferLast = bufferLast,
                        requireServer = requireServer,
                        requireOwnership = false,
                        excludeOwner = excludeOwner,
                        excludeSender = excludeSender,
                        isStatic = method.IsStatic,
                        asyncTimeoutInSec = asyncTimeoutInSec,
                        compressionLevel = compressionLevel,
                        deltaPacked = deltaPacked
                    };
                    rpcCount++;
                }
                else if (attribute.AttributeType.FullName == typeof(TargetRpcAttribute).FullName)
                {
                    if (attribute.ConstructorArguments.Count != 7)
                    {
                        Error(messages, "TargetRPC attribute must have 7 arguments", method);
                        return null;
                    }

                    var channel = (Channel)attribute.ConstructorArguments[0].Value;
                    var runLocally = (bool)attribute.ConstructorArguments[1].Value;
                    var bufferLast = (bool)attribute.ConstructorArguments[2].Value;
                    var requireServer = (bool)attribute.ConstructorArguments[3].Value;
                    var compressionLevel = (CompressionLevel)attribute.ConstructorArguments[4].Value;
                    var asyncTimeoutInSec = (float)attribute.ConstructorArguments[5].Value;
                    var deltaPacked = (bool)attribute.ConstructorArguments[6].Value;

                    data = new RPCSignature
                    {
                        type = RPCType.TargetRPC,
                        channel = channel,
                        runLocally = runLocally,
                        bufferLast = bufferLast,
                        requireServer = requireServer,
                        requireOwnership = false,
                        excludeOwner = false,
                        excludeSender = false,
                        isStatic = method.IsStatic,
                        asyncTimeoutInSec = asyncTimeoutInSec,
                        compressionLevel = compressionLevel,
                        deltaPacked = deltaPacked
                    };
                    rpcCount++;
                }
            }

            switch (rpcCount)
            {
                case 0:
                    return null;
                case > 1:
                    Error(messages, "Method cannot have multiple RPC attributes", method);
                    return null;
                default: return data;
            }
        }

        public static void Error(ICollection<DiagnosticMessage> messages, string message, MethodDefinition method)
        {
            try
            {
                if (method.DebugInformation.HasSequencePoints)
                {
                    var first = method.DebugInformation.SequencePoints[0];
                    string file = first.Document.Url;
                    if (!string.IsNullOrEmpty(file))
                        file = '/' + file[file.IndexOf("Assets", StringComparison.Ordinal)..].Replace('\\', '/');
                    else file = string.Empty;

                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = message,
                        Column = first.StartColumn,
                        Line = first.StartLine,
                        File = file
                    });
                }
                else
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = $"[{method.DeclaringType.FullName}] {message}"
                    });
                }
            }
            catch
            {
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = $"[{method.DeclaringType.FullName}] {message}"
                });
            }
        }

        static bool ShouldIgnore(RPCType rpcType, ParameterReference param, int index, int count,
            out SpecialParamType type)
        {
            if (index == count - 1 && param.ParameterType.FullName == typeof(RPCInfo).FullName)
            {
                type = SpecialParamType.RPCInfo;
                return true;
            }

            if (index == 0 && rpcType == RPCType.TargetRPC && GetArgType(param.ParameterType) != TargetArgType.None)
            {
                type = SpecialParamType.SenderId;
                return true;
            }

            type = default;
            return false;
        }

        private static void HandleRPCReceiver(ModuleDefinition module, TypeDefinition type,
            DisposableList<RPCMethod> originalRpcs, bool isNetworkClass, int offset)
        {
            for (var i = 0; i < originalRpcs.Count; i++)
            {
                var attributes = MethodAttributes.Private | MethodAttributes.HideBySig;

                if (originalRpcs[i].Signature.isStatic)
                    attributes |= MethodAttributes.Static;

                bool isValidReturn = ValidateReturnType(originalRpcs[i].originalMethod, out var returnMode);

                if (!isValidReturn)
                    continue;

                var voidType = module.TypeSystem.Void;
                var newMethod = new MethodDefinition($"HandleRPCGenerated_{offset + i}", attributes, voidType)
                {
                    IsPublic = true
                };

                var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
                var constructor = preserveAttribute.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters)
                    .Import(module);
                newMethod.CustomAttributes.Add(new CustomAttribute(constructor));

                var streamType = module.GetTypeDefinition<BitPacker>();

                var packetType = originalRpcs[i].Signature.isStatic ? module.GetTypeDefinition<StaticRPCPacket>() :
                    isNetworkClass ? module.GetTypeDefinition<ChildRPCPacket>() : module.GetTypeDefinition<RPCPacket>();

                var rpcInfo = module.GetTypeDefinition<RPCInfo>();

                var stream = new ParameterDefinition("stream", ParameterAttributes.None, streamType.Import(module));
                var packet = new ParameterDefinition("packet", ParameterAttributes.None, packetType.Import(module));
                var info = new ParameterDefinition("info", ParameterAttributes.None, rpcInfo.Import(module));
                var asServer = new ParameterDefinition("asServer", ParameterAttributes.None, module.TypeSystem.Boolean);

                newMethod.Parameters.Add(stream);
                newMethod.Parameters.Add(packet);
                newMethod.Parameters.Add(info);
                newMethod.Parameters.Add(asServer);
                newMethod.Body.InitLocals = true;

                var code = newMethod.Body.GetILProcessor();
                var end = Instruction.Create(OpCodes.Ret);

                ValidateReceivingRPC(module, isNetworkClass, originalRpcs[i], code, info, packet, asServer, end);

                // Add debug information pointing to the original method
                try
                {
                    if (originalRpcs[i].originalMethod.DebugInformation.HasSequencePoints)
                    {
                        var firstSequencePoint = originalRpcs[i].originalMethod.DebugInformation.SequencePoints[0];
                        var document = firstSequencePoint.Document;

                        // Get the first instruction of the new method
                        var firstInstruction = newMethod.Body.Instructions[0];

                        var newSequencePoint = new SequencePoint(firstInstruction, document)
                        {
                            StartLine = firstSequencePoint.StartLine,
                            StartColumn = firstSequencePoint.StartColumn,
                            EndLine = firstSequencePoint.EndLine,
                            EndColumn = firstSequencePoint.EndColumn
                        };

                        newMethod.DebugInformation.SequencePoints.Add(newSequencePoint);
                    }
                }
                catch
                {
                    // ignore
                }

                // call RPCModule.PostProcessRPC(RPCSignature signature, ref BitPacker packer)
                var rpcModule = module.GetTypeDefinition<RPCModule>();
                var postProcessRPC = rpcModule.GetMethod("PostProcessRpc").Import(module);

                // get packet.data field
                var dataField = packetType.GetField("data").Import(module);
                code.Append(Instruction.Create(OpCodes.Ldarga, packet));
                code.Append(Instruction.Create(OpCodes.Ldfld, dataField));
                code.Append(Instruction.Create(OpCodes.Ldarg, info));
                code.Append(Instruction.Create(OpCodes.Ldarga, stream));
                code.Append(Instruction.Create(OpCodes.Call, postProcessRPC));

                try
                {
                    if (originalRpcs[i].originalMethod.DeclaringType != null)
                    {
                        if (originalRpcs[i].originalMethod.HasGenericParameters)
                        {
                            HandleGenericRPCReceiver(module, originalRpcs[i], newMethod, stream, info, isNetworkClass);
                        }
                        else
                            HandleNonGenericRPCReceiver(module, originalRpcs[i], newMethod, stream, info, returnMode,
                                isNetworkClass);
                    }
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to handle RPC: {e.Message}\n{e.StackTrace}");
                }

                code.Append(end);
                type.Methods.Add(newMethod);
            }
        }

        private static void HandleRPCReceiverHandler(ModuleDefinition module, TypeDefinition type,
            DisposableList<RPCMethod> originalRpcs, bool isNetworkModule, int offset, bool isStaticPass)
        {
            var streamType = module.GetTypeDefinition<BitPacker>().Import(module);
            var rpcInfoType = module.GetTypeDefinition<RPCInfo>().Import(module);

            var packetType = isStaticPass
                ? module.GetTypeDefinition<StaticRPCPacket>().Import(module)
                : isNetworkModule
                    ? module.GetTypeDefinition<ChildRPCPacket>().Import(module)
                    : module.GetTypeDefinition<RPCPacket>().Import(module);

            var attributes = isStaticPass ?
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static :
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.ReuseSlot;

            var newMethod = new MethodDefinition($"OnReceivedRpc", attributes,
                module.TypeSystem.Void)
            {
                IsPublic = true,
                HasThis = !isStaticPass
            };

            var id = new ParameterDefinition("id", ParameterAttributes.None, module.TypeSystem.Int32);
            var stream = new ParameterDefinition("stream", ParameterAttributes.None, streamType);
            var packet = new ParameterDefinition("packet", ParameterAttributes.None, packetType);
            var info = new ParameterDefinition("info", ParameterAttributes.None, rpcInfoType);
            var asServer = new ParameterDefinition("asServer", ParameterAttributes.None, module.TypeSystem.Boolean);

            // int id, BitPacker stream, StaticRPCPacket packet, RPCInfo info, bool asServer
            newMethod.Parameters.Add(id);
            newMethod.Parameters.Add(stream);
            newMethod.Parameters.Add(packet);
            newMethod.Parameters.Add(info);
            newMethod.Parameters.Add(asServer);

            type.Methods.Add(newMethod);

            var body = newMethod.Body.GetILProcessor();

            for (int i = 0; i < originalRpcs.Count; i++)
            {
                var rpc = originalRpcs[i];

                if (rpc.Signature.isStatic != isStaticPass)
                    continue;

                var next = Instruction.Create(OpCodes.Nop);

                // if (id == $i)
                body.Append(Instruction.Create(OpCodes.Ldarg_S, id));
                body.Append(Instruction.Create(OpCodes.Ldc_I4, offset + i));
                body.Append(Instruction.Create(OpCodes.Bne_Un, next));

                // HandleRPCGenerated_$i(packer, packet, info, asServer);
                string methodName = $"HandleRPCGenerated_{offset + i}";

                var methodReference = new MethodReference(methodName, module.TypeSystem.Void, type);

                methodReference.Parameters.Add(new ParameterDefinition(streamType));
                methodReference.Parameters.Add(new ParameterDefinition(packetType));
                methodReference.Parameters.Add(new ParameterDefinition(rpcInfoType));
                methodReference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

                methodReference.HasThis = !isStaticPass;
                methodReference = GetOriginalMethod(methodReference);

                if (!isStaticPass)
                    body.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

                body.Append(Instruction.Create(OpCodes.Ldarg_S, stream));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, packet));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, info));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, asServer));
                body.Append(Instruction.Create(OpCodes.Call, methodReference));
                body.Append(Instruction.Create(OpCodes.Ret));

                body.Append(next);
            }

            if (type.BaseType != null)
            {
                var parent = type.BaseType;
                string methodName = $"OnReceivedRpc";
                var reference = new MethodReference(methodName, module.TypeSystem.Void, parent);

                reference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                reference.Parameters.Add(new ParameterDefinition(streamType));
                reference.Parameters.Add(new ParameterDefinition(packetType));
                reference.Parameters.Add(new ParameterDefinition(rpcInfoType));
                reference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

                reference.HasThis = !isStaticPass;

                if (!isStaticPass)
                    body.Append(Instruction.Create(OpCodes.Ldarg_0)); // this


                body.Append(Instruction.Create(OpCodes.Ldarg_S, id));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, stream));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, packet));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, info));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, asServer));

                body.Append(Instruction.Create(OpCodes.Call, reference));

                if (!isStaticPass)
                    newMethod.Overrides.Add(reference);
            }


            body.Append(Instruction.Create(OpCodes.Ret));
        }

        private static void ValidateReceivingRPC(ModuleDefinition module, bool isNetworkClass, RPCMethod originalRpc,
            ILProcessor code, ParameterDefinition info, ParameterDefinition data, ParameterDefinition asServer,
            Instruction end)
        {
            var compileTimeSignatureField = info.ParameterType.GetField("compileTimeSignature").Import(module);

            code.Append(Instruction.Create(OpCodes.Ldarga, info));
            PushRPCSignature(module, code, originalRpc, true, isNetworkClass);
            code.Append(Instruction.Create(OpCodes.Stfld, compileTimeSignatureField));

            MethodReference validateReceivingRPC;
            if (originalRpc.Signature.isStatic)
            {
                var rpcModule = module.GetTypeDefinition<RPCModule>();
                validateReceivingRPC = rpcModule.GetMethod("ValidateReceivingStaticRPC").Import(module);
            }
            else if (isNetworkClass)
            {
                var nclass = module.GetTypeDefinition<NetworkModule>();
                validateReceivingRPC = nclass.GetMethod("ValidateReceivingRPC").Import(module);

                // Call validateReceivingRPC(this, RPCInfo, RPCSignature, asServer)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this
            }
            else
            {
                var identityType = module.GetTypeDefinition<NetworkIdentity>();
                validateReceivingRPC = identityType.GetMethod("ValidateReceivingRPC").Import(module);

                // Call validateReceivingRPC(this, RPCInfo, RPCSignature, asServer)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this
            }

            // RPCInfo info, RPCSignature signature, INetworkedData data, bool asServer
            code.Append(Instruction.Create(OpCodes.Ldarg, info)); // info

            code.Append(Instruction.Create(OpCodes.Ldarga, info));
            code.Append(Instruction.Create(OpCodes.Ldfld, compileTimeSignatureField));

            code.Append(Instruction.Create(OpCodes.Ldarg, data)); // data
            code.Append(Instruction.Create(OpCodes.Box, data.ParameterType));
            code.Append(Instruction.Create(OpCodes.Ldarg, asServer)); // asServer
            code.Append(Instruction.Create(OpCodes.Call, validateReceivingRPC));

            // if returned false, return early
            code.Append(Instruction.Create(OpCodes.Brfalse, end));
        }

        private static void HandleNonGenericRPCReceiver(
            ModuleDefinition module,
            RPCMethod rpcMethod,
            MethodDefinition newMethod,
            ParameterDefinition stream,
            ParameterDefinition info,
            ReturnMode returnMode,
            bool isNetworkClass)
        {
            var code = newMethod.Body.GetILProcessor();
            var originalMethod = rpcMethod.originalMethod;
            int paramCount = originalMethod.Parameters.Count;

            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var rpcReqRespType = module.GetTypeDefinition<RpcRequestResponseModule>();
            var rpcModule = originalMethod.DeclaringType.Module.GetTypeDefinition<RPCModule>();
            var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
            var responder = rpcReqRespType.GetMethod("CompleteRequestWithResponse", true).Import(module);
            var responderUniTask = rpcReqRespType.GetMethod("CompleteRequestWithUniTask", true).Import(module);
            var responderWithoutResponse = rpcReqRespType.GetMethod("CompleteRequestWithEmptyResponse").Import(module);
            var responderCoroutine = rpcReqRespType.GetMethod("CompleteRequestWithCoroutine").Import(module);
            var responderUniTaskWithoutResponse =
                rpcReqRespType.GetMethod("CompleteRequestWithUniTaskEmptyResponse").Import(module);
            var RPCPacketPackerType = module.GetTypeDefinition<RPCPacketPacker>();

            var localPlayerProp = identityType.GetProperty("localPlayerForced");
            var localPlayerGetter = localPlayerProp.GetMethod.Import(module);

            var localPlayerPropModule = networkModule.GetProperty("localPlayerForced");
            var localPlayerGetterModule = localPlayerPropModule.GetMethod.Import(module);

            var networkManagerProp = identityType.GetProperty("networkManager");
            var getNetworkManager = networkManagerProp.GetMethod.Import(module);

            var networkManagerModuleProp = networkModule.GetProperty("networkManager");
            var getNetworkManagerModule = networkManagerModuleProp.GetMethod.Import(module);

            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            VariableDefinition reqId = null;
            VariableDefinition rpcPacker = null;


            bool useDeltaPacking = rpcMethod.Signature.deltaPacked && rpcMethod.Signature.type != RPCType.ServerRPC;

            // create delta packer
            if (useDeltaPacking)
            {
                rpcPacker = new VariableDefinition(RPCPacketPackerType.Import(module));
                newMethod.Body.Variables.Add(rpcPacker);

                var createPackerForRPC = RPCPacketPackerType.GetMethod(GetCreateWithInfoName(rpcMethod, isNetworkClass))
                    .Import(module);
                //NetworkManager manager, RPCPacket context, RPCInfo info
                PushNetworkManager(module, code, isNetworkClass, newMethod.IsStatic);
                // PushNetworkManager(newMethod, isNetworkClass, code, mainManagerGetter, getNetworkManagerModule, getNetworkManager);

                if (rpcMethod.Signature.isStatic)
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_1));
                    code.Append(Instruction.Create(OpCodes.Ldarg_2));
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_2));
                    code.Append(Instruction.Create(OpCodes.Ldarg_3));
                }

                code.Append(Instruction.Create(OpCodes.Call, createPackerForRPC));
                code.Append(Instruction.Create(OpCodes.Stloc, rpcPacker));
            }

            if (returnMode != ReturnMode.Void)
            {
                reqId = new VariableDefinition(module.TypeSystem.UInt32);
                newMethod.Body.Variables.Add(reqId);

                MethodReference serializer;

                if (useDeltaPacking)
                {
                    serializer = CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, false);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serializer = CreateSerializer(module, module.TypeSystem.UInt32, false);

                code.Append(Instruction.Create(OpCodes.Ldarg, stream));
                code.Append(Instruction.Create(OpCodes.Ldloca, reqId));
                code.Append(Instruction.Create(OpCodes.Call, serializer));
            }

            for (var p = 0; p < originalMethod.Parameters.Count; p++)
            {
                var param = originalMethod.Parameters[p];
                var variable = new VariableDefinition(param.ParameterType);
                newMethod.Body.Variables.Add(variable);

                if (ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out var specialType))
                {
                    switch (specialType)
                    {
                        case SpecialParamType.RPCInfo:
                            code.Append(Instruction.Create(OpCodes.Ldarg, info));
                            code.Append(Instruction.Create(OpCodes.Stloc, variable));
                            break;
                        case SpecialParamType.SenderId:
                            if (GetArgType(param.ParameterType) == TargetArgType.Player)
                            {
                                if (!rpcMethod.Signature.isStatic)
                                {
                                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                                    code.Append(isNetworkClass
                                        ? Instruction.Create(OpCodes.Call, localPlayerGetterModule)
                                        : Instruction.Create(OpCodes.Call, localPlayerGetter));
                                }
                                else
                                {
                                    code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
                                }

                                code.Append(Instruction.Create(OpCodes.Stloc, variable));
                            }

                            break;
                    }

                    continue;
                }

                MethodReference serialize;

                if (useDeltaPacking)
                {
                    serialize = CreateDeltaSerializer(module, param.ParameterType, rpcPacker, false);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serialize = CreateSerializer(module, param.ParameterType, false);

                code.Append(Instruction.Create(OpCodes.Ldarg, stream));
                code.Append(Instruction.Create(OpCodes.Ldloca, variable));
                code.Append(Instruction.Create(OpCodes.Call, serialize));
            }

            if (!originalMethod.IsStatic)
                code.Append(Instruction.Create(OpCodes.Ldarg_0));

            var vars = newMethod.Body.Variables;
            int startIdx;
            if (useDeltaPacking)
                startIdx = (reqId == null ? 1 : 2);
            else startIdx = (reqId == null ? 0 : 1);

            for (var j = startIdx; j < vars.Count; j++)
            {
                code.Append(Instruction.Create(OpCodes.Ldloc, vars[j]));
            }

            code.Append(Instruction.Create(OpCodes.Call, GetOriginalMethod(originalMethod)));

            if (reqId != null)
            {
                if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                    originalMethod.ReturnType is GenericInstanceType genericInstance)
                {
                    if (genericInstance.GenericArguments.Count != 1)
                    {
                        code.Append(Instruction.Create(OpCodes.Pop));
                        return;
                    }

                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, reqId));
                    // load networkManager
                    PushNetworkManager(module, code, isNetworkClass, newMethod.IsStatic);

                    var genericResponse =
                        new GenericInstanceMethod(returnMode is ReturnMode.Task ? responder : responderUniTask);
                    genericResponse.GenericArguments.Add(genericInstance.GenericArguments[0]);
                    code.Append(Instruction.Create(OpCodes.Call, genericResponse));
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, reqId));

                    // load networkManager
                    if (newMethod.IsStatic)
                    {
                        code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldarg_0));
                        code.Append(isNetworkClass
                            ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                            : Instruction.Create(OpCodes.Call, getNetworkManager));
                    }

                    code.Append(returnMode switch
                    {
                        ReturnMode.IEnumerator => Instruction.Create(OpCodes.Call, responderCoroutine),
                        ReturnMode.UniTask => Instruction.Create(OpCodes.Call, responderUniTaskWithoutResponse),
                        ReturnMode.Task => Instruction.Create(OpCodes.Call, responderWithoutResponse),
                        _ => throw new ArgumentOutOfRangeException()
                    });
                }
            }
        }

        private static string GetCreateWithInfoName(RPCMethod rpcMethod, bool isNetworkClass)
        {
            string methodName;

            if (isNetworkClass)
            {
                methodName = "CreateChildWithInfo";
            }
            else if (rpcMethod.Signature.isStatic)
            {
                methodName = "CreateStaticWithInfo";
            }
            else methodName = "CreateWithInfo";

            return methodName;
        }

        private static string GetCreateName(RPCMethod rpcMethod, bool isNetworkClass)
        {
            string methodName;

            if (isNetworkClass)
            {
                methodName = "CreateChild";
            }
            else if (rpcMethod.Signature.isStatic)
            {
                methodName = "CreateStatic";
            }
            else methodName = "Create";

            return methodName;
        }

        private static void PushNetworkManager(ModuleDefinition module, ILProcessor code, bool isNetworkClass, bool isStatic)
        {
            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();

            var networkManagerProp = identityType.GetProperty("networkManager");
            var getNetworkManager = networkManagerProp.GetMethod.Import(module);

            var networkManagerModuleProp = networkModule.GetProperty("networkManager");
            var getNetworkManagerModule = networkManagerModuleProp.GetMethod.Import(module);

            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            if (isStatic)
            {
                code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(isNetworkClass
                    ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                    : Instruction.Create(OpCodes.Call, getNetworkManager));
            }
        }

        private static MethodReference GetOriginalMethod(MethodReference originalMethod)
        {
            if (!originalMethod.DeclaringType.HasGenericParameters)
                return originalMethod;

            var declaringType = new GenericInstanceType(originalMethod.DeclaringType);

            foreach (var t in originalMethod.DeclaringType.GenericParameters)
                declaringType.GenericArguments.Add(t);

            var methodToCall = new MethodReference(
                originalMethod.Name,
                originalMethod.ReturnType,
                declaringType)
            {
                HasThis = originalMethod.HasThis,
                ExplicitThis = originalMethod.ExplicitThis,
                CallingConvention = originalMethod.CallingConvention,
            };

            foreach (var parameter in originalMethod.Parameters)
            {
                methodToCall.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes,
                    parameter.ParameterType));
            }

            foreach (var parameter in originalMethod.GenericParameters)
            {
                methodToCall.GenericParameters.Add(new GenericParameter(parameter.Name, parameter.Owner));
            }

            return methodToCall;
        }

        private static void HandleGenericRPCReceiver(ModuleDefinition module, RPCMethod rpcMethod,
            MethodDefinition newMethod,
            ParameterDefinition stream, ParameterDefinition info, bool isNetworkClass)
        {
            var genericRpcHeaderType = module.GetTypeDefinition<GenericRPCHeader>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var rpcReqRespType = module.GetTypeDefinition<RpcRequestResponseModule>();
            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();

            var responderWithoutResponse = rpcReqRespType.GetMethod("CompleteRequestWithEmptyResponse").Import(module);
            var responderCoroutine = rpcReqRespType.GetMethod("CompleteRequestWithCoroutine").Import(module);
            var responderUniTaskWithoutResponse =
                rpcReqRespType.GetMethod("CompleteRequestWithUniTaskEmptyResponse").Import(module);

            var responderTask = rpcReqRespType.GetMethod("CompleteRequestWithResponseObject", true).Import(module);
            var responderUniTask = rpcReqRespType.GetMethod("CompleteRequestWithUniTaskObject", true).Import(module);

            var responderTaskObject = rpcReqRespType.GetMethod("CompleteRequestWithResponseObject", false).Import(module);
            var responderUniTaskObject = rpcReqRespType.GetMethod("CompleteRequestWithUniTaskObject", false).Import(module);

            var localPlayerProp = identityType.GetProperty("localPlayerForced");
            var localPlayerGetter = localPlayerProp.GetMethod.Import(module);

            var code = newMethod.Body.GetILProcessor();
            var createGenericHeader = genericRpcHeaderType.GetMethod("CreateGenericHeader").Import(module);
            var saveReadHashMethod = genericRpcHeaderType.GetMethod("SaveReadHash").Import(module);

            var setInfo = genericRpcHeaderType.GetMethod("SetInfo").Import(module);
            var readGeneric = genericRpcHeaderType.GetMethod("Read").Import(module);
            var readT = genericRpcHeaderType.GetMethod("Read", true).Import(module);
            var setPlayerId = genericRpcHeaderType.GetMethod("SetPlayerId").Import(module);
            var getGenericTypeAt = genericRpcHeaderType.GetMethod("GetTypeAt").Import(module);
            var SaveReadValueNonGeneric = genericRpcHeaderType.GetMethod("SaveReadValue").Import(module);
            var SaveReadValueGeneric = genericRpcHeaderType.GetMethod("SaveReadValue", true).Import(module);

            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            var getNetworkManagerModule = networkModule.GetProperty("networkManager").GetMethod.Import(module);
            var getNetworkManager = identityType.GetProperty("networkManager").GetMethod.Import(module);
            var RPCPacketPackerType = module.GetTypeDefinition<RPCPacketPacker>();
            var ReadObjectMethod = RPCPacketPackerType.GetMethod("ReadObject").Import(module);
            var ReadObjectGenMethod = RPCPacketPackerType.GetMethod("ReadObject", true).Import(module);

            var rpcModule = module.GetTypeDefinition<RPCModule>();
            var nclassType = module.GetTypeDefinition<NetworkModule>();

            var callGenericMethod = rpcMethod.Signature.isStatic
                ?
                rpcModule.GetMethod("CallStaticGeneric").Import(module)
                :
                isNetworkClass
                    ? nclassType.GetMethod("CallGeneric").Import(module)
                    :
                    identityType.GetMethod("CallGeneric").Import(module);

            var originalMethod = rpcMethod.originalMethod;
            int paramCount = originalMethod.Parameters.Count;
            int genericParamCount = originalMethod.GenericParameters.Count;

            bool useDeltaPacking = rpcMethod.Signature.deltaPacked && rpcMethod.Signature.type != RPCType.ServerRPC;

            VariableDefinition requestId = null;
            VariableDefinition rpcPacker = null;
            var genericParamCountValue = new VariableDefinition(module.TypeSystem.UInt32);

            var headerValue = new VariableDefinition(genericRpcHeaderType.Import(module));
            newMethod.Body.Variables.Add(headerValue);
            newMethod.Body.Variables.Add(genericParamCountValue);

            // create delta packer
            if (useDeltaPacking)
            {
                rpcPacker = new VariableDefinition(RPCPacketPackerType.Import(module));
                newMethod.Body.Variables.Add(rpcPacker);

                var createPackerForRPC = RPCPacketPackerType.GetMethod(GetCreateWithInfoName(rpcMethod, isNetworkClass))
                    .Import(module);
                //NetworkManager manager, RPCPacket context, RPCInfo info
                PushNetworkManager(newMethod, isNetworkClass, code, mainManagerGetter, getNetworkManagerModule, getNetworkManager);

                if (rpcMethod.Signature.isStatic)
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_1));
                    code.Append(Instruction.Create(OpCodes.Ldarg_2));
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_2));
                    code.Append(Instruction.Create(OpCodes.Ldarg_3));
                }

                code.Append(Instruction.Create(OpCodes.Call, createPackerForRPC));
                code.Append(Instruction.Create(OpCodes.Stloc, rpcPacker));
            }

            bool isValidReturn = ValidateReturnType(originalMethod, out var returnMode);
            MethodReference serializeUint;

            serializeUint = useDeltaPacking ?
                CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, false) :
                CreateSerializer(module, module.TypeSystem.UInt32, false);

            if (!isValidReturn)
            {
                return;
            }

            if (returnMode != ReturnMode.Void)
            {
                requestId = new VariableDefinition(module.TypeSystem.UInt32);
                newMethod.Body.Variables.Add(requestId);

                if (useDeltaPacking)
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                code.Append(Instruction.Create(OpCodes.Ldarg, stream));
                code.Append(Instruction.Create(OpCodes.Ldloca, requestId));
                code.Append(Instruction.Create(OpCodes.Call, serializeUint));
            }

            // read header value
            code.Append(Instruction.Create(OpCodes.Ldarg, stream));
            code.Append(Instruction.Create(OpCodes.Ldarg, info));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, genericParamCount));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, paramCount));
            code.Append(Instruction.Create(OpCodes.Call, createGenericHeader));
            code.Append(Instruction.Create(OpCodes.Stloc, headerValue));

            for (int i = 0; i < genericParamCount; i++)
            {
                // read uint hash
                if (useDeltaPacking)
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                code.Append(Instruction.Create(OpCodes.Ldarg, stream));
                code.Append(Instruction.Create(OpCodes.Ldloca, genericParamCountValue));
                code.Append(Instruction.Create(OpCodes.Call, serializeUint));

                // call GenericRPCHeader.SaveReadHash(uint hash, int index)
                code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                code.Append(Instruction.Create(OpCodes.Ldloc, genericParamCountValue));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, i));
                code.Append(Instruction.Create(OpCodes.Call, saveReadHashMethod));
            }

            // read generic parameters
            for (var p = 0; p < paramCount; p++)
            {
                var param = originalMethod.Parameters[p];

                if (ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out var specialType))
                {
                    switch (specialType)
                    {
                        case SpecialParamType.RPCInfo:
                            code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                            code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                            code.Append(Instruction.Create(OpCodes.Call, setInfo));
                            break;
                        case SpecialParamType.SenderId:
                            if (GetArgType(param.ParameterType) == TargetArgType.Player)
                            {
                                code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));

                                if (!rpcMethod.Signature.isStatic)
                                {
                                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                                    code.Append(Instruction.Create(OpCodes.Call, localPlayerGetter));
                                }
                                else
                                {
                                    var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
                                    code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
                                }

                                code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                                code.Append(Instruction.Create(OpCodes.Call, setPlayerId));
                            }
                            break;
                    }

                    continue;
                }

                var genericIdx = param.ParameterType.IsGenericParameter
                    ? originalMethod.GenericParameters.IndexOf((GenericParameter)param.ParameterType)
                    : -1;

                if (genericIdx != -1)
                {
                    code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                    if (useDeltaPacking)
                    {
                        // OBJ in stack
                        {
                            code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                            code.Append(Instruction.Create(OpCodes.Ldarg, stream));

                            // GetTypeAt(genericIdx)
                            code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                            code.Append(Instruction.Create(OpCodes.Ldc_I4, genericIdx));
                            code.Append(Instruction.Create(OpCodes.Call, getGenericTypeAt));

                            code.Append(Instruction.Create(OpCodes.Call, ReadObjectMethod));
                        }

                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, SaveReadValueNonGeneric));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, genericIdx));
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, readGeneric));
                    }
                }
                else
                {
                    if (useDeltaPacking)
                    {
                        code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));

                        // OBJ in stack
                        {
                            var readAny = new GenericInstanceMethod(ReadObjectGenMethod);
                            readAny.GenericArguments.Add(param.ParameterType);

                            code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                            code.Append(Instruction.Create(OpCodes.Ldarg, stream));
                            code.Append(Instruction.Create(OpCodes.Call, readAny));
                        }

                        var saveAny = new GenericInstanceMethod(SaveReadValueGeneric);
                        saveAny.GenericArguments.Add(param.ParameterType);

                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, saveAny));
                    }
                    else
                    {
                        var readAny = new GenericInstanceMethod(readT);
                        readAny.GenericArguments.Add(param.ParameterType);

                        code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, readAny));
                    }
                }
            }

            // call 'CallGeneric'
            code.Append(!rpcMethod.Signature.isStatic
                ? Instruction.Create(OpCodes.Ldarg_0)
                : Instruction.Create(OpCodes.Ldtoken, originalMethod.DeclaringType));

            code.Append(Instruction.Create(OpCodes.Ldstr, originalMethod.Name)); // methodName
            code.Append(Instruction.Create(OpCodes.Ldloc, headerValue)); // rpcHeader
            code.Append(Instruction.Create(OpCodes.Call, callGenericMethod)); // CallGeneric

            if (requestId != null)
            {
                if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                    originalMethod.ReturnType is GenericInstanceType genericInstance)
                {
                    if (genericInstance.GenericArguments.Count != 1)
                    {
                        code.Append(Instruction.Create(OpCodes.Pop));
                        return;
                    }

                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, requestId));
                    PushNetworkManager(newMethod, isNetworkClass, code, mainManagerGetter, getNetworkManagerModule, getNetworkManager);

                    var taskType = genericInstance.GenericArguments[0];
                    bool isTaskTypeConcrete = IsConcreteType(taskType, out var concreteTaskType);

                    if (returnMode == ReturnMode.Task)
                    {
                        if (isTaskTypeConcrete)
                        {
                            var genericResponse = new GenericInstanceMethod(responderTask);
                            genericResponse.GenericArguments.Add(concreteTaskType);
                            code.Append(Instruction.Create(OpCodes.Call, genericResponse.Import(module)));
                        }
                        else code.Append(Instruction.Create(OpCodes.Call, responderTaskObject));
                    }
                    else
                    {
                        //code.Append(Instruction.Create(OpCodes.Call, responderUniTaskObject));

                        if (isTaskTypeConcrete)
                        {
                            var genericResponse = new GenericInstanceMethod(responderUniTask);
                            genericResponse.GenericArguments.Add(concreteTaskType);
                            code.Append(Instruction.Create(OpCodes.Call, genericResponse.Import(module)));
                        }
                        else code.Append(Instruction.Create(OpCodes.Call, responderUniTaskObject));
                    }
                }
                else if (originalMethod.ReturnType != module.TypeSystem.Void)
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, requestId));

                    // load networkManager
                    if (newMethod.IsStatic)
                    {
                        code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldarg_0));
                        code.Append(isNetworkClass
                            ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                            : Instruction.Create(OpCodes.Call, getNetworkManager));
                    }

                    code.Append(returnMode switch
                    {
                        ReturnMode.IEnumerator => Instruction.Create(OpCodes.Call, responderCoroutine),
                        ReturnMode.UniTask => Instruction.Create(OpCodes.Call, responderUniTaskWithoutResponse),
                        ReturnMode.Task => Instruction.Create(OpCodes.Call, responderWithoutResponse),
                        _ => throw new ArgumentOutOfRangeException()
                    });
                }
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Pop));
            }
        }

        private static void PushNetworkManager(MethodDefinition newMethod, bool isNetworkClass, ILProcessor code,
            MethodReference mainManagerGetter, MethodReference getNetworkManagerModule, MethodReference getNetworkManager)
        {
            // load networkManager
            if (newMethod.IsStatic)
            {
                code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(isNetworkClass
                    ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                    : Instruction.Create(OpCodes.Call, getNetworkManager));
            }
        }

        public enum ReturnMode
        {
            Void,
            Task,
            UniTask,
            IEnumerator
        }

        private static bool IsGeneric(TypeReference typeRef, Type type)
        {
            // Ensure method has a generic return type
            if (typeRef is GenericInstanceType genericReturnType)
            {
                // Resolve the element type to compare against Task<>
                var resolvedType = genericReturnType.ElementType.Resolve();

                // Check if the resolved type matches Task<>
                return resolvedType != null && resolvedType.FullName == type.FullName;
            }

            return false;
        }

        private static bool IsGeneric(MethodReference method, Type type)
        {
            // Ensure method has a generic return type
            if (method.ReturnType is GenericInstanceType genericReturnType)
            {
                // Resolve the element type to compare against Task<>
                var resolvedType = genericReturnType.ElementType.Resolve();

                // Check if the resolved type matches Task<>
                return resolvedType != null && resolvedType.FullName == type.FullName;
            }

            return false;
        }

        static bool ValidateReturnType(MethodDefinition method, out ReturnMode mode)
        {
            mode = ReturnMode.Void;

            if (method.ReturnType.FullName == typeof(void).FullName)
                return true;

            bool isIEnumerator = method.ReturnType.FullName == typeof(IEnumerator).FullName;

            if (isIEnumerator)
            {
                mode = ReturnMode.IEnumerator;
                return true;
            }

            bool isTask = method.ReturnType.FullName == typeof(Task).FullName;

            if (isTask)
            {
                mode = ReturnMode.Task;
                return true;
            }

            if (IsGeneric(method, typeof(Task<>)))
            {
                mode = ReturnMode.Task;
                return true;
            }

#if UNITASK_PURRNET_SUPPORT
            bool isUniTask = method.ReturnType.FullName == typeof(UniTask).FullName;

            if (isUniTask)
            {
                mode = ReturnMode.UniTask;
                return true;
            }

            if (IsGeneric(method, typeof(UniTask<>)))
            {
                mode = ReturnMode.UniTask;
                return true;
            }
#endif
            return false;
        }

        static bool IsTaskOrInheritsFromTask(TypeReference type)
        {
            if (type.FullName == typeof(Task).FullName)
                return true;

            return type.Resolve().BaseType?.FullName == typeof(Task).FullName;
        }

        public static bool IsConcreteType(TypeReference type, out TypeReference concreteType)
        {
            try
            {
                concreteType = type;

                if (type.ContainsGenericParameter)
                    return false;

                if (IsTaskOrInheritsFromTask(type) && type is GenericInstanceType genericInstanceType)
                    concreteType = genericInstanceType.GenericArguments[0];

                return true;
            }
            catch
            {
                concreteType = type;
                return false;
            }
        }

        static StripCodeMode GetMode(PurrNetSettings settings, StripCodeModeOverride overrideMode)
        {
            switch (overrideMode)
            {
                case StripCodeModeOverride.DoNotStrip:
                    return StripCodeMode.DoNotStrip;
                case StripCodeModeOverride.StripAll:
                    return StripCodeMode.StripAll;
                case StripCodeModeOverride.ReplaceWithEmptyMethod:
                    return StripCodeMode.ReplaceWithEmptyMethod;
                case StripCodeModeOverride.ReplaceWithLogWarning:
                    return StripCodeMode.ReplaceWithLogWarning;
                case StripCodeModeOverride.ReplaceWithLogError:
                    return StripCodeMode.ReplaceWithLogError;
                case StripCodeModeOverride.ThrowNotSupportedException:
                    return StripCodeMode.ThrowNotSupportedException;
                case StripCodeModeOverride.Settings:
                default:
                    return settings.stripCodeMode;
            }
        }

        static GuardFailureAction GetMode(PurrNetSettings settings, GuardFailureActionOverride overrideAction)
        {
            switch (overrideAction)
            {
                case GuardFailureActionOverride.ReturnDefault:
                    return GuardFailureAction.ReturnDefault;
                case GuardFailureActionOverride.ThrowException:
                    return GuardFailureAction.ThrowException;
                case GuardFailureActionOverride.LogWarning:
                    return GuardFailureAction.LogWarning;
                case GuardFailureActionOverride.LogError:
                    return GuardFailureAction.LogError;
                case GuardFailureActionOverride.Ignore:
                    return GuardFailureAction.Ignore;
                case GuardFailureActionOverride.Settings:
                default:
                    return settings.guardFailureAction;
            }
        }

        private static void StripBody(MethodDefinition method, string methodName, PurrNetSettings settings, StripCodeModeOverride overrideMode)
        {
            var mode = GetMode(settings, overrideMode);

            if (mode == StripCodeMode.DoNotStrip)
                return;

            var il = method.Body.GetILProcessor();
            il.Clear();

            AppendStripAction(method, methodName, mode, il, "stripped");
        }

        private static void AppendStripAction(MethodDefinition method, string methodName, StripCodeMode mode, ILProcessor il, string action)
        {
            switch (mode)
            {
                case StripCodeMode.StripAll:
                    method.DeclaringType.Methods.Remove(method);
                    break;

                case StripCodeMode.ReplaceWithLogWarning:
                    var logWarningMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerWarning", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{methodName}' is {action} and cannot be called."));
                    il.Append(il.Create(OpCodes.Call, logWarningMethod));
                    break;

                case StripCodeMode.ReplaceWithLogError:
                    var logErrorMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerError", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{methodName}' is {action} and cannot be called."));
                    il.Append(il.Create(OpCodes.Call, logErrorMethod));
                    break;

                case StripCodeMode.ReplaceWithEmptyMethod:
                    break;

                case StripCodeMode.ThrowNotSupportedException:
                    var throwExcep = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("ThrowUnsupportedException", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{methodName}' is {action} and cannot be called."));
                    il.Append(il.Create(OpCodes.Call, throwExcep));
                    break;
                case StripCodeMode.DoNotStrip:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ProperlyEndMethod(method, il);
        }

        private static void ProperlyEndMethod(MethodDefinition method, ILProcessor il)
        {
            var returnType = method.ReturnType;
            if (returnType.MetadataType != MetadataType.Void)
                EmitDefaultValue(method, il, returnType);
            il.Append(il.Create(OpCodes.Ret));
        }

        private static void EmitDefaultValue(MethodDefinition method, ILProcessor il, TypeReference type)
        {
            switch (type.MetadataType)
            {
                case MetadataType.Boolean:
                case MetadataType.Byte:
                case MetadataType.SByte:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                case MetadataType.Int64:
                case MetadataType.UInt64:
                case MetadataType.IntPtr:
                case MetadataType.UIntPtr:
                case MetadataType.Char:
                    il.Append(il.Create(OpCodes.Ldc_I4_0));
                    break;
                case MetadataType.Single:
                    il.Append(il.Create(OpCodes.Ldc_R4, 0f));
                    break;
                case MetadataType.Double:
                    il.Append(il.Create(OpCodes.Ldc_R8, 0d));
                    break;
                case MetadataType.String:
                case MetadataType.Class:
                case MetadataType.Object:
                case MetadataType.Array:
                case MetadataType.ByReference:
                    il.Append(il.Create(OpCodes.Ldnull));
                    break;
                case MetadataType.GenericInstance:
                case MetadataType.ValueType:
                {
                    // For structs, emit a local, initobj, ldloc
                    var varDef = new VariableDefinition(type);
                    method.Body.Variables.Add(varDef);
                    il.Append(il.Create(OpCodes.Ldloca_S, varDef));
                    il.Append(il.Create(OpCodes.Initobj, type));
                    il.Append(il.Create(OpCodes.Ldloc, varDef));
                    break;
                }
                case MetadataType.Void:
                case MetadataType.Pointer:
                case MetadataType.Var:
                case MetadataType.TypedByReference:
                case MetadataType.FunctionPointer:
                case MetadataType.MVar:
                case MetadataType.RequiredModifier:
                case MetadataType.OptionalModifier:
                case MetadataType.Sentinel:
                case MetadataType.Pinned:
                default:
                    il.Append(il.Create(OpCodes.Ldnull));
                    break;
            }
        }

        private MethodDefinition HandleRPC(ModuleDefinition module, int id, RPCMethod methodRpc, bool isNetworkClass, bool isServerBuild, PurrNetSettings settings,
            HashSet<TypeReference> usedTypes, [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            var method = methodRpc.originalMethod;

            if (method.DeclaringType == null)
                return null;

            bool isValidReturn = ValidateReturnType(method, out var returnMode);

            if (!isValidReturn)
            {
                Error(messages,
                    method.ReturnType.Name.Contains("UniTask")
                        ? $"RPC '{method.Name}' uses <b>UniTask</b>, you need to enable support under `Tools/PurrNet/Packages/Install UniTask` or define the `UNITASK_PURRNET_SUPPORT` symbol."
                        : $"RPC '{method.Name}' RPC must return <b>void</b>, <b>Task</b> or <b>UniTask</b>",
                    method);
                return null;
            }

            if (returnMode == ReturnMode.IEnumerator && methodRpc.Signature.type == RPCType.ObserversRPC)
            {
                Error(messages, $"ObserversRPC '{method.Name}' method cannot return IEnumerator", method);
                return null;
            }

            if (returnMode == ReturnMode.Task && methodRpc.Signature.type == RPCType.ObserversRPC)
            {
                Error(messages, $"ObserversRPC '{method.Name}' method cannot return Task", method);
                return null;
            }

            if (IsConcreteType(method.ReturnType, out var concreteType))
                usedTypes.Add(concreteType);

            string ogName = method.Name;
            method.Name = ogName + "_Original_" + id;

            var attributes = MethodAttributes.Public | MethodAttributes.HideBySig;

            if (methodRpc.Signature.isStatic)
                attributes |= MethodAttributes.Static;

            if (method.IsVirtual)
                attributes |= MethodAttributes.Virtual;

            var newMethod = new MethodDefinition(ogName, attributes, method.ReturnType);

            foreach (var t in method.GenericParameters)
                newMethod.GenericParameters.Add(new GenericParameter(t.Name, newMethod));

            foreach (var param in method.CustomAttributes)
                newMethod.CustomAttributes.Add(param);

            if (method.HasGenericParameters)
                method.IsPublic = true;

            // add preserve attribute to newMethod
            var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
            var constructor = preserveAttribute.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters)
                .Import(module);
            newMethod.CustomAttributes.Add(new CustomAttribute(constructor));

            newMethod.CallingConvention = method.CallingConvention;
            method.CustomAttributes.Clear();

            foreach (var param in method.Parameters)
            {
                if (IsConcreteType(param.ParameterType, out var concreteParam))
                    usedTypes.Add(concreteParam);

                var p = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType);
                newMethod.Parameters.Add(p);

                foreach (var t in param.CustomAttributes)
                    p.CustomAttributes.Add(t);

                p.HasDefault = param.HasDefault;
                p.IsLcid = param.IsLcid;
                if (p.HasDefault)
                    p.Constant = param.Constant;
                p.IsIn = param.IsIn;
                p.IsOptional = param.IsOptional;
                p.IsOut = param.IsOut;
                p.IsReturnValue = param.IsReturnValue;
                p.HasFieldMarshal = param.HasFieldMarshal;
                p.MarshalInfo = param.MarshalInfo;
            }

            var code = newMethod.Body.GetILProcessor();

            var streamType = module.GetTypeDefinition<BitPacker>();
            var rpcType = module.GetTypeDefinition<RPCModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var moduleType = module.GetTypeDefinition<NetworkModule>();
            var hahserType = module.GetTypeDefinition<Hasher>();
            var rpcRequestType = module.GetTypeDefinition<RpcRequest>();
            var rpcSignatureType = module.GetTypeDefinition<RPCSignature>();
            var reqRespModule = module.GetTypeDefinition<RpcRequestResponseModule>();
            var packerType = module.GetTypeDefinition(typeof(Packer)).Import(module);
            var RPCPacketPackerType = module.GetTypeDefinition<RPCPacketPacker>();

            var allocStreamMethod = rpcType.GetMethod("AllocStream").Import(module);
            var freeStreamMethod = rpcType.GetMethod("FreeStream").Import(module);

            var getId = identityType.GetProperty("id");
            var getSceneId = identityType.GetProperty("sceneId");
            var getStableHashU32 = hahserType.GetMethod("GetStableHashU32", true).Import(module);
            var getParent = moduleType.GetProperty("parent").GetMethod.Import(module);
            var targetPlayerField = rpcSignatureType.GetField("targetPlayer").Import(module);

            var getNextId = identityType.GetMethod("GetNextId", true).Import(module);
            var getNextIdNonGeneric = identityType.GetMethod("GetNextId").Import(module);
            var getNextIdStatic = reqRespModule.GetMethod("GetNextIdStatic", true).Import(module);
            var getNextIdStaticNonGeneric = reqRespModule.GetMethod("GetNextIdStatic").Import(module);
            var getNextIdUniTaskNonGeneric = identityType.GetMethod("GetNextIdUniTask").Import(module);
            var getNextIdUniTask = identityType.GetMethod("GetNextIdUniTask", true).Import(module);

            var getNextIdUniTaskStatic = reqRespModule.GetMethod("GetNextIdUniTaskStatic", true).Import(module);
            var getNextIdUniTaskStaticNonGeneric = reqRespModule.GetMethod("GetNextIdUniTaskStatic").Import(module);

            var waitForTask = reqRespModule.GetMethod("WaitForTask").Import(module);

            var reqIdField = rpcRequestType.GetField("id").Import(module);

            // Declare local variables
            newMethod.Body.InitLocals = true;

            var packetType = methodRpc.Signature.isStatic ? module.GetTypeDefinition<StaticRPCPacket>() :
                isNetworkClass ? module.GetTypeDefinition<ChildRPCPacket>() : module.GetTypeDefinition<RPCPacket>();

            var streamVariable = new VariableDefinition(streamType.Import(module));
            var rpcDataVariable = new VariableDefinition(packetType.Import(module));
            var typeHash = new VariableDefinition(module.TypeSystem.UInt32);
            var rpcRequest = new VariableDefinition(rpcRequestType.Import(module));
            var taskWithReturnType = new VariableDefinition(newMethod.ReturnType);
            var rpcSignature = new VariableDefinition(rpcSignatureType.Import(module));

            if (returnMode != ReturnMode.Void)
            {
                newMethod.Body.Variables.Add(taskWithReturnType);
                newMethod.Body.Variables.Add(rpcRequest);
            }

            newMethod.Body.Variables.Add(streamVariable);
            newMethod.Body.Variables.Add(rpcDataVariable);

            if (newMethod.GenericParameters.Count > 0)
                newMethod.Body.Variables.Add(typeHash);
            newMethod.Body.Variables.Add(rpcSignature);

            var paramCount = newMethod.Parameters.Count;

            var endOfRunLocallyCheck = Instruction.Create(OpCodes.Nop);
            var executeRunLocally = Instruction.Create(OpCodes.Nop);

            PushRPCSignature(module, code, methodRpc, false, isNetworkClass);
            code.Append(Instruction.Create(OpCodes.Stloc, rpcSignature));

            var playerType = module.GetTypeDefinition<PlayerID>().Import(module);
            var disposableListType = module.GetTypeDefinition(typeof(DisposableList<>));

            var playersListType = new GenericInstanceType(disposableListType);
            playersListType.GenericArguments.Add(playerType);

            var playersList = new VariableDefinition(playersListType.Import(module));
            var iterator = new VariableDefinition(module.TypeSystem.Int32);
            VariableDefinition currentDeltaPlayerTarget = null;
            VariableDefinition rpcPacker = null;

            Instruction playersLoopStart = null;
            Instruction playersLoopEnd = null;

            bool hasMultipleTargets = methodRpc.Signature.type == RPCType.ObserversRPC ||
                                      methodRpc.Signature.targetPlayerEnumerable != null ||
                                      methodRpc.Signature.targetPlayerList != null;

            bool useDeltaPacking = methodRpc.Signature.deltaPacked && methodRpc.Signature.type != RPCType.ServerRPC;

            if (useDeltaPacking)
            {
                rpcPacker = new VariableDefinition(RPCPacketPackerType.Import(module));
                currentDeltaPlayerTarget = new VariableDefinition(playerType.Import(module));

                var rpcSig_getTargets = rpcSignatureType.GetMethod("GetTargets").Import(module);

                newMethod.Body.Variables.Add(playersList);
                newMethod.Body.Variables.Add(currentDeltaPlayerTarget);
                newMethod.Body.Variables.Add(iterator);
                newMethod.Body.Variables.Add(rpcPacker);

                if (hasMultipleTargets)
                {
                    playersLoopEnd = Instruction.Create(OpCodes.Nop);

                    // playersList = rpcSignature.GetTargets() or GetObservers(signature);
                    if (methodRpc.Signature.type == RPCType.ObserversRPC)
                    {
                        var parentType = GetParentType(module, isNetworkClass, methodRpc.Signature.isStatic);
                        var getObservers = parentType.GetMethod("GetObservers").Import(module);
                        // signature
                        if (!methodRpc.Signature.isStatic)
                            code.Append(Instruction.Create(OpCodes.Ldarg_0));
                        code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                        code.Append(Instruction.Create(OpCodes.Call, getObservers));
                    }
                    else code.Append(Instruction.Create(OpCodes.Call, rpcSig_getTargets));

                    code.Append(Instruction.Create(OpCodes.Stloc, playersList));

                    // i = targets.Count;
                    code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                    var prop = disposableListType.GetProperty("Count");
                    var methodDef = prop.GetMethod;
                    var methodRef = new MethodReference(methodDef.Name, methodDef.ReturnType, playersListType)
                    {
                        HasThis = true
                    };

                    code.Append(Instruction.Create(OpCodes.Call, methodRef.Import(module)));
                    code.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                    code.Append(Instruction.Create(OpCodes.Sub));
                    code.Append(Instruction.Create(OpCodes.Stloc, iterator));

                    // while (i >= 0)
                    playersLoopStart = Instruction.Create(OpCodes.Ldloc, iterator);
                    code.Append(playersLoopStart);
                    code.Append(Instruction.Create(OpCodes.Ldc_I4_0));
                    code.Append(Instruction.Create(OpCodes.Blt, playersLoopEnd));
                    // loop content

                    // this.ModifyManyToOne(ref signature, players.GetAt(i));
                    var rpcModule = module.GetTypeDefinition<RPCModule>();
                    var modifyManyToOne = rpcModule.GetMethod("ModifyManyToOne").Import(module);
                    // ref signature
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                    // players.GetAt(i)
                    var getAtConcrete = playersListType.GetMethodRef("GetAt");
                    code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                    code.Append(Instruction.Create(OpCodes.Ldloc, iterator));
                    code.Append(Instruction.Create(OpCodes.Call, getAtConcrete.Import(module)));
                    code.Append(Instruction.Create(OpCodes.Dup));
                    code.Append(Instruction.Create(OpCodes.Stloc, currentDeltaPlayerTarget));
                    // ();
                    code.Append(Instruction.Create(OpCodes.Call, modifyManyToOne));
                }
            }

            switch (methodRpc.Signature.type)
            {
                case RPCType.ServerRPC:
                    PutIsServerOnStack(module, methodRpc, isNetworkClass, code, moduleType, identityType);
                    code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));
                    break;
                case RPCType.TargetRPC when GetArgType(newMethod.Parameters[0].ParameterType) == TargetArgType.Player:
                {
                    PushLocalPlayerProp(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                    code.Append(Instruction.Create(OpCodes.Ldarg_S, newMethod.Parameters[0]));
                    var areEqualMethod = rpcType.GetMethod("ArePlayersEqual", false).Import(module);
                    code.Append(Instruction.Create(OpCodes.Call, areEqualMethod));
                    code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));
                    break;
                }
            }

            if (returnMode != ReturnMode.Void)
            {
                // Task<bool> nextId = GetNextId<bool>(base.networkManager.localClientConnection, 5f, out request);

                if (methodRpc.Signature.isStatic)
                {
                    var getNextIdRef = returnMode == ReturnMode.UniTask
                        ? getNextIdUniTaskStaticNonGeneric
                        : getNextIdStaticNonGeneric;

                    if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                        newMethod.ReturnType is GenericInstanceType genericInstance)
                    {
                        if (genericInstance.GenericArguments.Count != 1)
                        {
                            Error(messages, "Task must have a single generic argument", method);
                            return null;
                        }

                        var param = genericInstance.GenericArguments[0];

                        if (returnMode == ReturnMode.Task)
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextIdStatic);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                        else
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextIdUniTaskStatic);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                    }

                    // get targetPlayerField
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                    code.Append(Instruction.Create(OpCodes.Ldfld, targetPlayerField));

                    code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)methodRpc.Signature.type));
                    code.Append(Instruction.Create(OpCodes.Ldc_R4, methodRpc.Signature.asyncTimeoutInSec)); // timeout
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcRequest)); // out request
                    code.Append(Instruction.Create(OpCodes.Call, getNextIdRef)); // GetNextIdStatic
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

                    if (isNetworkClass)
                        code.Append(Instruction.Create(OpCodes.Call, getParent)); // parent

                    // this
                    code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)methodRpc.Signature.type));

                    // get targetPlayerField
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                    code.Append(Instruction.Create(OpCodes.Ldfld, targetPlayerField));

                    // localClientConnection
                    code.Append(Instruction.Create(OpCodes.Ldc_R4, methodRpc.Signature.asyncTimeoutInSec)); // timeout
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcRequest)); // out request

                    var getNextIdRef = returnMode == ReturnMode.UniTask
                        ? getNextIdUniTaskNonGeneric
                        : getNextIdNonGeneric;

                    if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                        newMethod.ReturnType is GenericInstanceType genericInstance)
                    {
                        if (genericInstance.GenericArguments.Count != 1)
                        {
                            Error(messages, "Task must have a single generic argument", method);
                            return null;
                        }

                        var param = genericInstance.GenericArguments[0];

                        if (returnMode == ReturnMode.Task)
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextId);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                        else
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextIdUniTask);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                    }

                    code.Append(Instruction.Create(OpCodes.Call, getNextIdRef)); // GetNextId
                }

                if (returnMode == ReturnMode.IEnumerator)
                    code.Append(Instruction.Create(OpCodes.Call, waitForTask));

                code.Append(Instruction.Create(OpCodes.Stloc, taskWithReturnType)); // taskWithReturnType
            }

            code.Append(Instruction.Create(OpCodes.Ldc_I4, 0));
            code.Append(Instruction.Create(OpCodes.Call, allocStreamMethod));
            code.Append(Instruction.Create(OpCodes.Stloc, streamVariable));

            CreateAndSetPacket(module, id, methodRpc, isNetworkClass, rpcType, method, code, streamVariable, getId, getSceneId, rpcDataVariable);

            // create delta packer
            if (useDeltaPacking)
            {
                var createPackerForRPC = RPCPacketPackerType.GetMethod(GetCreateName(methodRpc, isNetworkClass))
                    .Import(module);
                //NetworkManager manager, RPCPacket context, RPCSignature signature, PlayerID target
                PushNetworkManager(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable));
                code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                code.Append(Instruction.Create(OpCodes.Ldloc, currentDeltaPlayerTarget));
                code.Append(Instruction.Create(OpCodes.Call, createPackerForRPC));
                code.Append(Instruction.Create(OpCodes.Stloc, rpcPacker));
            }

            if (returnMode != ReturnMode.Void)
            {
                MethodReference serializeGenericMethod;

                if (useDeltaPacking)
                {
                    serializeGenericMethod = CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, true);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serializeGenericMethod = CreateSerializer(module, module.TypeSystem.UInt32, true);

                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloc, rpcRequest));
                code.Append(Instruction.Create(OpCodes.Ldfld, reqIdField));
                code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
            }

            for (var i = 0; i < newMethod.GenericParameters.Count; i++)
            {
                var param = newMethod.GenericParameters[i];
                var getStableHashU32Generic = new GenericInstanceMethod(getStableHashU32);
                getStableHashU32Generic.GenericArguments.Add(param);

                code.Append(Instruction.Create(OpCodes.Call, getStableHashU32Generic));
                code.Append(Instruction.Create(OpCodes.Stloc, typeHash));

                MethodReference serializeGenericMethod;

                if (useDeltaPacking)
                {
                    serializeGenericMethod = CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, true);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serializeGenericMethod = CreateSerializer(module, module.TypeSystem.UInt32, true);

                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloc, typeHash));
                code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
            }

            for (var i = 0; i < paramCount; i++)
            {
                var param = newMethod.Parameters[i];

                if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0)
                {
                    var argType = GetArgType(param.ParameterType);
                    if (argType == TargetArgType.None)
                    {
                        Error(messages, "TargetRPC method must have a 'PlayerID' as the first parameter", method);
                        return null;
                    }

                    continue;
                }

                if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _))
                    continue;

                MethodReference serializeGenericMethod;

                if (useDeltaPacking)
                {
                    serializeGenericMethod = CreateDeltaSerializer(module, param.ParameterType, rpcPacker, true);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serializeGenericMethod = CreateSerializer(module, param.ParameterType, true);

                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldarg, param));
                code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
            }

            // Call RPCModule.PreProcessRpc(RPCPacket packet, RPCSignature signature, ref BitPacker packer)
            var preProcessRpc = rpcType.GetMethod("PreProcessRpc").Import(module);

            // get rpcDataVariable.data field
            code.Append(Instruction.Create(OpCodes.Ldloca, rpcDataVariable));
            code.Append(Instruction.Create(OpCodes.Ldflda, packetType.GetField("data").Import(module)));

            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature)); // stream
            code.Append(Instruction.Create(OpCodes.Ldloca, streamVariable));
            code.Append(Instruction.Create(OpCodes.Call, preProcessRpc));

            if (!methodRpc.Signature.isStatic)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

            code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable)); // rpcPacket
            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature)); // rpcDetails

            if (methodRpc.Signature.isStatic)
            {
                var sendRpc = rpcType.GetMethod("SendStaticRPC").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            else if (isNetworkClass)
            {
                var sendRpc = module.GetTypeDefinition<NetworkModule>().GetMethod("SendRPC").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            else
            {
                var sendRpc = identityType.GetMethod("SendRPC").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }

            code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
            code.Append(Instruction.Create(OpCodes.Call, freeStreamMethod));

            if (useDeltaPacking && hasMultipleTargets)
            {
                // i -= 1;
                code.Append(Instruction.Create(OpCodes.Ldloc, iterator));
                code.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                code.Append(Instruction.Create(OpCodes.Sub));
                code.Append(Instruction.Create(OpCodes.Stloc, iterator));

                // jump back up
                code.Append(Instruction.Create(OpCodes.Br, playersLoopStart));
                code.Append(playersLoopEnd);

                var disposeConcret = playersListType.GetMethodRef("Dispose");

                // playersList.Dispose();
                code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                code.Append(Instruction.Create(OpCodes.Call, disposeConcret.Import(module)));
            }

            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
            code.Append(Instruction.Create(OpCodes.Ldfld,
                module.GetTypeDefinition<RPCSignature>().GetField("runLocally").Import(module)));
            code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));

            /*if (methodRpc.Signature.type == RPCType.ServerRPC)
            {
                PutIsServerOnStack(module, methodRpc, isNetworkClass, code, moduleType, identityType);
                code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));
            }*/

            code.Append(Instruction.Create(OpCodes.Br, endOfRunLocallyCheck));
            code.Append(executeRunLocally);

            bool hasRpcInfoParam = paramCount > 0 && newMethod.Parameters[^1].ParameterType.FullName == typeof(RPCInfo).FullName;
            if (hasRpcInfoParam)
            {
                var RPCInfo_compileTimeSignatureField = module.GetTypeDefinition<RPCInfo>()
                    .GetField("compileTimeSignature").Import(module);

                var RPCInfo_manager = module.GetTypeDefinition<RPCInfo>()
                    .GetField("manager").Import(module);

                var RPCInfo_sender = module.GetTypeDefinition<RPCInfo>()
                    .GetField("sender").Import(module);

                var RPCInfo_asServer = module.GetTypeDefinition<RPCInfo>()
                    .GetField("asServer").Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarga, newMethod.Parameters[^1]));
                code.Append(Instruction.Create(OpCodes.Dup));
                code.Append(Instruction.Create(OpCodes.Dup));
                code.Append(Instruction.Create(OpCodes.Dup));

                code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_compileTimeSignatureField));

                PushNetworkManager(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_manager));

                PushLocalPlayerProp(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_sender));

                code.Append(Instruction.Create(methodRpc.Signature.type == RPCType.ServerRPC ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_asServer));
            }

            var callMethod = GetOriginalMethod(method);

            if (method.HasGenericParameters)
            {
                var genericInstanceMethod = new GenericInstanceMethod(callMethod);

                for (var i = 0; i < method.GenericParameters.Count; i++)
                {
                    var gp = method.GenericParameters[i];
                    genericInstanceMethod.GenericArguments.Add(gp);
                }

                callMethod = genericInstanceMethod;
            }

            if (!methodRpc.Signature.isStatic)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

            // Packer.Copy<T>
            var copyMethod = packerType.GetMethod("Copy", true).Import(module);

            for (int i = 0; i < paramCount; ++i)
            {
                var param = newMethod.Parameters[i];
                code.Append(Instruction.Create(OpCodes.Ldarg, param)); // param

                bool shouldIgnore = ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _);
                var resolved = param.ParameterType.Resolve();

                if (shouldIgnore || resolved != null && resolved.IsUnmanaged())
                    continue;

                var copyMethodGeneric = new GenericInstanceMethod(copyMethod);
                copyMethodGeneric.GenericArguments.Add(param.ParameterType);

                code.Append(Instruction.Create(OpCodes.Call, copyMethodGeneric)); // Packer.Copy<T>(param)
            }


            code.Append(Instruction.Create(OpCodes.Call, callMethod)); // Call original method

            // Pop return value if not void for now
            code.Append(Instruction.Create(OpCodes.Ret));
            code.Append(endOfRunLocallyCheck);

            if (returnMode != ReturnMode.Void)
                code.Append(Instruction.Create(OpCodes.Ldloc, taskWithReturnType));

            code.Append(Instruction.Create(OpCodes.Ret));

            if (!isServerBuild)
            {
                if (settings.stripServerCode && methodRpc.Signature is { runLocally: false, type: RPCType.ServerRPC })
                {
                    StripBody(method, methodRpc.ogName, settings, methodRpc.Signature.stripCodeMode);
                }
            }

            return newMethod;
        }

        private static void CreateAndSetPacket(ModuleDefinition module, int id, RPCMethod methodRpc, bool isNetworkClass,
            TypeDefinition rpcType, MethodDefinition method, ILProcessor code, VariableDefinition streamVariable,
            PropertyDefinition getId, PropertyDefinition getSceneId, VariableDefinition rpcDataVariable)
        {
            if (methodRpc.Signature.isStatic)
            {
                var buildRawRPCMethod = rpcType.GetMethod("BuildStaticRawRPC", true).Import(module);
                var genericInstanceMethod = new GenericInstanceMethod(buildRawRPCMethod);
                genericInstanceMethod.GenericArguments.Add(method.DeclaringType);

                // rpcId, stream
                code.Append(Instruction.Create(OpCodes.Ldc_I4, id));
                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));

                // BuildStaticRawRPC(int rpcId, NetworkStream stream)
                code.Append(Instruction.Create(OpCodes.Call, genericInstanceMethod));
            }
            else if (isNetworkClass)
            {
                var buildChildRpc = module.GetTypeDefinition<NetworkModule>().GetMethod("BuildRPC").Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, id));
                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Call, buildChildRpc));
            }
            else
            {
                var buildRawRPCMethod = rpcType.GetMethod("BuildRawRPC").Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getId.GetMethod.Import(module))); // id
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getSceneId.GetMethod.Import(module))); // sceneId
                code.Append(Instruction.Create(OpCodes.Ldc_I4, id)); // rpcId
                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable)); // stream

                // BuildRawRPC(int networkId, SceneID sceneId, byte rpcId, NetworkStream stream, RPCDetails details)
                code.Append(Instruction.Create(OpCodes.Call, buildRawRPCMethod));
            }

            code.Append(Instruction.Create(OpCodes.Stloc, rpcDataVariable)); // rpcPacket
        }

        private static void PutIsServerOnStack(ModuleDefinition module, RPCMethod methodRpc, bool isNetworkClass,
            ILProcessor code, TypeDefinition moduleType, TypeDefinition identityType)
        {
            if (methodRpc.Signature.isStatic)
            {
                // NetworkManager.main.isServerOnly
                var managerType = module.GetTypeDefinition<NetworkManager>();
                var mainManagerProp = managerType.GetProperty("main");
                var mainManagerGetter = mainManagerProp.GetMethod.Import(module);
                var getIsServerOnly = module.GetTypeDefinition<NetworkManager>().GetProperty("isServer")
                    .GetMethod.Import(module);

                code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                code.Append(Instruction.Create(OpCodes.Call, getIsServerOnly));
            }
            else if (isNetworkClass)
            {
                var getIsServerOnly = moduleType.GetProperty("isServer");
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getIsServerOnly.GetMethod.Import(module)));
            }
            else
            {
                var getIsServerOnly = identityType.GetProperty("isServer");
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getIsServerOnly.GetMethod.Import(module)));
            }
        }

        private static MethodReference CreateDeltaSerializer(ModuleDefinition module, TypeReference type, VariableDefinition packer, bool isWrite)
        {
            var method = packer.VariableType.GetMethod(isWrite ? "Write" : "Read", true).Import(module);

            var genericMethod = new GenericInstanceMethod(method);
            genericMethod.GenericArguments.Add(type);

            try
            {
                return genericMethod.Import(module);
            }
            catch (Exception e)
            {
                throw new Exception(
                    $"Failed to import method '{genericMethod.FullName}'. Module: {module.Name}, Target: {type.FullName}",
                    e);
            }
        }

        private static MethodReference CreateSerializer(ModuleDefinition module, TypeReference type, bool isWrite)
        {
            var packerType = module.GetTypeDefinition(typeof(Packer<>)).Import(module);
            var method = packerType.GetMethod(isWrite ? "Write" : "Read").Import(module);

            var genericPackerType = new GenericInstanceType(packerType);
            genericPackerType.GenericArguments.Add(type);

            var genericWriteMethod =
                new MethodReference(method.Name, method.ReturnType, genericPackerType.Import(module))
                {
                    HasThis = method.HasThis
                };

            foreach (var param in method.Parameters)
                genericWriteMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                    param.ParameterType));

            try
            {
                return genericWriteMethod.Import(module);
            }
            catch (Exception e)
            {
                throw new Exception(
                    $"Failed to import method '{genericWriteMethod.FullName}'. Module: {module.Name}, Target: {type.FullName}",
                    e);
            }
        }

        enum TargetArgType
        {
            None,
            Player,
            Enumerator,
            List
        }

        static TargetArgType GetArgType(TypeReference type)
        {
            if (type == null)
                return TargetArgType.None;

            if (type.FullName == typeof(PlayerID).FullName)
                return TargetArgType.Player;

            if (type.IsArray && type.GetElementType().FullName == typeof(PlayerID).FullName)
                return TargetArgType.List;

            if (!(type is GenericInstanceType { HasGenericArguments: true } genType) ||
                genType.GenericArguments[0].FullName != typeof(PlayerID).FullName)
            {
                return TargetArgType.None;
            }

            var resolved = type.Resolve();

            if (GenerateSerializersProcessor.HasInterfaceRaw(resolved, typeof(IList<>)))
                return TargetArgType.List;

            if (GenerateSerializersProcessor.HasInterfaceRaw(resolved, typeof(IEnumerable<>)))
                return TargetArgType.Enumerator;

            return TargetArgType.None;
        }

        private static void PushRPCSignature(ModuleDefinition module, ILProcessor code, RPCMethod rpc,
            bool isReceiving, bool isNetworkModule)
        {
            var rpcDetails = module.GetTypeDefinition<RPCSignature>();
            var makeRpcDetails = rpcDetails.GetMethod("Make").Import(module);
            var makeRpcDetailsTarget = rpcDetails.GetMethod("MakeWithTarget").Import(module);

            // RPCDetails Make(RPCType type, Channel channel, bool runLocally, bool bufferLast, bool requireServer, bool excludeOwner)
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.type));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.channel));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.runLocally ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.requireOwnership ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.bufferLast ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.requireServer ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.excludeOwner ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldstr, rpc.ogName));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.isStatic ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_R4, rpc.Signature.asyncTimeoutInSec));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.compressionLevel));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.excludeSender ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.deltaPacked ? 1 : 0));

            if (rpc.Signature.type == RPCType.TargetRPC)
            {
                if (!isReceiving)
                {
                    var argType = rpc.originalMethod.Parameters[0].ParameterType;

                    var type = GetArgType(argType);

                    switch (type)
                    {
                        case TargetArgType.Player:
                        {
                            code.Append(rpc.Signature.isStatic
                                ? Instruction.Create(OpCodes.Ldarg_0)
                                : Instruction.Create(OpCodes.Ldarg_1));
                            ConvertPlayerIDToNullable(module, code);

                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            break;
                        }
                        case TargetArgType.List:
                        {
                            LoadDefaultPlayerId(module, code);
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            code.Append(rpc.Signature.isStatic
                                ? Instruction.Create(OpCodes.Ldarg_0)
                                : Instruction.Create(OpCodes.Ldarg_1));
                            break;
                        }
                        case TargetArgType.Enumerator:
                        {
                            LoadDefaultPlayerId(module, code);
                            code.Append(rpc.Signature.isStatic
                                ? Instruction.Create(OpCodes.Ldarg_0)
                                : Instruction.Create(OpCodes.Ldarg_1));
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            break;
                        }
                        case TargetArgType.None:
                        default:
                        {
                            LoadDefaultPlayerId(module, code);
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            break;
                        }
                    }
                }
                else
                {
                    bool isStatic = rpc.Signature.isStatic;
                    PushLocalPlayerProp(module, code, isNetworkModule, isStatic);
                    ConvertPlayerIDToNullable(module, code);
                    code.Append(Instruction.Create(OpCodes.Ldnull));
                    code.Append(Instruction.Create(OpCodes.Ldnull));
                }

                code.Append(Instruction.Create(OpCodes.Call, makeRpcDetailsTarget));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Call, makeRpcDetails));
            }
        }

        private static void LoadDefaultPlayerId(ModuleDefinition module, ILProcessor code)
        {
            var playdIdType = module.GetTypeDefinition<PlayerID>();
            playdIdType.Import(module);
            var getNullableMethod = playdIdType.GetMethod("GetDefaultNullable").Import(module);
            code.Append(Instruction.Create(OpCodes.Call, getNullableMethod));
        }

        private static void ConvertPlayerIDToNullable(ModuleDefinition module, ILProcessor code)
        {
            var playdIdType = module.GetTypeDefinition<PlayerID>();
            playdIdType.Import(module);
            var getNullableMethod = playdIdType.GetMethod("GetNullable").Import(module);
            code.Append(Instruction.Create(OpCodes.Call, getNullableMethod));
        }

        private static TypeReference GetParentType(ModuleDefinition module, bool isNetworkModule, bool isStatic)
        {
            if (!isStatic)
            {
                return isNetworkModule
                    ? module.GetTypeDefinition<NetworkModule>().Import(module)
                    : module.GetTypeDefinition<NetworkIdentity>().Import(module);
            }

            return module.GetTypeDefinition<RPCModule>().Import(module);
        }

        private static void PushLocalPlayerProp(ModuleDefinition module, ILProcessor code, bool isNetworkModule, bool isStatic)
        {
            if (!isStatic)
            {
                var localPlayerProp =
                    isNetworkModule
                        ? module.GetTypeDefinition<NetworkModule>().GetProperty("localPlayerForced").GetMethod
                            .Import(module)
                        : module.GetTypeDefinition<NetworkIdentity>().GetProperty("localPlayerForced").GetMethod
                            .Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, localPlayerProp));
            }
            else
            {
                var rpcModule = module.GetTypeDefinition<RPCModule>();
                var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
            }
        }

        private static bool UpdateMethodReferences(ModuleDefinition module, MethodReference old, MethodReference @new,
            [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            using var types =  DisposableList<TypeDefinition>.Create(32);
            var startLocalExecutionFlag = module.GetTypeDefinition(typeof(PurrCompilerFlags))
                .GetMethod("EnterLocalExecution").FullName;
            var exitLocalExecutionFlag = module.GetTypeDefinition(typeof(PurrCompilerFlags))
                .GetMethod("ExitLocalExecution").FullName;

            types.AddRange(module.Types);
            foreach (var type in module.Types)
            {
                types.AddRange(type.NestedTypes);
            }

            bool isSkipping = false;

            for (var tidx = 0; tidx < types.Count; tidx++)
            {
                var type = types[tidx];
                foreach (var method in type.Methods)
                {
                    if (method == @new || method.GetElementMethod() == @new) continue;

                    if (method.Body == null) continue;

                    var processor = method.Body.GetILProcessor();

                    bool hasLocalModeAttribute = method.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == typeof(LocalModeAttribute).FullName);

                    if (hasLocalModeAttribute)
                        continue;

                    for (var i = 0; i < method.Body.Instructions.Count; i++)
                    {
                        var instruction = method.Body.Instructions[i];

                        if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference
                            {
                                DeclaringType: not null
                            } flag)
                        {
                            if (flag.FullName == startLocalExecutionFlag)
                            {
                                //processor.Replace(instruction, Instruction.Create(OpCodes.Nop));
                                if (isSkipping)
                                {
                                    Error(messages, "Local mode flag was already set, avoid nesting these flags.",
                                        method);
                                    return false;
                                }

                                isSkipping = true;
                                continue;
                            }

                            if (flag.FullName == exitLocalExecutionFlag)
                            {
                                //processor.Replace(instruction, Instruction.Create(OpCodes.Nop));
                                if (!isSkipping)
                                {
                                    Error(messages,
                                        "Local mode flag was not set, you should first call <b>PurrCompilerFlags.EnterLocalExecution()</b>",
                                        method);
                                    return false;
                                }

                                isSkipping = false;
                                continue;
                            }
                        }

                        if (isSkipping)
                            continue;

                        if (instruction.Operand is MethodReference methodReference &&
                            methodReference.GetElementMethod() == old)
                        {
                            var newRef = GenerateNewRef(@new, methodReference);
                            processor.Replace(instruction, Instruction.Create(instruction.OpCode, newRef));
                        }
                    }

                    if (isSkipping)
                    {
                        Error(messages,
                            "Local mode flag was not unset, you should call <b>PurrCompilerFlags.ExitLocalExecution()</b>",
                            method);
                        return false;
                    }
                }
            }

            return true;
        }

        private static MethodReference GenerateNewRef(MethodReference @new, MethodReference methodReference)
        {
            // Check if methodReference is a MethodDefinition for a deeper copy if possible
            var methodDefinition = methodReference.Resolve();

            // Start with a MethodReference pointing to the new definition, copying name and return type
            var newRef = new MethodReference(@new.Name, @new.ReturnType, @new.DeclaringType)
            {
                HasThis = methodReference.HasThis,
                ExplicitThis = methodReference.ExplicitThis,
                CallingConvention = methodReference.CallingConvention,
            };

            // Clone parameters with exact types and attributes
            foreach (var parameter in methodDefinition.Parameters)
            {
                var newParameterType = parameter.ParameterType;
                if (newParameterType is GenericParameter && newRef.GenericParameters.Count > 0)
                {
                    // Ensure matching GenericParameter
                    var matchedParameter =
                        newRef.GenericParameters.FirstOrDefault(p => p.Name == newParameterType.Name);
                    if (matchedParameter != null) newParameterType = matchedParameter;
                }

                newRef.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, newParameterType));
            }

            // Handle generic parameters exactly as defined in the MethodDefinition
            foreach (var genericParameter in methodDefinition.GenericParameters)
            {
                var newGenericParameter = new GenericParameter(genericParameter.Name, newRef);
                newRef.GenericParameters.Add(newGenericParameter);
            }

            // If the methodReference is a GenericInstanceMethod, convert newRef to match
            if (methodReference is GenericInstanceMethod ogGenericMethodRef)
            {
                var newGenericInstanceMethod = new GenericInstanceMethod(newRef);

                // Match each generic argument exactly
                foreach (var argument in ogGenericMethodRef.GenericArguments)
                    newGenericInstanceMethod.GenericArguments.Add(argument);

                // Assign the generic instance method back to newRef
                newRef = newGenericInstanceMethod;
            }

            return newRef;
        }

        static DisposableList<TypeDefinition> GetAllTypes(ModuleDefinition module)
        {
            var types = DisposableList<TypeDefinition>.Create(32);

            types.AddRange(module.Types);
            foreach (var type in module.Types)
                types.AddRange(type.NestedTypes);

            return types;
        }


        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            try
            {
                if (!WillProcess(compiledAssembly))
                    return null!;

                var settings = PurrNetSettings.GetOrCreateSettings();
                bool isEditor = false;
                bool isServerBuild = false;

                foreach (var define in compiledAssembly.Defines)
                {
                    switch (define)
                    {
                        case "UNITY_EDITOR":
                            isEditor = true;
                            break;
                        case "UNITY_SERVER":
                            isServerBuild = true;
                            break;
                    }
                }

                if (isEditor)
                    isServerBuild = true;

                var visitedTypes = new HashSet<string>(128);
                var typesToGenerateSerializer = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);
                var typesToPrepareHasher = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);
                var typesToIgnoreForDelta = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);
                var typesToIgnoreForSerialization = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);

                var messages = new List<DiagnosticMessage>(32);

                using var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
                using var pdbStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData);
                var resolver = new AssemblyResolver(compiledAssembly);

                var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, new ReaderParameters
                {
                    ReadSymbols = true,
                    SymbolStream = pdbStream,
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    AssemblyResolver = resolver
                });

                resolver.SetSelf(assemblyDefinition);

                for (var m = 0; m < assemblyDefinition.Modules.Count; m++)
                {
                    var module = assemblyDefinition.Modules[m];
                    var hasPurrNetAsReference = HasPurrNetAsReference(compiledAssembly.Name, module);

                    using var types = GetAllTypes(module);
                    var usedTypes = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);

                    for (var t = 0; t < types.Count; t++)
                    {
                        if (types[t].HasInterfaces)
                        {
                            try
                            {
                                var mathInterfaceName = typeof(IMath<>).FullName;

                                for (var i = 0; i < types[t].Interfaces.Count; i++)
                                {
                                    var reference = types[t].Interfaces[i].InterfaceType;
                                    var resolved = reference.Resolve();

                                    if (resolved == null)
                                        continue;

                                    if (resolved.FullName == mathInterfaceName &&
                                        reference is GenericInstanceType genRef &&
                                        genRef.GenericArguments.Count == 1)
                                    {
                                        GenerateAutoMathProcessor.HandleType(types[t], genRef.GenericArguments[0],
                                            reference, messages);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                messages.Add(new DiagnosticMessage
                                {
                                    DiagnosticType = DiagnosticType.Error,
                                    MessageData = $"IMath: {e.Message}\n{e.StackTrace}"
                                });
                            }
                        }

                        UnityProxyProcessor.Process(types[t], messages);
                        RegisterSerializersProcessor.HandleType(module, types[t], typesToIgnoreForDelta,
                            typesToIgnoreForSerialization);

                        var type = types[t];

                        if (!hasPurrNetAsReference)
                            continue;

                        // check if it has RegisterNetworkTypeAttribute
                        foreach (var customAttribute in type.CustomAttributes)
                        {
                            if (customAttribute.AttributeType.FullName == typeof(RegisterNetworkTypeAttribute).FullName)
                            {
                                foreach (var field in customAttribute.ConstructorArguments)
                                {
                                    if (field.Value is TypeReference tref)
                                        typesToGenerateSerializer.Add(tref);
                                }
                            }
                        }

                        if (GenerateSerializersProcessor.HasInterface(type, typeof(IPackedAuto)) ||
                            GenerateSerializersProcessor.HasInterface(type, typeof(IPacked)) ||
                            GenerateSerializersProcessor.HasInterface(type, typeof(IPackedSimple)))
                        {
                            typesToGenerateSerializer.Add(type);
                        }


                        if (!type.IsClass)
                        {
                            for (var i = 0; i < type.Methods.Count; i++)
                            {
                                var method = type.Methods[i];
                                ProcessServerOnlyMethods(method, settings, isServerBuild, isEditor);
                                ProcessRunContextGuardedMethods(method, settings, false);
                            }
                            continue;
                        }

                        var idFullName = typeof(NetworkIdentity).FullName;
                        var classFullName = typeof(NetworkModule).FullName;

                        bool inheritsFromNetworkIdentity =
                            type.FullName == idFullName || InheritsFrom(type, idFullName);
                        bool inheritsFromNetworkClass =
                            type.FullName == classFullName || InheritsFrom(type, classFullName);

                        using var _rpcMethods = DisposableList<RPCMethod>.Create(32);

                        int idOffset = GetIDOffset(type, messages);

                        if (inheritsFromNetworkIdentity || inheritsFromNetworkClass)
                        {
                            var _networkFields = new List<FieldDefinition>();

                            // add the Preserve attribute
                            if (type.CustomAttributes.All(x =>
                                    x.AttributeType.FullName != typeof(PreserveAttribute).FullName))
                            {
                                var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
                                var constructor = preserveAttribute.Resolve().Methods
                                    .First(md => md.IsConstructor && !md.HasParameters).Import(module);
                                type.CustomAttributes.Add(new CustomAttribute(constructor));
                            }

                            IncludeAnyConcreteGenericParameters(type, typesToGenerateSerializer);
                            FindNetworkModules(type, classFullName, _networkFields);
                            CreateSyncVarInitMethod(inheritsFromNetworkIdentity, module, type, _networkFields);
                        }

                        for (var i = 0; i < type.Methods.Count; i++)
                        {
                            try
                            {
                                var method = type.Methods[i];
                                ProcessServerOnlyMethods(method, settings, isServerBuild, isEditor);
                                ProcessRunContextGuardedMethods(method, settings, inheritsFromNetworkIdentity);

                                if (inheritsFromNetworkIdentity && MakeSureOverrideIsCalled.ShouldProcess(method))
                                    MakeSureOverrideIsCalled.Process(method, messages);

                                if (method.DeclaringType.FullName != type.FullName)
                                    continue;

                                var rpcType = GetMethodRPCType(method, messages);

                                if (rpcType == null)
                                    continue;

                                if (!rpcType.Value.isStatic && !inheritsFromNetworkIdentity &&
                                    !inheritsFromNetworkClass)
                                {
                                    string inheritsFrom = type.BaseType?.FullName ?? "null";
                                    Error(messages,
                                        $"RPC must be static if not inheriting from NetworkIdentity or NetworkClass | {inheritsFrom}",
                                        method);
                                    continue;
                                }

                                _rpcMethods.Add(new RPCMethod
                                {
                                    Signature = rpcType.Value, originalMethod = method, ogName = method.Name
                                });
                            }
                            catch (Exception e)
                            {
                                Error(messages, e.Message + "\n" + e.StackTrace, type.Methods[i]);
                            }
                        }

                        switch (inheritsFromNetworkIdentity)
                        {
                            case false when !inheritsFromNetworkClass && _rpcMethods.Count == 0:
                                continue;
                            case false when !inheritsFromNetworkClass && !inheritsFromNetworkIdentity:
                                typesToPrepareHasher.Add(type);
                                break;
                        }

                        if (inheritsFromNetworkIdentity || inheritsFromNetworkClass)
                            typesToGenerateSerializer.Add(type);

                        for (var index = 0; index < _rpcMethods.Count; index++)
                        {
                            var method = _rpcMethods[index].originalMethod;

                            try
                            {
                                var newMethod = HandleRPC(module, idOffset + index, _rpcMethods[index],
                                    inheritsFromNetworkClass, isServerBuild, settings, usedTypes, messages);

                                if (newMethod != null && method.DeclaringType != null)
                                {
                                    type.Methods.Add(newMethod);
                                    if (!UpdateMethodReferences(module, method, newMethod, messages))
                                        return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
                                }
                            }
                            catch (Exception e)
                            {
                                Error(messages, "SendRPCFailed: " + e.Message + "\n" + e.StackTrace, method);
                            }
                        }

                        try
                        {
                            if (_rpcMethods.Count > 0)
                                HandleRPCReceiver(module, type, _rpcMethods, inheritsFromNetworkClass, idOffset);

                            if (type.FullName != typeof(NetworkIdentity).FullName &&
                                type.FullName != typeof(NetworkModule).FullName &&
                                (inheritsFromNetworkIdentity || inheritsFromNetworkClass))
                            {
                                HandleRPCReceiverHandler(module, type, _rpcMethods, inheritsFromNetworkClass,
                                    idOffset,
                                    false);
                            }

                        }
                        catch (Exception e)
                        {
                            messages.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"HandleRPCReceiver [{type.Name}]: {e.Message}\n{e.StackTrace}"
                            });
                        }
                    }

                    if (hasPurrNetAsReference)
                    {
                        try
                        {
                            FindUsedTypes(module, types, usedTypes);

                            foreach (var usedType in usedTypes)
                            {
                                if (IsTypeInOwnModule(usedType, module))
                                    typesToGenerateSerializer.Add(usedType);
                            }
                        }
                        catch (Exception e)
                        {
                            messages.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"FindUsedTypes {e.Message}\n{e.StackTrace}"
                            });
                        }
                    }
                }

                ExpandNested(assemblyDefinition, typesToGenerateSerializer);

                // remove any typesToGenerateSerializer from typesToPrepareHasher
                typesToPrepareHasher.ExceptWith(typesToGenerateSerializer);

                foreach (var typeRef in typesToGenerateSerializer)
                    GenerateSerializersProcessor.HandleType(false, assemblyDefinition, typeRef, visitedTypes, isEditor,
                        typesToIgnoreForSerialization, typesToIgnoreForDelta);

                foreach (var typeRef in typesToPrepareHasher)
                    GenerateSerializersProcessor.HandleType(true, assemblyDefinition, typeRef, visitedTypes, isEditor,
                        typesToIgnoreForSerialization, typesToIgnoreForDelta);

                var pe = new MemoryStream();
                var pdb = new MemoryStream();

                var writerParameters = new WriterParameters
                {
                    WriteSymbols = true,
                    SymbolStream = pdb,
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                };

                try
                {
                    assemblyDefinition.Write(pe, writerParameters);
                }
                catch (Exception e)
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData =
                            $"Failed to write assembly ({compiledAssembly.Name}): {e.Message}\n{e.StackTrace}",
                    });
                }

                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), messages);
            }
            catch (Exception e)
            {
                var messages = new List<DiagnosticMessage>
                {
                    new()
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = $"Unhandled exception {e.Message}\n{e.StackTrace}",
                    }
                };

                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
            }
        }

        private static bool HasPurrNetAsReference(string myName, ModuleDefinition module)
        {
            if (myName == "PurrNet.Runtime")
                return true;

            bool hasPurrNetAsReference = false;

            foreach (var reference in module.AssemblyReferences)
            {
                if (reference.Name == "PurrNet.Runtime")
                {
                    hasPurrNetAsReference = true;
                    break;
                }
            }

            return hasPurrNetAsReference;
        }

        private static void ProcessServerOnlyMethods(MethodDefinition method, PurrNetSettings settings, bool serverBuild, bool isEditor)
        {
            CustomAttribute serverOnlyAttribute = null;

            foreach (var attribute in method.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == typeof(ServerOnlyAttribute).FullName)
                {
                    serverOnlyAttribute = attribute;
                    break;
                }
            }

            if (serverOnlyAttribute == null)
                return;

            if (serverBuild && !isEditor)
                return;

            var stripCodeMode = (StripCodeModeOverride)serverOnlyAttribute.ConstructorArguments[0].Value;
            PutServerCheck(method, settings, stripCodeMode);
            if (!serverBuild)
                StripBody(method, method.Name, settings, stripCodeMode);
        }

        static void PutServerCheck(MethodDefinition method, PurrNetSettings settings, StripCodeModeOverride modeOverride)
        {
            var il = method.Body.GetILProcessor();
            var instructions = method.Body.Instructions;
            var module = method.Module;

            var getIsServer = GetIsServerMethod(module);
            var firstInstruction = instructions[0];
            var ogInstructionCount = instructions.Count;

            // at the end, return default value
            var mode = GetMode(settings, modeOverride);
            if (mode == StripCodeMode.DoNotStrip)
                mode = StripCodeMode.ReplaceWithEmptyMethod;
            AppendStripAction(method, method.Name, mode, il, "server only");

            var returnDefaultInst = instructions[ogInstructionCount];

            il.InsertBefore(firstInstruction, getIsServer);
            il.InsertAfter(getIsServer, Instruction.Create(OpCodes.Brfalse, returnDefaultInst));
        }

        private static Instruction GetIsServerMethod(ModuleDefinition module)
        {
            var isServerOnlyProp = module.GetTypeDefinition<NetworkManager>()
                .GetProperty("isServerStatic")
                .GetMethod.Import(module);
            var getIsServer = Instruction.Create(OpCodes.Call, isServerOnlyProp);
            return getIsServer;
        }

        private static Instruction GetIsClientMethod(ModuleDefinition module)
        {
            var isClientProp = module.GetTypeDefinition<NetworkManager>()
                .GetProperty("isClientStatic")
                .GetMethod.Import(module);
            var getIsClient = Instruction.Create(OpCodes.Call, isClientProp);
            return getIsClient;
        }

        private static void ProcessRunContextGuardedMethods(MethodDefinition method, PurrNetSettings settings, bool inheritsFromNetworkIdentity)
        {
            if (method.Body == null) return;

            bool hasServer = false;
            bool hasClient = false;
            bool hasOwner = false;

            var guardFailureActionOverride = GuardFailureActionOverride.Settings;

            foreach (var attribute in method.CustomAttributes)
            {
                string fullName = attribute.AttributeType.FullName;
                if (!hasServer && fullName == typeof(ServerAttribute).FullName)
                {
                    hasServer = true;
                }
                else if (!hasClient && fullName == typeof(ClientAttribute).FullName)
                {
                    hasClient = true;
                }
                else if (!hasOwner && fullName == typeof(OwnerAttribute).FullName)
                {
                    hasOwner = true;
                }
                else if (fullName == typeof(GuardFailureActionAttribute).FullName)
                {
                    guardFailureActionOverride = (GuardFailureActionOverride)attribute.ConstructorArguments[0].Value;
                }
            }

            if (!hasServer && !hasClient && !hasOwner)
                return;

            var guardFailureAction = GetMode(settings, guardFailureActionOverride);
            if (guardFailureAction == GuardFailureAction.Ignore) return;

            // insert guard
            var il = method.Body.GetILProcessor();
            var firstInstruction = method.Body.Instructions[0];
            var module = method.Module;

            var failureHandler = CreateGuardFailureActionHandler(method, il, guardFailureAction);

            if (hasServer)
            {
                var callIsServer = GetIsServerMethod(module);
                il.InsertBefore(firstInstruction, callIsServer);
                il.InsertAfter(callIsServer, il.Create(OpCodes.Brtrue, firstInstruction));
            }

            if (hasClient)
            {
                var callIsClient = GetIsClientMethod(module);
                il.InsertBefore(firstInstruction, callIsClient);
                il.InsertAfter(callIsClient, il.Create(OpCodes.Brtrue, firstInstruction));
            }

            if (hasOwner)
            {
                if (!inheritsFromNetworkIdentity)
                    throw new InvalidOperationException($"[Owner] requires {method.DeclaringType.Name} to inherit NetworkIdentity.");

                CheckNetworkIdentityProperty("isOwner", module, il, firstInstruction);
            }

            il.InsertBefore(firstInstruction, il.Create(OpCodes.Br, failureHandler));
        }

        private static Instruction CreateGuardFailureActionHandler(
            MethodDefinition method,
            ILProcessor il,
            GuardFailureAction action)
        {
            var handler = il.Create(OpCodes.Nop);
            il.Append(handler);

            switch (action)
            {
                case GuardFailureAction.ReturnDefault:
                    break;
                case GuardFailureAction.ThrowException:
                    var throwExcep = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("ThrowUnsupportedException", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{method.Name}' cannot be called from this context."));
                    il.Append(il.Create(OpCodes.Call, throwExcep));
                    break;
                case GuardFailureAction.LogWarning:
                    var logWarningMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerWarning", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{method.Name}' cannot be called from this context."));
                    il.Append(il.Create(OpCodes.Call, logWarningMethod));
                    break;
                case GuardFailureAction.LogError:
                    var logErrorMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerError", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{method.Name}' cannot be called from this context."));
                    il.Append(il.Create(OpCodes.Call, logErrorMethod));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ProperlyEndMethod(method, il);

            return handler;
        }

        private static void CheckNetworkIdentityProperty(string property, ModuleDefinition module, ILProcessor il, Instruction target)
        {
            var networkIdentityType = module.GetTypeDefinition<NetworkIdentity>().Resolve();
            var isOwnerGetter = networkIdentityType
                .GetProperty(property)
                .GetMethod.Import(module);

            var ldarg0 = il.Create(OpCodes.Ldarg_0);
            il.InsertBefore(target, ldarg0);
            il.InsertAfter(ldarg0, il.Create(OpCodes.Callvirt, isOwnerGetter));
            il.InsertAfter(ldarg0.Next, il.Create(OpCodes.Brtrue, target));
        }

        private static void IncludeAnyConcreteGenericParameters(TypeDefinition type,
            HashSet<TypeReference> typesToGenerateSerializer)
        {
            if (type.BaseType is GenericInstanceType genericType)
            {
                foreach (var genericParameter in genericType.GenericArguments)
                {
                    if (IsConcreteType(genericParameter, out var concreteType))
                    {
                        if (concreteType != null)
                            typesToGenerateSerializer.Add(concreteType);
                    }
                }
            }
        }

        private static void FindNetworkModules(TypeDefinition type, string classFullName,
            List<FieldDefinition> _networkFields)
        {
            for (var i = 0; i < type.Fields.Count; i++)
            {
                var field = type.Fields[i];

                if (field.IsStatic) continue;

                var fieldType = field.FieldType.Resolve();

                if (fieldType == null) continue;

                var isNetworkClass = fieldType.FullName == classFullName || InheritsFrom(fieldType, classFullName);

                if (!isNetworkClass) continue;

                _networkFields.Add(field);
            }
        }

        private static void ExpandNested(AssemblyDefinition assembly, HashSet<TypeReference> typesToHandle)
        {
            HashSet<TypeReference> visited = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);
            HashSet<TypeReference> visited2 = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);
            var copy = typesToHandle.ToArray();

            for (var i = 0; i < copy.Length; i++)
            {
                var type = copy[i];
                if (type is GenericInstanceType genericInstance)
                    AddNestedGenerics(assembly, genericInstance, typesToHandle, visited2);
                AddNestedFields(assembly, type, typesToHandle, visited);
            }
        }

        private static void AddNestedGenerics(AssemblyDefinition assembly, GenericInstanceType type,
            HashSet<TypeReference> typesToHandle, HashSet<TypeReference> visited)
        {
            for (int i = 0; i < type.GenericArguments.Count; i++)
            {
                var argument = type.GenericArguments[i];

                if (!visited.Add(argument))
                    continue;

                if (argument is GenericInstanceType genericInstance)
                {
                    AddNestedGenerics(assembly, genericInstance, typesToHandle, visited);
                }
                else if (IsTypeInOwnModule(argument, assembly.MainModule))
                {
                    typesToHandle.Add(argument);
                }
            }
        }

        private static void AddNestedFields(AssemblyDefinition assembly, TypeReference reference,
            HashSet<TypeReference> typesToHandle, HashSet<TypeReference> visited)
        {
            var fields = GetConcreteFields(reference);

            foreach (var field in fields)
            {
                if (!visited.Add(field))
                    continue;

                if (field is GenericInstanceType genericInstance)
                {
                    bool containsRelevantTypes = false;

                    // Add the GenericInstanceType itself if fully concrete
                    if (genericInstance.GenericArguments.All(IsResolved))
                    {
                        typesToHandle.Add(field);
                        containsRelevantTypes = true;
                    }

                    // Check the generic arguments
                    for (int i = 0; i < genericInstance.GenericArguments.Count; i++)
                    {
                        var argument = genericInstance.GenericArguments[i];
                        var resolvedArg = argument.Resolve();

                        if (resolvedArg != null)
                        {
                            typesToHandle.Add(argument);
                            containsRelevantTypes = true;
                        }

                        AddNestedFields(assembly, argument, typesToHandle, visited);
                    }

                    // If the GenericInstanceType contains relevant arguments, add it
                    if (containsRelevantTypes)
                    {
                        typesToHandle.Add(field);
                    }

                    AddNestedFields(assembly, field, typesToHandle, visited);
                }
                else if (IsTypeInOwnModule(field, assembly.MainModule))
                {
                    // Handle non-generic field types
                    typesToHandle.Add(field);
                    AddNestedFields(assembly, field, typesToHandle, visited);
                }
            }
        }

        static List<TypeReference> GetConcreteFields(TypeReference typeReference)
        {
            List<TypeReference> concreteFields = new List<TypeReference>();

            if (typeReference is GenericInstanceType genericInstance)
            {
                // Resolve the type definition
                TypeDefinition typeDef = typeReference.Resolve();
                if (typeDef == null)
                {
                    throw new InvalidOperationException($"Could not resolve type: {typeReference.FullName}");
                }

                // Map generic parameters to concrete arguments
                Dictionary<string, TypeReference> genericMapping = new Dictionary<string, TypeReference>();
                for (int i = 0; i < genericInstance.GenericArguments.Count; i++)
                {
                    string paramName = typeDef.GenericParameters[i].Name;
                    TypeReference concreteType = genericInstance.GenericArguments[i];
                    genericMapping[paramName] = concreteType;
                }

                // Process each field
                foreach (var field in typeDef.Fields)
                {
                    TypeReference fieldType = field.FieldType;

                    // Substitute generic parameters with concrete arguments
                    TypeReference concreteFieldType = SubstituteGenericParameters(fieldType, genericMapping);
                    concreteFields.Add(concreteFieldType);
                }
            }
            else
            {
                TypeDefinition typeDef = typeReference.Resolve();

                if (typeDef != null)
                {
                    foreach (var field in typeDef.Fields)
                        concreteFields.Add(field.FieldType);
                }
            }

            return concreteFields;
        }

        static TypeReference SubstituteGenericParameters(TypeReference type,
            Dictionary<string, TypeReference> genericMapping)
        {
            if (type is GenericInstanceType genericInstance)
            {
                // Substitute each generic argument recursively
                GenericInstanceType concreteGenericInstance = new GenericInstanceType(genericInstance.ElementType);
                foreach (var argument in genericInstance.GenericArguments)
                {
                    concreteGenericInstance.GenericArguments.Add(SubstituteGenericParameters(argument, genericMapping));
                }

                return concreteGenericInstance;
            }
            else if (type is GenericParameter genericParameter)
            {
                // Replace the generic parameter with its mapped concrete type
                if (genericMapping.TryGetValue(genericParameter.Name, out var concreteType))
                {
                    return concreteType;
                }
            }

            // Return the type as is if no substitution is needed
            return type;
        }

        private static bool IsResolved(TypeReference type)
        {
            return type.Resolve() != null;
        }

        private static void CreateSyncVarInitMethod(bool isNetworkIdentity, ModuleDefinition module,
            TypeDefinition type, List<FieldDefinition> networkFields)
        {
            var methodName = GenerateSerializersProcessor.MakeFullNameValidCSharp(type.Name);
            var newMethod = new MethodDefinition($"__{methodName}_CodeGen_Initialize",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);

            var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
            var constructor = preserveAttribute.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters)
                .Import(module);
            newMethod.CustomAttributes.Add(new CustomAttribute(constructor));

            var parentStr = new ParameterDefinition("parent", ParameterAttributes.None, module.TypeSystem.String);

            if (!isNetworkIdentity)
                newMethod.Parameters.Add(parentStr);

            type.Methods.Add(newMethod);

            newMethod.Body.InitLocals = true;

            var code = newMethod.Body.GetILProcessor();

            var parentType =
                (isNetworkIdentity
                    ? module.GetTypeDefinition<NetworkIdentity>()
                    : module.GetTypeDefinition<NetworkModule>()).Import(module);

            var registerModule = parentType.GetMethod("RegisterModuleInternal").Import(module);
            var concatMethod = module.TypeSystem.String.Resolve()
                .GetMethod("Concat", module.TypeSystem.String, module.TypeSystem.String).Import(module);

            for (int i = 0; i < networkFields.Count; i++)
            {
                var field = networkFields[i];

                // add the Preserve attribute to field
                if (field.CustomAttributes.All(x => x.AttributeType.FullName != typeof(PreserveAttribute).FullName))
                    type.CustomAttributes.Add(new CustomAttribute(constructor));

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldstr, field.Name));
                code.Append(Instruction.Create(OpCodes.Ldstr, field.FieldType.Name));
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldfld, field));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, isNetworkIdentity ? 1 : 0));
                code.Append(Instruction.Create(OpCodes.Call, registerModule));

                var endInstruction = Instruction.Create(OpCodes.Nop);

                // if not null
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldfld, field));
                code.Append(Instruction.Create(OpCodes.Brfalse, endInstruction));

                // call init method
                var initMethodName =
                    $"__{GenerateSerializersProcessor.MakeFullNameValidCSharp(field.FieldType.Name)}_CodeGen_Initialize";
                var codeGenInitRef = new MethodReference(initMethodName, module.TypeSystem.Void, field.FieldType)
                {
                    HasThis = true
                };

                codeGenInitRef.Parameters.Add(parentStr);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldfld, field));
                if (isNetworkIdentity)
                {
                    code.Append(Instruction.Create(OpCodes.Ldstr, field.Name));
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_1));
                    code.Append(Instruction.Create(OpCodes.Ldstr, '.' + field.Name));
                    code.Append(Instruction.Create(OpCodes.Call, concatMethod));
                }

                code.Append(Instruction.Create(OpCodes.Call, codeGenInitRef));
                code.Append(endInstruction);

                if (!isNetworkIdentity)
                {
                    // if null
                    var endInstruction2 = Instruction.Create(OpCodes.Nop);
                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                    code.Append(Instruction.Create(OpCodes.Ldfld, field));
                    code.Append(Instruction.Create(OpCodes.Brtrue, endInstruction2));

                    // call error
                    var errorMethod = module.GetTypeDefinition<NetworkModule>().GetMethod("Error").Import(module);

                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                    code.Append(Instruction.Create(OpCodes.Ldarg_1));
                    code.Append(Instruction.Create(OpCodes.Ldstr, '.' + field.Name));
                    code.Append(Instruction.Create(OpCodes.Call, concatMethod));
                    code.Append(Instruction.Create(OpCodes.Call, errorMethod));

                    code.Append(endInstruction2);
                }
            }

            code.Append(Instruction.Create(OpCodes.Ret));
        }

        private static void FindUsedTypes(ModuleDefinition module, DisposableList<TypeDefinition> allTypes,
            HashSet<TypeReference> types)
        {
            var playersBroadcasterSubscribe = module.GetTypeDefinition<PlayersBroadcaster>();
            var playersManagerSubscribe = module.GetTypeDefinition<PlayersManager>();
            var broadcastModuleSubscribe = module.GetTypeDefinition<BroadcastModule>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();

            for (int i = 0; i < allTypes.Count; i++)
            {
                var type = allTypes[i];

                foreach (var field in type.Fields)
                {
                    var resolved = field.FieldType.Resolve();
                    if (resolved == null) continue;

                    AddAnySyncVarOrGenericNetworkModulesType(types, field.FieldType, resolved, networkModule);
                }

                for (int j = 0; j < type.Methods.Count; j++)
                {
                    var method = type.Methods[j];

                    if (method.Body == null) continue;

                    var body = method.Body;

                    for (int k = 0; k < body.Instructions.Count; k++)
                    {
                        var instruction = body.Instructions[k];

                        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                            continue;

                        if (instruction.Operand is MethodReference normalMethod)
                        {
                            if (IsRpcMethod(normalMethod) && normalMethod.DeclaringType is GenericInstanceType genericInstanceType)
                            {
                                if (IsConcreteType(genericInstanceType, out var gconcreteType))
                                    types.Add(gconcreteType);

                                for (var index = 0; index < genericInstanceType.GenericArguments.Count; index++)
                                {
                                    var ga = genericInstanceType.GenericArguments[index];
                                    if (IsConcreteType(ga, out var concreteType))
                                        types.Add(concreteType);
                                }
                            }
                        }

                        if (instruction.Operand is GenericInstanceMethod currentMethod)
                        {
                            if (IsRpcMethod(currentMethod))
                                FindUsedGenericRpcTypes(types, currentMethod);

                            var isSubscribeMethod =
                                currentMethod.GenericArguments.Count == 1 && currentMethod.Name.Equals("Subscribe") &&
                                (currentMethod.DeclaringType.FullName == playersManagerSubscribe.FullName ||
                                 currentMethod.DeclaringType.FullName == broadcastModuleSubscribe.FullName ||
                                 currentMethod.DeclaringType.FullName == playersBroadcasterSubscribe.FullName);

                            if (isSubscribeMethod &&
                                IsConcreteType(currentMethod.GenericArguments[0], out var concreteType))
                                types.Add(concreteType);
                        }
                    }
                }
            }
        }

        private static void AddAnySyncVarOrGenericNetworkModulesType(HashSet<TypeReference> types, TypeReference type,
            TypeDefinition resolved,
            TypeDefinition networkModule)
        {
            if (networkModule == null)
                return;

            bool inheritsNetworkModule = InheritsFrom(resolved, networkModule.FullName);
            if (inheritsNetworkModule && type is GenericInstanceType genericInstance)
            {
                bool allConcrete = true;

                if (genericInstance.GenericArguments == null)
                    return;

                foreach (var genericArg in genericInstance.GenericArguments)
                {
                    if (genericArg == null)
                        continue;

                    if (IsConcreteType(genericArg, out var concreteType))
                    {
                        types.Add(concreteType);
                    }
                    else allConcrete = false;
                }

                if (allConcrete)
                    types.Add(type);
            }
        }

        private static void FindUsedGenericRpcTypes(HashSet<TypeReference> types, GenericInstanceMethod currentMethod)
        {
            foreach (var argument in currentMethod.GenericArguments)
            {
                if (!argument.IsGenericParameter)
                    types.Add(argument);
            }
        }

        private static bool IsRpcMethod(MethodReference currentMethod)
        {
            try
            {
                var resolved = currentMethod.Resolve();

                if (resolved == null)
                    return false;

                foreach (var attribute in resolved.CustomAttributes)
                {
                    if (attribute.AttributeType.FullName == typeof(ServerRpcAttribute).FullName ||
                        attribute.AttributeType.FullName == typeof(TargetRpcAttribute).FullName ||
                        attribute.AttributeType.FullName == typeof(ObserversRpcAttribute).FullName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsTypeInOwnModule(TypeReference typeReference, ModuleDefinition ownModule)
        {
            if (IsGeneric(typeReference, typeof(Dictionary<,>)))
                return true;

            if (IsGeneric(typeReference, typeof(HashSet<>)))
                return true;

            if (IsGeneric(typeReference, typeof(List<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableList<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableArray<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableHashSet<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableDictionary<,>)))
                return true;

            if (IsGeneric(typeReference, typeof(Queue<>)))
                return true;

            if (IsGeneric(typeReference, typeof(Stack<>)))
                return true;

            if (IsGeneric(typeReference, typeof(Nullable<>)))
                return true;

            if (typeReference is ArrayType)
                return true;

            // Check if the type's module matches our own module
            if (typeReference.Module != ownModule)
                return false;

            // Check if the type is primitive or belongs to the core library (e.g., System, mscorlib)
            if (typeReference.IsPrimitive || typeReference.Scope.Name == "mscorlib" ||
                typeReference.Scope.Name == "System.Private.CoreLib")
                return false;

            // Check if the type is an external reference by comparing the assembly name
            if (typeReference.Scope is AssemblyNameReference assemblyRef &&
                assemblyRef.Name != ownModule.Assembly.Name.Name)
                return false;

            return true;
        }
    }
}
#endif
