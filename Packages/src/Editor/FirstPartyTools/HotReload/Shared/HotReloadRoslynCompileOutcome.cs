namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What a hot-reload Roslyn compile attempt produced: either the backend's result, or the fact
    /// that the external compiler paths could not be resolved, so nothing was written and nothing
    /// was compiled. Callers phrase the unresolved case in their own failure vocabulary.
    /// </summary>
    internal sealed class HotReloadRoslynCompileOutcome
    {
        /// <summary>False when compiler discovery failed before any source was written.</summary>
        public bool PathsResolved { get; }

        // Why this can be null even when the paths resolved: the backend is behind an interface a
        // test can substitute, and the callers' "the compile produced no result" branch has to keep
        // seeing that case rather than turning it into an exception raised here.
        public DynamicCompilationBackendResult BackendResult { get; }

        private HotReloadRoslynCompileOutcome(
            bool pathsResolved,
            DynamicCompilationBackendResult backendResult)
        {
            PathsResolved = pathsResolved;
            BackendResult = backendResult;
        }

        public static HotReloadRoslynCompileOutcome PathsUnresolved()
        {
            return new HotReloadRoslynCompileOutcome(false, null);
        }

        public static HotReloadRoslynCompileOutcome Compiled(DynamicCompilationBackendResult backendResult)
        {
            return new HotReloadRoslynCompileOutcome(true, backendResult);
        }
    }
}
