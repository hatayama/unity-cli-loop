
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Represents a compilation error during code compilation
    /// 
    /// Related Classes: RoslynCompiler, CompilationResult
    /// </summary>
    public class CompilationError
    {
        /// <summary>Line number where the compilation error occurred</summary>
        public int Line { get; set; }

        /// <summary>Column number where the compilation error occurred</summary>
        public int Column { get; set; }

        /// <summary>Detailed error message describing the compilation error</summary>
        public string Message { get; set; }

        /// <summary>Unique error code identifying the specific compilation error</summary>
        public string ErrorCode { get; set; }
    }
}