using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Keeps the domain reload measurements in SessionState, which survives domain reloads and is cleared when the
    /// Editor quits, so a restarted Editor measures its baseline again.
    /// </summary>
    internal sealed class SessionStateDomainReloadDurationStore : IDomainReloadDurationStore
    {
        // Why a versioned key: a package update reloads the domain mid-session, and new code must not read
        // measurements an older layout wrote.
        private const string StateKey = UnityCliLoopEditorSessionStateStorage.KeyPrefix + "domainReloadDuration.v2";

        public DomainReloadDurationState Load()
        {
            string json = UnityCliLoopEditorSessionStateStorage.GetString(StateKey);
            if (string.IsNullOrEmpty(json))
            {
                return DomainReloadDurationState.Empty;
            }

            return JsonConvert.DeserializeObject<DomainReloadDurationState>(json);
        }

        public void Save(DomainReloadDurationState state)
        {
            UnityEngine.Debug.Assert(state != null, "state must not be null");

            UnityCliLoopEditorSessionStateStorage.SetString(StateKey, JsonConvert.SerializeObject(state));
        }
    }
}
