
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Find Game Objects tool.
    /// </summary>
    public class FindGameObjectsSchema : UnityCliLoopToolSchema
    {
        // Search criteria
        public string NamePattern { get; set; } = "";
        public SearchMode SearchMode { get; set; } = SearchMode.Exact;
        public string[] RequiredComponents { get; set; } = new string[0];
        public string Tag { get; set; } = "";
        public int? Layer { get; set; } = null;
        public bool IncludeInactive { get; set; } = false;
        
        // Result control
        public int MaxResults { get; set; } = 20;  // Reduced from 100 to prevent performance issues
        public bool IncludeInheritedProperties { get; set; } = false;
    }
    
}
