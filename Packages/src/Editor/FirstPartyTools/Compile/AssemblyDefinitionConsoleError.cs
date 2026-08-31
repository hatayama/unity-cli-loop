namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Immutable Console error snapshot for one Assembly Definition or Assembly Reference issue.
    /// </summary>
    public sealed class AssemblyDefinitionConsoleError
    {
        public string Message { get; }
        public string File { get; }
        public int Line { get; }

        public AssemblyDefinitionConsoleError(string message, string file, int line)
        {
            Message = message ?? string.Empty;
            File = file ?? string.Empty;
            Line = line;
        }
    }
}
