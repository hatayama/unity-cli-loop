using System;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test-only settings port that exposes every discovered tool.
    /// Guard tests compare the whole catalog, so local tool settings must not hide disabled tools and
    /// turn a developer's preferences into a failure.
    /// </summary>
    internal sealed class AlwaysEnabledToolSettingsPort : IToolSettingsPort
    {
        public bool IsToolEnabled(string toolName)
        {
            return true;
        }

        public void SetToolEnabled(string toolName, bool enabled)
        {
        }

        public string[] GetDisabledTools()
        {
            return Array.Empty<string>();
        }

        public void InvalidateCache()
        {
        }
    }
}
