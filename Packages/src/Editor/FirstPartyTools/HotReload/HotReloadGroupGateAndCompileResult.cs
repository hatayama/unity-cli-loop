using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports what the signature-change gate and the first shim compile of one group produced,
    /// so the group pipeline can tell a run that must end unapplied from one that still has to
    /// reach the commit boundary, with or without entries to patch.
    /// </summary>
    internal sealed class HotReloadGroupGateAndCompileResult
    {
        private HotReloadGroupGateAndCompileResult(
            HotReloadGroupGateAndCompileOutcome outcome,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gate,
            HotReloadGroupCompileResult compile)
        {
            Outcome = outcome;
            Gate = gate;
            Compile = compile;
        }

        public HotReloadGroupGateAndCompileOutcome Outcome { get; }

        public HotReloadSignatureChangeGate.SignatureChangeGateResult Gate { get; }

        public HotReloadGroupCompileResult Compile { get; }

        /// <summary>
        /// The gate failed the group, or the compile could not produce anything to apply. Both
        /// stages have already routed their own outcomes onto the group's files.
        /// </summary>
        public static HotReloadGroupGateAndCompileResult Failed()
        {
            return new HotReloadGroupGateAndCompileResult(
                HotReloadGroupGateAndCompileOutcome.Failed,
                null,
                null);
        }

        /// <summary>
        /// The group succeeded and simply has no method left to patch. It still reaches the commit
        /// boundary, because a reload whose only change is a new type declaration has nothing to
        /// patch and yet must leave that type active.
        /// </summary>
        public static HotReloadGroupGateAndCompileResult ReadyWithoutEntries(
            HotReloadSignatureChangeGate.SignatureChangeGateResult gate,
            HotReloadGroupCompileResult compile)
        {
            return Create(HotReloadGroupGateAndCompileOutcome.ReadyWithoutEntries, gate, compile);
        }

        public static HotReloadGroupGateAndCompileResult Ready(
            HotReloadSignatureChangeGate.SignatureChangeGateResult gate,
            HotReloadGroupCompileResult compile)
        {
            return Create(HotReloadGroupGateAndCompileOutcome.ReadyWithEntries, gate, compile);
        }

        private static HotReloadGroupGateAndCompileResult Create(
            HotReloadGroupGateAndCompileOutcome outcome,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gate,
            HotReloadGroupCompileResult compile)
        {
            if (gate == null)
            {
                throw new ArgumentNullException(nameof(gate));
            }

            if (compile == null)
            {
                throw new ArgumentNullException(nameof(compile));
            }

            return new HotReloadGroupGateAndCompileResult(outcome, gate, compile);
        }
    }
}
