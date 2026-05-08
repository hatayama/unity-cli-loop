namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Security levels are a project policy, so infrastructure stores only their values.
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
