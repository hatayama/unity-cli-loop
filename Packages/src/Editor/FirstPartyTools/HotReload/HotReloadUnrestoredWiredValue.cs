namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A value wired into an added field that did not come back to the object a scene reload
    /// built in place of its host.
    /// </summary>
    public class HotReloadUnrestoredWiredValue
    {
        /// <summary>Where the host was: its scene path and component, or its asset GUID.</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>The added field, as "Type.field".</summary>
        public string Field { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }
}
