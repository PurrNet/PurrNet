#if UNITY_MONO_CECIL
using System.Collections.Generic;
using Mono.Cecil;

namespace PurrNet.Codegen
{
    public static class TypeExtensions
    {
        public static bool IsUnmanagedType(this TypeReference typeRef)
        {
            // TypeSpecification.Resolve() resolves through to its element/type definition.
            // In particular, resolving int[] returns System.Int32, which must not make the
            // array itself eligible for NativePacker<T>/NativeDeltaPacker<T> calli dispatch.
            return typeRef?.IsValueType == true && typeRef.Resolve()?.IsUnmanaged() == true;
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
