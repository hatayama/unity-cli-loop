namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Frame data pre-extracted from System.Diagnostics.StackFrame so the caller frame
    /// selection logic can run on plain values in unit tests.
    /// </summary>
    internal readonly struct SourcePausePointRawStackFrame
    {
        public SourcePausePointRawStackFrame(string typeFullName, string methodName, string fileName, int line)
        {
            TypeFullName = typeFullName;
            MethodName = methodName;
            FileName = fileName;
            Line = line;
        }

        // Null for dynamic methods (no declaring type), e.g. Harmony-generated bodies.
        public string TypeFullName { get; }

        public string MethodName { get; }

        // Null when debug symbols are unavailable for the frame.
        public string FileName { get; }

        public int Line { get; }
    }
}
