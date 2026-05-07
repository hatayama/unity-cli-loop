using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Find Game Objects tool.
    /// </summary>
    public class FindGameObjectsResponse : UnityCliLoopToolResponse
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

    /// <summary>
    /// Carries the result data produced by Find Game Object behavior.
    /// </summary>
    public class FindGameObjectResult
    {
        public string name { get; set; }
        public string path { get; set; }
        public bool isActive { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public ComponentInfo[] components { get; set; }
    }

    /// <summary>
    /// Provides Processing Error behavior for Unity CLI Loop.
    /// </summary>
    public class ProcessingError
    {
        public string gameObjectName { get; set; }
        public string gameObjectPath { get; set; }
        public string error { get; set; }
    }
}