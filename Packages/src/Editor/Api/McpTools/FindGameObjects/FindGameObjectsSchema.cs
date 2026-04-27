using System.ComponentModel;

namespace io.github.hatayama.uLoopMCP
{
    public class FindGameObjectsSchema : BaseToolSchema
    {
        // Search criteria
        public string NamePattern { get; set; } = "";
        [Description("Search mode. Use Selected(4) to inspect the GameObject(s) currently selected in the Unity Hierarchy. Other modes are Exact(0), Path(1), Regex(2), Contains(3).")]
        public SearchMode SearchMode { get; set; } = SearchMode.Exact;
        public string[] RequiredComponents { get; set; } = new string[0];
        public string Tag { get; set; } = "";
        public int? Layer { get; set; } = null;
        public bool IncludeInactive { get; set; } = false;
        
        // Result control
        public int MaxResults { get; set; } = 20;  // Reduced from 100 to prevent performance issues
        public bool IncludeInheritedProperties { get; set; } = false;
    }
    
    public enum SearchMode
    {
        Exact = 0,      // Exact match (default)
        Path = 1,       // Hierarchy path search (e.g. "Canvas/Button")
        Regex = 2,      // Regular expression
        Contains = 3,   // Partial match
        Selected = 4    // Get currently selected GameObjects in Unity Editor
    }
}
