using System;

namespace io.github.hatayama.UnityCliLoop
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
