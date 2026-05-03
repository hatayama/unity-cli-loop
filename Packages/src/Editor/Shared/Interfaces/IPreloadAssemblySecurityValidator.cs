namespace io.github.hatayama.UnityCliLoop
{
    public interface IPreloadAssemblySecurityValidator
    {
        SecurityValidationResult Validate(byte[] assemblyBytes);
    }
}
