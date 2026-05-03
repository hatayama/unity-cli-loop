namespace io.github.hatayama.UnityCliLoop
{
    public class GetVersionResponse : BaseToolResponse
    {
        public string UnityVersion { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string DataPath { get; set; } = string.Empty;
        public string PersistentDataPath { get; set; } = string.Empty;
        public string TemporaryCachePath { get; set; } = string.Empty;
        public bool IsEditor { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
    }
}
