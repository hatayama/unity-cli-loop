using System;

namespace io.github.hatayama.UnityCliLoop
{
    [Serializable]
    public record ToolSettingsData
    {
        public string[] disabledTools = Array.Empty<string>();
    }
}
