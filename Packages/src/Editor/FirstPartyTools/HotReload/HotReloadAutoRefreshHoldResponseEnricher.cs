using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Appends the fixed Auto Refresh hold sentences onto apply and revert responses.
    /// </summary>
    internal static class HotReloadAutoRefreshHoldResponseEnricher
    {
        internal static string AppendNewlyArmedMessage(string message, bool newlyArmed)
        {
            Debug.Assert(message != null, "message must not be null");
            if (!newlyArmed)
            {
                return message;
            }

            return message + " " + HotReloadAutoRefreshHoldConstants.NewlyArmedMessageSuffix;
        }

        internal static void AppendDeferredWarning(List<string> warnings, bool releaseDeferred)
        {
            Debug.Assert(warnings != null, "warnings must not be null");
            if (!releaseDeferred)
            {
                return;
            }

            warnings.Add(HotReloadAutoRefreshHoldConstants.ReleaseDeferredWarning);
        }

        internal static void AppendSceneRefreshWarning(List<string> warnings, string sceneRefreshWarning)
        {
            Debug.Assert(warnings != null, "warnings must not be null");
            if (string.IsNullOrEmpty(sceneRefreshWarning))
            {
                return;
            }

            warnings.Add(sceneRefreshWarning);
        }
    }
}
