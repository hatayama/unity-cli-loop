using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Input parameters for GetHierarchy command
    /// </summary>
    public class GetHierarchySchema : BaseToolSchema
    {
        [Description("Whether to include inactive GameObjects in the hierarchy result")]
        public bool IncludeInactive { get; set; } = true;
        
        [Description("Maximum depth to traverse the hierarchy (-1 for unlimited depth)")]
        public int MaxDepth { get; set; } = -1;
        
        [Description("Root GameObject path to start hierarchy traversal from (empty/null for all root objects)")]
        public string RootPath { get; set; }
        
        [Description("Whether to include component information for each GameObject in the hierarchy")]
        public bool IncludeComponents { get; set; } = true;

        // Advanced output options
        [Description("Whether to include path information for nodes (default: false)")]
        public bool IncludePaths { get; set; } = false;

        [Description("Use LUT for components (auto|true|false)")]
        public string UseComponentsLut { get; set; } = "auto";

        [Description("Whether to use GameObject(s) currently selected in the Unity Hierarchy as root(s) for hierarchy traversal. When true, RootPath is ignored.")]
        public bool UseSelection { get; set; } = false;
    }
}
