using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Security settings stored in .uloop/settings.permissions.json.
    /// This file can be git-tracked for team-wide security policy sharing.
    /// </summary>
    [Serializable]
    public record ULoopSettingsData
    {
        public int dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted;
    }
}
