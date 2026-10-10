using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identity of a compiled method to search for (assembly + type + name + arity + parameter types).
    /// </summary>
    public readonly struct HotReloadCompiledMethodIdentity
    {
        // Only the analysis behind the entry point reads these; hot-reload code outside it only
        // builds this value and passes it in.
        internal readonly string AssemblyName;
        internal readonly HotReloadMetadataTypeName TypeMetadataName;
        internal readonly string MethodName;
        internal readonly string[] ParameterTypeFullNames;
        internal readonly int GenericArity;

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
