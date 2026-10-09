using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point through which the hot-reload tool tells the dynamic-code
    /// tool which compile references to load into the shared compiler worker after a server
    /// reset. The two tools live in sibling assemblies that must not reference each other.
    /// </summary>
    public static class SharedCompilerWarmUpCoordination
    {
        // Set by the hot-reload startup. Lists the compile references the next hot-reload shim
        // compile binds as they are, for the most recently edited assembly that is compiled;
        // empty when there is none. Callable from any thread: it switches to the main thread
        // itself for the Unity API it needs. Null means the hot-reload tool has not initialized
        // in this domain - callers treat null as nothing to warm.
        public static Func<CancellationToken, Task<IReadOnlyList<string>>> CollectWarmUpReferencePaths { get; set; }
    }
}
