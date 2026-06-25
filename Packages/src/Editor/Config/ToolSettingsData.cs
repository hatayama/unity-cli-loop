using System;

namespace io.github.hatayama.uLoopMCP
{
    [Serializable]
    public record ToolSettingsData
    {
        public string[] disabledTools = Array.Empty<string>();
        public string skillCliInvocation = CliConstants.SKILL_CLI_INVOCATION_NPX;
    }
}
