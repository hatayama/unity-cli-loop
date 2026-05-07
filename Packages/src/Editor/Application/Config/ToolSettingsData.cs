using System;

namespace io.github.hatayama.UnityCliLoop.Application
{
    [Serializable]
    public record ToolSettingsData
    {
        public string[] disabledTools = Array.Empty<string>();
    }
}
