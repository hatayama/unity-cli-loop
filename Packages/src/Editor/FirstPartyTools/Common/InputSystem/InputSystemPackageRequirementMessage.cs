#nullable enable
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats the shared missing Input System package warning for first-party input tools.
    /// </summary>
    internal static class InputSystemPackageRequirementMessage
    {
        private const string MissingPackageRequirement =
            "requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.";

        public static string Format(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must identify the CLI tool.");
            return $"{toolName} {MissingPackageRequirement}";
        }
    }
}
