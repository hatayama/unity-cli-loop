using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines validation operations for Preload Assembly Security data before the workflow continues.
    /// </summary>
    public interface IPreloadAssemblySecurityValidator
    {
        SecurityValidationResult Validate(byte[] assemblyBytes);
    }
}
