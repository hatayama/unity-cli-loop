using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Describes the setup action required for the global uloop command.
    /// </summary>
    internal readonly struct CliSetupCompatibilityState
    {
        public CliSetupCompatibilityState(bool needsUpdate, bool isCompatible)
        {
            NeedsUpdate = needsUpdate;
            IsCompatible = isCompatible;
        }

        public bool NeedsUpdate { get; }
        public bool IsCompatible { get; }
    }

    /// <summary>
    /// Evaluates setup compatibility for the global uloop dispatcher.
    /// </summary>
    internal static class CliSetupCompatibility
    {
        public static CliSetupCompatibilityState Evaluate(
            string cliVersion,
            bool isDispatcher,
            string minimumRequiredDispatcherVersion)
        {
            Debug.Assert(!string.IsNullOrEmpty(minimumRequiredDispatcherVersion), "minimumRequiredDispatcherVersion must not be null or empty");

            if (string.IsNullOrEmpty(cliVersion))
            {
                return new CliSetupCompatibilityState(false, false);
            }

            if (!isDispatcher)
            {
                return new CliSetupCompatibilityState(true, false);
            }

            bool isMinimumSatisfied = CliVersionComparer.IsVersionGreaterThanOrEqual(
                cliVersion,
                minimumRequiredDispatcherVersion);
            return new CliSetupCompatibilityState(!isMinimumSatisfied, isMinimumSatisfied);
        }
    }
}
