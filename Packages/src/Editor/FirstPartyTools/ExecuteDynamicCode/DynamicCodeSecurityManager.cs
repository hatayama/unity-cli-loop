using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Security management utility for ExecuteDynamicCodeTool
    /// Related Classes: DynamicCodeSecurityLevel, AssemblyReferencePolicy, RoslynCompiler
    /// </summary>
    public static class DynamicCodeSecurityManager
    {
        /// <summary>
        /// Check if code execution is possible at the specified security level
        /// </summary>
        public static bool CanExecute(DynamicCodeSecurityLevel level)
        {
            switch (level)
            {
                case DynamicCodeSecurityLevel.Restricted:
                case DynamicCodeSecurityLevel.FullAccess:
                    // Level 1, 2: Execution permitted
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Retrieve list of allowed assemblies based on the security level
        /// </summary>
        public static IReadOnlyList<string> GetAllowedAssemblies(DynamicCodeSecurityLevel level)
        {
            return AssemblyReferencePolicy.GetAssemblies(level);
        }
    }
}