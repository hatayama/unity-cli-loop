namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compiler error text paired with the script path it was reported from, without depending on CompilerMessage.
    /// </summary>
    internal sealed class CompileErrorOrigin
    {
        internal string Message { get; }
        internal string File { get; }

        internal CompileErrorOrigin(string message, string file)
        {
            Message = message ?? string.Empty;
            File = file ?? string.Empty;
        }
    }
}
