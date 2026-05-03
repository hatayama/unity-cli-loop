namespace io.github.hatayama.UnityCliLoop
{
    public class FindGameObjectsResponse : BaseToolResponse
    {
        public FindGameObjectResult[] results { get; set; }
        public int totalFound { get; set; }
        public string errorMessage { get; set; }

        // For multiple selection file output
        public string resultsFilePath { get; set; }
        public string message { get; set; }

        // Processing errors for objects that failed to serialize
        public ProcessingError[] processingErrors { get; set; }
    }

    public class FindGameObjectResult
    {
        public string name { get; set; }
        public string path { get; set; }
        public bool isActive { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public ComponentInfo[] components { get; set; }
    }

    public class ProcessingError
    {
        public string gameObjectName { get; set; }
        public string gameObjectPath { get; set; }
        public string error { get; set; }
    }
}