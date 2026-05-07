namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Security level definitions for ExecuteDynamicCodeTool
    /// Related classes: DynamicCodeSecurityManager, AssemblyReferencePolicy, RoslynCompiler
    /// </summary>
    public enum DynamicCodeSecurityLevel
    {
        /// <summary>
        /// Dangerous APIs (System.IO, System.Net.Http, Process, reflection, etc.) are blocked.
        /// Default level for safe Unity development.
        /// </summary>
        Restricted = 1,

        /// <summary>
        /// All APIs available without restrictions.
        /// Warning: Security risks present - use only with trusted code.
        /// </summary>
        FullAccess = 2
    }
}