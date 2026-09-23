using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the warning that asks to pause Play Mode before wiring added fields, when the
    /// response names fields to wire while Play Mode is running.
    /// </summary>
    internal static class HotReloadPauseBeforeWiringWarning
    {
        /// <summary>
        /// Appends the warning when Play Mode is running unpaused and the response names fields
        /// the caller has to wire.
        /// </summary>
        /// <remarks>
        /// Why paused Play Mode is left out: no frame runs while it is paused, so nothing reads the
        /// fields before the caller wires them. Why the fields gate it: without a field to wire,
        /// the warning would ask for a pause that achieves nothing.
        /// </remarks>
        internal static void Append(
            List<string> warnings,
            bool isPlaying,
            bool isPaused,
            bool namesFieldsToWire)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (!isPlaying || isPaused || !namesFieldsToWire)
            {
                return;
            }

            warnings.Add(HotReloadConstants.PauseBeforeWiringDuringPlayWarning);
        }
    }
}
