using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports what the signature-change gate and the first shim compile of one group produced,
    /// so the group pipeline can tell a run that has entries to apply from one that must end
    /// unapplied without repeating either stage's routing.
    /// </summary>
    internal sealed class HotReloadGroupGateAndCompileResult
    {
        private HotReloadGroupGateAndCompileResult(
            bool hasEntriesToApply,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gate,
            HotReloadGroupCompileResult compile)
        {
            HasEntriesToApply = hasEntriesToApply;
            Gate = gate;
            Compile = compile;
        }

        public bool HasEntriesToApply { get; }

        public HotReloadSignatureChangeGate.SignatureChangeGateResult Gate { get; }

        public HotReloadGroupCompileResult Compile { get; }

        /// <summary>
        /// The gate failed the group, or the compile left nothing to apply. Both stages have
        /// already routed their own outcomes onto the group's files.
        /// </summary>
        public static HotReloadGroupGateAndCompileResult NothingToApply()
        {
            return new HotReloadGroupGateAndCompileResult(false, null, null);
        }

        public static HotReloadGroupGateAndCompileResult Ready(
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

            return new HotReloadGroupGateAndCompileResult(true, gate, compile);
        }
    }
}
