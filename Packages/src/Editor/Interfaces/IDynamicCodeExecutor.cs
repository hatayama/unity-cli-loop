using System;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Interface for dynamic code execution integration functionality

    /// Related classes: DynamicCodeExecutor, RoslynCompiler, SecurityValidator, CommandRunner
    /// </summary>
    public interface IDynamicCodeExecutor : IDisposable
    {
        /// <summary>Asynchronous code execution</summary>
        System.Threading.Tasks.Task<ExecutionResult> ExecuteCodeAsync(
            string code,
            string className = DynamicCodeConstants.DEFAULT_CLASS_NAME, 
            object[] parameters = null,
            CancellationToken cancellationToken = default,
            bool compileOnly = false
        );



        ExecutionStatistics GetStatistics();
    }

    /// <summary>Execution statistics</summary>
    public class ExecutionStatistics
    {
        /// <summary>Total execution count</summary>
        public int TotalExecutions { get; set; }

        /// <summary>Successful execution count</summary>
        public int SuccessfulExecutions { get; set; }

        /// <summary>Failed execution count</summary>
        public int FailedExecutions { get; set; }

        /// <summary>Average execution time</summary>
        public TimeSpan AverageExecutionTime { get; set; }

        /// <summary>Security violation count</summary>
        public int SecurityViolations { get; set; }

        /// <summary>Compilation error count</summary>
        public int CompilationErrors { get; set; }
    }
}
