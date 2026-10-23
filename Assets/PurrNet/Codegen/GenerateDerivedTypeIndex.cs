#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;
using UnityEngine.Scripting;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen
{
    public static class GenerateDerivedTypeIndex
    {
        public static bool Generate(AssemblyDefinition assembly, Type root, string indexTypeName)
        {
            var module = assembly.MainModule;
            var existing = module.GetType(indexTypeName);
            if (existing != null)
            {
                if (!existing.CustomAttributes.Any(a => a.AttributeType.FullName == typeof(GeneratedByILAttribute).FullName))
                    throw new InvalidOperationException("Reserved PurrNet derived type index already exists.");
                module.Types.Remove(existing);
            }

            var entries = new List<TypeDefinition>();
            foreach (var type in assembly.Modules.SelectMany(m => m.GetTypes()))
            {
                if (!type.IsClass || type.IsValueType || type.IsAbstract) continue;
                bool matches;
                try { matches = DerivesFrom(type, root); }
                catch { return false; } // An unresolved base may belong to this family.
                if (matches) entries.Add(type);
            }

            int split = indexTypeName.LastIndexOf('.');
            var index = new TypeDefinition(indexTypeName.Substring(0, split), indexTypeName.Substring(split + 1),
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);
            index.CustomAttributes.Add(new CustomAttribute(module.ImportReference(
                typeof(GeneratedByILAttribute).GetConstructor(Type.EmptyTypes))));
            var typeReference = module.ImportReference(typeof(Type));
            var get = new MethodDefinition("GetTypes", MethodAttributes.Public | MethodAttributes.Static,
                new ArrayType(typeReference));
            get.CustomAttributes.Add(new CustomAttribute(module.ImportReference(
                typeof(PreserveAttribute).GetConstructor(Type.EmptyTypes))));
            index.Methods.Add(get);
            module.Types.Add(index);

            var fromHandle = module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle)));
            var il = get.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, entries.Count);
            il.Emit(OpCodes.Newarr, typeReference);
            for (int i = 0; i < entries.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldtoken, module.ImportReference(entries[i]));
                il.Emit(OpCodes.Call, fromHandle);
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Ret);
            return true;
        }

        private static bool DerivesFrom(TypeDefinition type, Type root)
        {
            var visited = new HashSet<TypeDefinition>();
            for (var current = type.BaseType; current != null;)
            {
                var definition = current.Resolve();
                if (definition == null || !visited.Add(definition))
                    throw new InvalidOperationException("Incomplete base type hierarchy.");
                if (definition.FullName.Replace('/', '+') == root.FullName &&
                    definition.Module.Assembly.Name.Name == root.Assembly.GetName().Name)
                    return true;
                current = definition.BaseType;
            }
            return false;
        }
    }
}
#endif
