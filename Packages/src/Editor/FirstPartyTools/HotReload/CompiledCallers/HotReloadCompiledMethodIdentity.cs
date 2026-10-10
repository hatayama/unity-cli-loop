using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identity of a compiled method to search for (assembly + type + name + arity + parameter types).
    /// </summary>
    public readonly struct HotReloadCompiledMethodIdentity
    {
        public readonly string AssemblyName;
        public readonly HotReloadMetadataTypeName TypeMetadataName;
        public readonly string MethodName;
        public readonly string[] ParameterTypeFullNames;
        public readonly int GenericArity;

        public HotReloadCompiledMethodIdentity(
            string assemblyName,
            HotReloadMetadataTypeName typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
            Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");
            Debug.Assert(genericArity >= 0, "genericArity must not be negative.");

            AssemblyName = assemblyName;
            TypeMetadataName = typeMetadataName;
            MethodName = methodName;
            ParameterTypeFullNames = parameterTypeFullNames;
            GenericArity = genericArity;
        }
    }
}
