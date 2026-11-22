using System;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides whether a compile's domain reload would discard Editor state the compile Warning
    /// reports: the Play session, enabled pause points (persisted ones are dropped and re-armed),
    /// and active hot-reload changes. A compile with that state must reload even when no script changed.
    /// </summary>
    internal static class CompileReloadDiscardState
    {
        internal static bool Evaluate(bool isPlaying, int activePausePointCount, int activeHotReloadChangeCount)
        {
            return isPlaying || activePausePointCount > 0 || activeHotReloadChangeCount > 0;
        }

        internal static bool ReadLive()
        {
            Func<int> getHotReloadChangeCount = HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount;
            int activeHotReloadChangeCount = getHotReloadChangeCount == null ? 0 : getHotReloadChangeCount();
            return Evaluate(
                EditorApplication.isPlaying,
                UloopPausePointRegistry.GetActiveCount(),
                activeHotReloadChangeCount);
        }
    }
}
