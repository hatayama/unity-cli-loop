using System;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    [Serializable]
    public record ToolSettingsData
    {
        public string[] disabledTools = Array.Empty<string>();
    }
}
