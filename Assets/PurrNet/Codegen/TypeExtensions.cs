#if UNITY_MONO_CECIL
using System.Collections.Generic;
using Mono.Cecil;

namespace PurrNet.Codegen
{
    public static class TypeExtensions
    {
        public static bool IsKnownUnmanaged(this TypeReference type)
        {
            return IsKnownUnmanaged(type, new HashSet<TypeDefinition>(), false);
        }

        private static bool IsKnownUnmanaged(TypeReference type, HashSet<TypeDefinition> activeTypes,
            bool isField)
        {
            if (type == null)
                return false;

            if (type.IsPointer || type.IsFunctionPointer)
                return isField;

            if (type is TypeSpecification || type.IsGenericParameter || type.HasGenericParameters ||
                type.ContainsGenericParameter)
                return false;

            if (type.FullName is "System.TypedReference" or "System.ArgIterator" or "System.RuntimeArgumentHandle")
                return false;

            switch (type.MetadataType)
            {
                case MetadataType.Boolean:
                case MetadataType.Char:
                case MetadataType.SByte:
                case MetadataType.Byte:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                case MetadataType.Int64:
                case MetadataType.UInt64:
                case MetadataType.Single:
                case MetadataType.Double:
                case MetadataType.IntPtr:
                case MetadataType.UIntPtr:
                    return true;
                case MetadataType.Void:
                case MetadataType.TypedByReference:
                    return false;
            }

            TypeDefinition definition;
            try
            {
                definition = type.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }

            if (definition == null || !definition.IsValueType || definition.HasGenericParameters)
                return false;

            foreach (var attribute in definition.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                    return false;
            }

            if (!activeTypes.Add(definition))
                return false;

            try
            {
                foreach (var field in definition.Fields)
                {
                    if (!field.IsStatic && !IsKnownUnmanaged(field.FieldType, activeTypes, true))
                        return false;
                }

                return true;
            }
            finally
            {
                activeTypes.Remove(definition);
            }
        }

        public static bool IsUnmanaged(this TypeDefinition typeDef)
        {
            if (!typeDef.IsValueType)
            {
                return false; // Only value types can be unmanaged
            }

            var visitedTypes = new HashSet<TypeDefinition>();
            return !ContainsReferenceTypes(typeDef, visitedTypes);
        }

        private static bool ContainsReferenceTypes(TypeDefinition typeDef, HashSet<TypeDefinition> visitedTypes)
        {
            if (!visitedTypes.Add(typeDef))
                return false; // Already visited, no reference types found on this path yet

            foreach (var field in typeDef.Fields)
            {
                var fieldType = field.FieldType;

                if (fieldType.IsPointer || fieldType.IsFunctionPointer)
                {
                    continue; // Pointers are unmanaged
                }

                if (fieldType.IsArray || fieldType.IsByReference || fieldType.IsRequiredModifier)
                {
                    return true; // Arrays, byref, and required modifiers are reference-like
                }

                if (fieldType.IsDefinition)
                {
                    var fieldTypeDef = (TypeDefinition)fieldType;
                    if (!fieldTypeDef.IsValueType || ContainsReferenceTypes(fieldTypeDef, visitedTypes))
                    {
                        return true;
                    }
                }
                else if (fieldType.IsGenericInstance)
                {
                    var genericInstance = (GenericInstanceType)fieldType;
                    var genericTypeDef = genericInstance.Resolve();

                    if (genericTypeDef == null)
                    {
                        // Could not resolve the generic type definition, assume it could be managed
                        return true;
                    }

                    if (!genericTypeDef.IsValueType || ContainsReferenceTypes(genericTypeDef, visitedTypes))
                    {
                        return true;
                    }

                    // Also check generic arguments themselves
                    foreach (var genericArgument in genericInstance.GenericArguments)
                    {
                        var genericArgumentTypeDef = genericArgument.Resolve();
                        if (genericArgumentTypeDef == null)
                        {
                            // Could not resolve the generic argument type definition, assume it could be managed
                            return true;
                        }

                        if (!genericArgumentTypeDef.IsValueType || ContainsReferenceTypes(genericArgumentTypeDef, visitedTypes))
                        {
                            return true;
                        }
                    }
                }
                else if (fieldType.IsGenericParameter)
                {
                    // If a generic parameter could potentially be a reference type, consider it managed.
                    // This is a conservative approach.
                    var genericParamResolved = fieldType.Resolve();
                    if (genericParamResolved != null && !genericParamResolved.IsValueType)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
#endif
