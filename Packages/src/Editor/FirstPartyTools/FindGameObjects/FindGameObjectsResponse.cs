using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Find Game Objects tool.
    /// </summary>
    public class FindGameObjectsResponse : UnityCliLoopToolResponse
    {
        public FindGameObjectResult[] Results { get; set; }

        public int TotalFound { get; set; }

        public string ErrorMessage { get; set; }

        // For multiple selection file output
        public string ResultsFilePath { get; set; }

        public string Message { get; set; }

        // Processing errors for objects that failed to serialize
        public ProcessingError[] ProcessingErrors { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Find Game Object behavior.
    /// </summary>
    public class FindGameObjectResult
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public bool IsActive { get; set; }

        public string Tag { get; set; }

        public int Layer { get; set; }

        public ComponentInfo[] Components { get; set; }
    }

    /// <summary>
    /// Provides Processing Error behavior for Unity CLI Loop.
    /// </summary>
    public class ProcessingError
    {
        public string GameObjectName { get; set; }

        public string GameObjectPath { get; set; }

        public string Error { get; set; }
    }
}
