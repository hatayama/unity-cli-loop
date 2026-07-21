using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Mono.Cecil;
using Mono.Cecil.Cil;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects whether a patch target method is called (one level deep) from any of Unity's
    /// physics message methods elsewhere in the same compiled assembly, so BuildPatchWarning can
    /// surface the same cached-dispatch risk documented for a directly-named physical message
    /// method (see PhysicalCallbackMayMissExistingInstanceWarning) even when the patched method is
    /// only a helper such a callback calls into.
    /// </summary>
    internal static class SourcePausePointPhysicalCallbackCallSiteScanner
    {
        private const string MonoBehaviourFullName = "UnityEngine.MonoBehaviour";

        public static bool IsCalledFromPhysicalMessageMethod(MethodBase method)
        {
            // Re-deriving the dll path from the assembly name (the same way SourcePausePointResolver
            // locates a compiled assembly) rather than reading Assembly.Location: Unity's domain-reload
            // load path does not guarantee Location is populated, but the compiled-output layout is fixed.
            string assemblyName = method.Module.Assembly.GetName().Name;
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string dllPath = Path.Combine(
                projectRoot,
                SourcePausePointConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + SourcePausePointConstants.CompiledAssemblyExtension);

            if (!File.Exists(dllPath))
            {
                return false;
            }

            using FileStream assemblyStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true, ReadSymbols = false };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyStream, readerParameters);

            string declaringTypeFullName = method.DeclaringType.FullName;
            string methodName = method.Name;
            string[] parameterTypeFullNames = method.GetParameters()
                .Select(parameter => parameter.ParameterType.FullName)
                .ToArray();

            foreach (TypeDefinition type in EnumerateTypes(assemblyDefinition.MainModule))
            {
                if (!DerivesFromMonoBehaviour(type))
                {
                    continue;
                }

                foreach (MethodDefinition candidateCaller in type.Methods)
                {
                    if (!candidateCaller.HasBody ||
                        !SourcePausePointPhysicalMessageMethods.IsPhysicalMessageMethod(candidateCaller.Name))
                    {
                        continue;
                    }

                    if (CallsTarget(candidateCaller, declaringTypeFullName, methodName, parameterTypeFullNames))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CallsTarget(
            MethodDefinition caller, string declaringTypeFullName, string methodName, string[] parameterTypeFullNames)
        {
            foreach (Instruction instruction in caller.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference calleeReference &&
                    IsSameMethod(calleeReference, declaringTypeFullName, methodName, parameterTypeFullNames))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameMethod(
            MethodReference candidate, string declaringTypeFullName, string methodName, string[] parameterTypeFullNames)
        {
            if (candidate.Name != methodName)
            {
                return false;
            }

            // Cecil renders nested-type names with '/' between the outer and inner type, while
            // reflection's Type.FullName uses '+'; normalize before comparing the two worlds.
            if (candidate.DeclaringType.FullName.Replace('/', '+') != declaringTypeFullName)
            {
                return false;
            }

            if (candidate.Parameters.Count != parameterTypeFullNames.Length)
            {
                return false;
            }

            for (int i = 0; i < parameterTypeFullNames.Length; i++)
            {
                if (candidate.Parameters[i].ParameterType.FullName != parameterTypeFullNames[i])
                {
                    return false;
                }
            }

            return true;
        }

        // Only walks base types Cecil already resolved within the same module (nested classes of a
        // custom base class in this assembly); an external non-MonoBehaviour base (e.g. a plain
        // framework type) stops the walk rather than forcing cross-assembly resolution, mirroring
        // SourcePausePointResolver.IsRefStructType's stance on the same tradeoff.
        private static bool DerivesFromMonoBehaviour(TypeDefinition type)
        {
            TypeReference current = type.BaseType;
            while (current != null)
            {
                if (current.FullName == MonoBehaviourFullName)
                {
                    return true;
                }

                if (current is not TypeDefinition definition)
                {
                    return false;
                }

                current = definition.BaseType;
            }

            return false;
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(ModuleDefinition module)
        {
            foreach (TypeDefinition type in module.Types)
            {
                foreach (TypeDefinition nested in EnumerateTypesRecursive(type))
                {
                    yield return nested;
                }
            }
        }

        private static IEnumerable<TypeDefinition> EnumerateTypesRecursive(TypeDefinition type)
        {
            yield return type;
            foreach (TypeDefinition nestedType in type.NestedTypes)
            {
                foreach (TypeDefinition nested in EnumerateTypesRecursive(nestedType))
                {
                    yield return nested;
                }
            }
        }
    }
}
