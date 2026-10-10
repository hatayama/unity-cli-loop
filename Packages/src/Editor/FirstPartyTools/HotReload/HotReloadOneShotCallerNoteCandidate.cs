namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A patched or added method whose outcome may get a one-shot lifecycle note, with the
    /// compiled identity its callers are searched by.
    /// </summary>
    internal sealed class HotReloadOneShotCallerNoteCandidate
    {
        public HotReloadCompiledMethodIdentity Identity;
        public HotReloadMethodOutcome Outcome;

        public HotReloadOneShotCallerNoteCandidate(
            HotReloadCompiledMethodIdentity identity,
            HotReloadMethodOutcome outcome)
        {
            Identity = identity;
            Outcome = outcome;
        }
    }
}
