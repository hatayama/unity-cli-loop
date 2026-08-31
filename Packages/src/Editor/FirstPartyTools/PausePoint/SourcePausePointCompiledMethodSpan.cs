using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Inclusive compiled-source line range of one method that matches a --method filter.
    /// </summary>
    internal sealed class SourcePausePointCompiledMethodSpan
    {
        public int StartLine { get; }
        public int EndLine { get; }

        public SourcePausePointCompiledMethodSpan(int startLine, int endLine)
        {
            Debug.Assert(startLine > 0, "startLine must be a positive 1-based line number.");
            Debug.Assert(endLine >= startLine, "endLine must be on or after startLine.");
            StartLine = startLine;
            EndLine = endLine;
        }
    }
}
