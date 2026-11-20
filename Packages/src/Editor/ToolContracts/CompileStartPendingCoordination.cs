using System;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point through which the compile tool tells the compile status
    /// report how long the active compile request has been accepted by Unity without starting.
    /// The compile tool lives in a sibling assembly the server must not reference, so it publishes
    /// the reader through a delegate while a request is in flight.
    /// </summary>
    public static class CompileStartPendingCoordination
    {
        // Set by the compile tool for the duration of one compile request and cleared when the
        // request completes. Null means no compile request is in flight in this domain; callers
        // treat null as zero seconds. The value only grows while Unity reports isCompiling without
        // having raised compilationStarted, and only while the Editor main thread keeps ticking.
        public static Func<double> ReadPendingSeconds { get; set; }
    }
}
