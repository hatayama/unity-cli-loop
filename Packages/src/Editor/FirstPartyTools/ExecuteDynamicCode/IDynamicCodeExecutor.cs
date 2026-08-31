using System;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Interface for dynamic code execution integration functionality.
    /// Related classes: DynamicCodeExecutor, DynamicCodeCompiler, CommandRunner
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
    }
}
