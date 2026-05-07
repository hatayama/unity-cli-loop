using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Host capability that lets a bundled tool request Unity project compilation.
    /// </summary>
    public interface IUnityCliLoopCompilationService
    {
        Task<UnityCliLoopCompileResult> CompileAsync(UnityCliLoopCompileRequest request, CancellationToken ct);
    }

    /// <summary>
    /// Compilation request snapshot shared across the tool boundary.
    /// </summary>
    public sealed class UnityCliLoopCompileRequest
    {
        public bool ForceRecompile { get; set; }
        public bool WaitForDomainReload { get; set; }
        public string RequestId { get; set; } = "";
    }

    /// <summary>
    /// Compilation diagnostic snapshot shared across the tool boundary.
    /// </summary>
    public sealed class UnityCliLoopCompileIssue
    {
        public string Message { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
    }

    /// <summary>
    /// Compilation result snapshot shared across the tool boundary.
    /// </summary>
    public sealed class UnityCliLoopCompileResult
    {
        public bool? Success { get; set; }
        public int? ErrorCount { get; set; }
        public int? WarningCount { get; set; }
        public UnityCliLoopCompileIssue[] Errors { get; set; }
        public UnityCliLoopCompileIssue[] Warnings { get; set; }
        public string Message { get; set; }
        public string ProjectRoot { get; set; }
    }
}
