using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds the in-flight CLI compile flag that the Harmony prefix reads, and the decline
    /// fact until it is copied onto the request's CompileResult.
    /// </summary>
    internal static class CompileApiUpdaterConsentState
    {
        // Why static: Harmony prefix on EditorUtility.DisplayDialogComplex has no CompileController
        // instance, so the in-flight gate must be reachable from a static prefix.
        private static bool _cliCompileInFlight;
        private static bool _declinedDuringCurrentCompile;

        internal static bool IsCliCompileInFlight => _cliCompileInFlight;

        /// <summary>
        /// Marks a CLI-started compile as in flight so the consent dialog can be declined.
        /// </summary>
        internal static void BeginCliCompile()
        {
            _cliCompileInFlight = true;
            _declinedDuringCurrentCompile = false;
        }

        /// <summary>
        /// Clears the in-flight gate and any unconsumed decline fact.
        /// </summary>
        internal static void EndCliCompile()
        {
            _cliCompileInFlight = false;
            _declinedDuringCurrentCompile = false;
        }

        /// <summary>
        /// Records that the consent dialog was declined at least once for the current compile.
        /// Multiple declines still collapse to a single disclosure.
        /// </summary>
        internal static void MarkDeclined()
        {
            if (!_cliCompileInFlight)
            {
                return;
            }

            _declinedDuringCurrentCompile = true;
        }

        /// <summary>
        /// Moves the decline fact onto <paramref name="result"/> so it is no longer stored only
        /// in this static gate.
        /// </summary>
        internal static CompileResult AttachDeclined(CompileResult result)
        {
            Debug.Assert(result != null, "result must not be null");
            if (!_declinedDuringCurrentCompile)
            {
                return result;
            }

            _declinedDuringCurrentCompile = false;
            return result.WithApiUpdaterConsentDeclined();
        }
    }
}
