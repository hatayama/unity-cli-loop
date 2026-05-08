using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    [Serializable]
    public record ToolSettingsData
    {
        public string[] disabledTools = Array.Empty<string>();
    }
}
