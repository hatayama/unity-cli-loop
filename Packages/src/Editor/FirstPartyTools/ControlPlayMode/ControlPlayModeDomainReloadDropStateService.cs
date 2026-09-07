using System;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Supplies Play-start domain-reload drop counts and Enter Play Mode Options
    /// for ControlPlayModeUseCase warning construction.
    /// </summary>
    public interface IControlPlayModeDomainReloadDropStateProvider
    {
        /// <summary>
        /// How many hot-reload changes the Play-start domain reload discards: patched methods,
        /// added members, and the types a reload introduced.
        /// </summary>
        int GetActiveHotReloadPatchCount();
        int GetActivePausePointCount();
        bool IsDomainReloadDisabledOnEnterPlayMode();
    }

    /// <summary>
    /// Reads the live hot-reload runtime-change count, the armed pause-point count, and the
    /// Enter Play Mode Domain Reload options for the Play-start drop warning.
    /// </summary>
    internal sealed class ControlPlayModeDomainReloadDropStateService : IControlPlayModeDomainReloadDropStateProvider
    {
        public int GetActiveHotReloadPatchCount()
        {
            Func<int> getter = HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount;
            return getter?.Invoke() ?? 0;
        }

        public int GetActivePausePointCount()
        {
            return UloopPausePointRegistry.GetActiveCount();
        }

        public bool IsDomainReloadDisabledOnEnterPlayMode()
        {
            // Why duplicate this check: the PausePoint assembly is a sibling asmdef and cannot
            // be referenced, so ControlPlayMode keeps the same EditorSettings condition here.
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return false;
            }

            return (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        }
    }
}
