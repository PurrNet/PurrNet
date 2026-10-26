#if UNITY_MONO_CECIL
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packing;
using UnityEngine.Scripting;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen
{
    public static class GenerateRegistrationIndex
    {
        public static bool Generate(AssemblyDefinition assembly)
        {
            var module = assembly.MainModule;
            var runtimeAssembly = typeof(RegisterPackersAttribute).Assembly.GetName().Name;
            if (assembly.Name.Name != runtimeAssembly &&
                !assembly.Modules.Any(m => m.AssemblyReferences.Any(r => r.Name == runtimeAssembly)))
                return false;

            var existing = module.GetType(PackerRegistrationEntry.IndexTypeName);
            if (existing != null)
            {
                if (!existing.CustomAttributes.Any(a => a.AttributeType.FullName == typeof(GeneratedByILAttribute).FullName))
                    throw new InvalidOperationException("Reserved PurrNet registration index type already exists.");
                module.Types.Remove(existing);
            }

            var registrations = assembly.Modules.SelectMany(m => m.GetTypes())
                .Where(t => t.IsAbstract && t.IsSealed)
                .SelectMany(t => t.Methods).Where(m => m.IsStatic)
                .SelectMany(m => m.CustomAttributes.Select(a => (method: m, attribute: a)))
                .ToArray();

            // A derived attribute can calculate its priority in arbitrary constructor
            // code. Preserve the existing reflection path for such an assembly.
            if (registrations.Any(r => r.attribute.AttributeType.FullName != typeof(RegisterPackersAttribute).FullName &&
                RequiresReflection(r.attribute.AttributeType)))
                return false;

            var entries = registrations.Where(r => r.attribute.AttributeType.FullName == typeof(RegisterPackersAttribute).FullName)
                .ToArray();
            var split = PackerRegistrationEntry.IndexTypeName.LastIndexOf('.');
            var index = new TypeDefinition(PackerRegistrationEntry.IndexTypeName.Substring(0, split),
                PackerRegistrationEntry.IndexTypeName.Substring(split + 1),
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);
            index.CustomAttributes.Add(new CustomAttribute(module.ImportReference(
                typeof(GeneratedByILAttribute).GetConstructor(Type.EmptyTypes))));
            var entryType = module.ImportReference(typeof(PackerRegistrationEntry));
            var get = new MethodDefinition(PackerRegistrationEntry.IndexMethodName,
                MethodAttributes.Public | MethodAttributes.Static, new ArrayType(entryType));
            get.CustomAttributes.Add(new CustomAttribute(module.ImportReference(
                typeof(PreserveAttribute).GetConstructor(Type.EmptyTypes))));
            index.Methods.Add(get);
            module.Types.Add(index);

            var constructor = module.ImportReference(typeof(PackerRegistrationEntry).GetConstructor(
                new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle), typeof(int) }));
            var il = get.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, entries.Length);
            il.Emit(OpCodes.Newarr, entryType);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                int priority = entry.attribute.ConstructorArguments.Count == 0 ? 0 :
                    (int)entry.attribute.ConstructorArguments[0].Value;
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldtoken, module.ImportReference(entry.method));
                il.Emit(OpCodes.Ldtoken, module.ImportReference(entry.method.DeclaringType));
                il.Emit(OpCodes.Ldc_I4, priority);
                il.Emit(OpCodes.Newobj, constructor);
                il.Emit(OpCodes.Stelem_Any, entryType);
            }
            il.Emit(OpCodes.Ret);
            return true;
        }

        private static bool RequiresReflection(TypeReference attribute)
        {
            try
            {
                for (var current = attribute; current != null; current = current.Resolve()?.BaseType)
                {
                    if (current.FullName == typeof(RegisterPackersAttribute).FullName)
                        return true;
                    if (current.FullName == typeof(Attribute).FullName || current.FullName == typeof(object).FullName)
                        return false;
                }
                return true;
            }
            catch
            {
                // An optional dependency may be unavailable to the postprocessor.
                return true;
            }
        }
    }
}
#endif
