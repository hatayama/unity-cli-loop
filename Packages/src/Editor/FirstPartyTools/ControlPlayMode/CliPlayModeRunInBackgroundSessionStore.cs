#nullable enable
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Persists CLI PlayMode runInBackground override state in SessionState so domain reload cannot lose it.
    /// </summary>
    internal sealed class CliPlayModeRunInBackgroundSessionStore : ICliPlayModeRunInBackgroundStore
    {
        // Why: domain reload clears static fields; SessionState is the Editor-local store that survives it.
        private const string ActiveKey =
            "io.github.hatayama.uloopmcp.cliPlayModeRunInBackground.active";
        private const string OriginalKey =
            "io.github.hatayama.uloopmcp.cliPlayModeRunInBackground.original";

        public bool IsActive => SessionState.GetBool(ActiveKey, false);

        public bool OriginalRunInBackground => SessionState.GetBool(OriginalKey, false);

        public void Activate(bool originalRunInBackground)
        {
            SessionState.SetBool(OriginalKey, originalRunInBackground);
            SessionState.SetBool(ActiveKey, true);
        }

        public void Clear()
        {
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(OriginalKey, false);
        }
    }
}
