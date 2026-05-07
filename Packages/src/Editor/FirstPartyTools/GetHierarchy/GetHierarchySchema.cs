
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Input parameters for GetHierarchy command
    /// </summary>
    public class GetHierarchySchema : UnityCliLoopToolSchema
    {
        public bool IncludeInactive { get; set; } = true;
        public int MaxDepth { get; set; } = -1;
        public string RootPath { get; set; }
        public bool IncludeComponents { get; set; } = true;

        // Advanced output options
        public bool IncludePaths { get; set; } = false;
        public string UseComponentsLut { get; set; } = "auto";
        public bool UseSelection { get; set; } = false;
    }
}
