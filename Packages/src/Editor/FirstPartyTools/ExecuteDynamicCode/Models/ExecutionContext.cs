using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Execution Context
    ///
    /// Related Class: CommandRunner
    /// </summary>
    public class ExecutionContext
    {
        /// <summary>Compiled Assembly</summary>
        public Assembly CompiledAssembly { get; set; }

        /// <summary>Parameters</summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>Cancellation Token</summary>
        public CancellationToken CancellationToken { get; set; }
    }
}