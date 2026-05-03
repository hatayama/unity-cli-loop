namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Options for advanced GameObject search
    /// </summary>
    public class GameObjectSearchOptions
    {
        public string NamePattern { get; set; } = "";
        public SearchMode SearchMode { get; set; } = SearchMode.Exact;
        public string[] RequiredComponents { get; set; } = new string[0];
        public string Tag { get; set; } = "";
        public int? Layer { get; set; } = null;
        public bool IncludeInactive { get; set; } = false;
        public int MaxResults { get; set; } = 100;
    }
}