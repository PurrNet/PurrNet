#if UNITY_MONO_CECIL

using System;
using Mono.Cecil;

namespace PurrNet.Codegen
{
    public static class ModuleExtensions
    {
        public static TypeReference GetTypeReference(this ModuleDefinition module, Type type)
        {
            switch (type.FullName)
            {
                case "System.Int32":
                    return module.TypeSystem.Int32;
                case "System.Single":
                    return module.TypeSystem.Single;
                case "System.String":
                    return module.TypeSystem.String;
                case "System.Boolean":
                    return module.TypeSystem.Boolean;
                case "System.Void":
                    return module.TypeSystem.Void;
                default:
                    for (var i = 0; i < module.Types.Count; i++)
                    {
                        if (module.Types[i].FullName == type.FullName)
                            return module.Types[i];
                    }

                    return module.ImportReference(type);
            }
        }

        private static TypeReference GetTypeReference<T>(this ModuleDefinition module)
        {
            var type = typeof(T);

            switch (type.FullName)
            {
                case "System.Int32":
                    return module.TypeSystem.Int32;
                case "System.Single":
                    return module.TypeSystem.Single;
                case "System.String":
                    return module.TypeSystem.String;
                case "System.Boolean":
                    return module.TypeSystem.Boolean;
                case "System.Void":
                    return module.TypeSystem.Void;
                default:
                    var fullName = type.FullName;

                    for (var i = 0; i < module.Types.Count; i++)
                    {
                        if (module.Types[i].FullName == fullName)
                            return module.Types[i];
                    }

                    return module.ImportReference(typeof(T));
            }
        }

        public static TypeDefinition GetTypeDefinition<T>(this ModuleDefinition module)
        {
            return GetTypeReference<T>(module).Resolve();
        }

        public static TypeDefinition GetTypeDefinition(this ModuleDefinition module, Type type)
        {
            return GetTypeReference(module, type).Resolve();
        }

        public static TypeReference Import(this ModuleDefinition module, TypeReference member)
        {
            return module.ImportReference(member);
        }

        public static MethodReference Import(this ModuleDefinition module, MethodReference member)
        {
            return module.ImportReference(member);
        }

        public static FieldReference Import(this ModuleDefinition module, FieldReference member)
        {
            return module.ImportReference(member);
        }

        public static FieldReference Import(this FieldReference member, ModuleDefinition module)
        {
            return Import(module, member);
        }

        public static MethodReference Import(this MethodReference member, ModuleDefinition module)
        {
            return Import(module, member);
        }

        public static TypeReference Import(this TypeReference member, ModuleDefinition module)
        {
            return Import(module, member);
        }

        public static MethodDefinition GetMethod(this TypeReference type, string name, bool isGeneric = false)
        {
            return GetMethod(type.Resolve(), name, isGeneric);
        }

        public static MethodDefinition TryGetMethodWihtStatic(this TypeReference type, string name, bool isGeneric = false, bool isStatic = false)
        {
            return TryGetMethodWihtStatic(type.Resolve(), name, isGeneric, isStatic);
        }

        public static FieldDefinition GetField(this TypeDefinition type, string name)
        {
            for (var i = 0; i < type.Fields.Count; i++)
            {
                if (type.Fields[i].Name == name)
                    return type.Fields[i];
            }

            throw new Exception($"Field {name} not found on type {type.FullName}");
        }

        public static FieldDefinition GetField(this TypeReference type, string name)
        {
            return GetField(type.Resolve(), name);
        }

        public static MethodDefinition TryGetMethodWihtStatic(this TypeDefinition type, string name, bool isGeneric = false, bool isStatic = false)
        {
            for (var i = 0; i < type.Methods.Count; i++)
            {
                if (type.Methods[i].Name == name && type.Methods[i].HasGenericParameters == isGeneric && type.Methods[i].IsStatic == isStatic)
                    return type.Methods[i];
            }

            return null;
        }

        public static bool TryGetMethod(this TypeDefinition type, string name, bool isGeneric, out MethodDefinition result)
        {
            for (var i = 0; i < type.Methods.Count; i++)
            {
                if (type.Methods[i].Name == name && type.Methods[i].HasGenericParameters == isGeneric)
                {
                    result = type.Methods[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        public static MethodDefinition GetMethod(this TypeDefinition type, string name, bool isGeneric = false)
        {
            for (var i = 0; i < type.Methods.Count; i++)
            {
                if (type.Methods[i].Name == name && type.Methods[i].HasGenericParameters == isGeneric)
                    return type.Methods[i];
            }

            throw new Exception($"Method {name} not found on type {type.FullName}");
        }

        public static MethodReference GetMethodRef(this TypeReference typeRef, string name, bool isGeneric = false)
        {
            var resolved = typeRef.Resolve();
            if (resolved == null)
                throw new InvalidOperationException("Unable to resolve type: " + typeRef.FullName);

            MethodDefinition methodDef = null;
            for (int i = 0; i < resolved.Methods.Count; i++)
            {
                var m = resolved.Methods[i];
                if (m.Name == name && m.HasGenericParameters == isGeneric)
                {
                    methodDef = m;
                    break;
                }
            }

            if (methodDef == null)
                throw new Exception("Method " + name + " not found on type " + resolved.FullName);

            var imported = typeRef.Module.ImportReference(methodDef);

            if (typeRef is GenericInstanceType git)
            {
                var genericMethod = new MethodReference(imported.Name, imported.ReturnType, git)
                {
                    HasThis = imported.HasThis,
                    ExplicitThis = imported.ExplicitThis,
                    CallingConvention = imported.CallingConvention
                };

                for (int i = 0; i < imported.Parameters.Count; i++)
                {
                    var p = imported.Parameters[i];
                    genericMethod.Parameters.Add(new ParameterDefinition(p.ParameterType));
                }

                for (int i = 0; i < imported.GenericParameters.Count; i++)
                {
                    var gp = imported.GenericParameters[i];
                    genericMethod.GenericParameters.Add(new GenericParameter(gp.Name, genericMethod));
                }

                return genericMethod;
            }

            return imported;
        }

        public static MethodDefinition GetMethod(this TypeDefinition type, string name, int paramCount, bool isGeneric = false)
        {
            for (var i = 0; i < type.Methods.Count; i++)
            {
                if (type.Methods[i].Name == name && type.Methods[i].HasGenericParameters == isGeneric)
                {
                    var method = type.Methods[i];

                    if (method.Parameters.Count == paramCount)
                        return method;
                }
            }

            throw new Exception($"Method {name} not found on type {type.FullName}");
        }

        public static MethodDefinition GetMethod(this TypeDefinition type, string name, TypeReference a,
            TypeReference b, bool isGeneric = false)
        {
            for (var i = 0; i < type.Methods.Count; i++)
            {
                if (type.Methods[i].Name == name && type.Methods[i].HasGenericParameters == isGeneric)
                {
                    var method = type.Methods[i];

                    if (method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == a.FullName &&
                        method.Parameters[1].ParameterType.FullName == b.FullName)
                        return method;
                }
            }

            throw new Exception($"Method {name} not found on type {type.FullName}");
        }

        public static PropertyDefinition GetProperty(this TypeReference type, string name)
        {
            return GetProperty(type.Resolve(), name);
        }

        public static PropertyDefinition GetProperty(this TypeDefinition type, string name)
        {
            for (var i = 0; i < type.Properties.Count; i++)
            {
                if (type.Properties[i].Name == name)
                {
                    return type.Properties[i];
                }
            }

            throw new Exception($"Property {name} not found on type {type.FullName}");
        }
    }
}

#endif
