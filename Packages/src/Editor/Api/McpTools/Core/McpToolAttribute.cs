using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Attribute used by the Unity tool registry.
    /// The class name is historical and remains part of the public extension API.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class McpToolAttribute : Attribute
    {
        /// <summary>
        /// Gets whether this tool should only be displayed in development mode
        /// </summary>
        public bool DisplayDevelopmentOnly { get; set; } = false;

        /// <summary>
        /// Gets the specific security setting required to execute this command
        /// </summary>
        public SecuritySettings RequiredSecuritySetting { get; set; } = SecuritySettings.None;

        /// <summary>
        /// Gets or sets the description of this tool
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Public parameterless constructor required for attribute usage.
        /// </summary>
        public McpToolAttribute()
        {
        }
    }
}
