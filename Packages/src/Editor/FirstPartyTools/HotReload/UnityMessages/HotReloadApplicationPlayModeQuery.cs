using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>Answers the play-mode question from the running editor.</summary>
    internal sealed class HotReloadApplicationPlayModeQuery : IHotReloadPlayModeQuery
    {
        public bool IsPlaying => Application.isPlaying;
    }
}
